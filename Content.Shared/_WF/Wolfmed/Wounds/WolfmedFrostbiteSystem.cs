using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared.FixedPoint;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>Frostbite numbs a part while the wound is open and flags necrosis risk at its deepest stage.</summary>
// The part carries WolfmedFrostbiteComponent, which pins pain suppression the way painkillers do, so a numb limb
// reads quiet on an analyzer and the patient stops noticing what else is wrong with it.
/// <remarks>
/// Shared so the component and its flag are networked; every write is server-gated, because pain
/// suppression is.
/// </remarks>
public sealed class WolfmedFrostbiteSystem : EntitySystem
{
    /// <summary>Suppression key, so frostbite numbness stacks beside a painkiller rather than over it.</summary>
    public const string SuppressionKey = "WolfmedFrostbite";

    /// <summary>Numbness is topped up on a slow tick; nothing here needs frame resolution.</summary>
    private const float TickSeconds = 2f;

    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private WoundSystem _wounds = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        // The numbness behavior sits on the wound's base behaviors, so this matches at any severity,
        // including the zero a removal reports.
        if (!_net.IsServer || !_prototypes.TryIndex(args.Prototype, out var prototype) ||
            !prototype.TryGetBehavior(args.Severity, out WolfmedNumbnessBehavior _))
            return;

        Refresh(args.Part);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        // Buffered: Refresh adds or removes the component being walked.
        List<EntityUid>? due = null;
        var query = EntityQueryEnumerator<WolfmedFrostbiteComponent>();
        while (query.MoveNext(out var uid, out var frostbite))
        {
            frostbite.Accumulator += frameTime;
            if (frostbite.Accumulator < TickSeconds)
                continue;

            frostbite.Accumulator = 0f;
            (due ??= new List<EntityUid>()).Add(uid);
        }

        if (due == null)
            return;

        foreach (var part in due)
        {
            if (!TerminatingOrDeleted(part))
                Refresh(part);
        }
    }

    /// <summary>
    /// Recomputes the part's numbness and necrosis risk from the frostbite it is actually carrying, and
    /// tops the pain suppression back up to what the current stage asks for. Public so a test can drive it.
    /// </summary>
    public void Refresh(EntityUid part)
    {
        if (TerminatingOrDeleted(part))
            return;

        var suppression = FixedPoint2.Zero;
        var decay = TimeSpan.Zero;
        var risk = 0f;
        var onset = TimeSpan.Zero;

        foreach (var wound in _wounds.GetWounds(part).ToArray())
        {
            if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                !_prototypes.TryIndex(wound.Comp.Prototype, out var prototype))
                continue;

            if (prototype.TryGetBehavior(wound.Comp.Severity, out WolfmedNumbnessBehavior numbness) &&
                numbness.Suppression > suppression)
            {
                suppression = numbness.Suppression;
                decay = numbness.Decay;
            }

            if (prototype.TryGetBehavior(wound.Comp.Severity, out WolfmedNecrosisRiskBehavior necrosis) &&
                necrosis.RiskMultiplier > risk)
            {
                risk = necrosis.RiskMultiplier;
                onset = necrosis.Onset;
            }
        }

        if (suppression <= FixedPoint2.Zero && risk <= 0f)
        {
            // The suppression already on the part is left to decay on its own: feeling comes back slowly.
            RemComp<WolfmedFrostbiteComponent>(part);
            return;
        }

        var component = EnsureComp<WolfmedFrostbiteComponent>(part);
        if (component.NecrosisRisk != risk || component.NecrosisOnset != onset)
        {
            component.NecrosisRisk = risk;
            component.NecrosisOnset = onset;
            Dirty(part, component);
        }

        if (suppression <= FixedPoint2.Zero || decay <= TimeSpan.Zero ||
            !TryComp(part, out PainComponent? pain))
            return;

        // Top-up rather than a fresh dose: SuppressPain accumulates, so re-applying the full amount every
        // tick would numb the limb further and further instead of holding it at the stage's value.
        var current = pain.SuppressionModifiers.TryGetValue(SuppressionKey, out var modifier)
            ? modifier.Amount
            : FixedPoint2.Zero;
        if (current < suppression)
            _pain.SuppressPain((part, pain), SuppressionKey, suppression - current, decay);
    }
}
