using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>Damages a part with an open chemical burn every few seconds until the patient is washed off.</summary>
// The part carries WolfmedChemicalBurnComponent while the burn is open. Anything that puts water on the patient
// washes it off: a splash, a spray bottle, an extinguisher, a puddle.
/// <remarks>
/// The wash hook is <see cref="Content.Shared._WF.Wolfmed.EntityEffects.WashChemicalBurns"/>, an entity
/// effect on the humanoid base's existing water touch reaction, so every way water already reaches a mob
/// works without a second code path. Shared for that effect's sake; every write is server-gated.
/// </remarks>
public sealed class WolfmedChemicalBurnSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WoundDamageRoutingSystem _routing = default!;
    [Dependency] private WoundSystem _wounds = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        if (!_net.IsServer || !_prototypes.TryIndex(args.Prototype, out var prototype) ||
            !prototype.TryGetBehavior(args.Severity, out WolfmedCausticResidueBehavior _))
            return;

        Refresh(args.Part);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        // Gated in the query loop, not in Tick: walking a part's wounds costs a prototype lookup and a
        // behavior iterator per wound, and that is due once every few seconds, not every frame.
        List<EntityUid>? due = null;
        var query = EntityQueryEnumerator<WolfmedChemicalBurnComponent>();
        while (query.MoveNext(out var uid, out var residue))
        {
            residue.Accumulator += frameTime;
            if (residue.Accumulator < residue.Interval)
                continue;

            // Buffered: applying the damage raises wound events that can add or remove the component.
            (due ??= new List<EntityUid>()).Add(uid);
        }

        if (due == null)
            return;

        foreach (var part in due)
            Tick(part);
    }

    /// <summary>
    /// M5 (P21): the part and the wound the residue is biting through right now. The wound rules put every wound
    /// the residue's damage makes into this one instead of a plain burn.
    /// </summary>
    public (EntityUid Part, ProtoId<WoundPrototype> Wound)? ResidueTarget { get; private set; }

    private void Tick(EntityUid part)
    {
        if (TerminatingOrDeleted(part) ||
            !TryComp(part, out WolfmedChemicalBurnComponent? residue) ||
            CompOrNull<BodyPartComponent>(part)?.Body is not { } body)
            return;

        residue.Accumulator = 0f;
        if (FindResidue(part, out var wound) is not { } behavior)
        {
            RemComp<WolfmedChemicalBurnComponent>(part);
            return;
        }

        residue.Interval = Interval(behavior);
        if (behavior.Damage.Empty)
            return;

        // M5 (P21): the residue deepens its own chemical burn, never the plain one.
        ResidueTarget = (part, wound);
        try
        {
            _routing.TryApplyPartDamage(body, part, behavior.Damage, healWounds: false);
        }
        finally
        {
            ResidueTarget = null;
        }
    }

    /// <summary>Arms or disarms the part from the chemical burns it is actually carrying.</summary>
    public void Refresh(EntityUid part)
    {
        if (TerminatingOrDeleted(part))
            return;

        if (FindResidue(part) is not { } behavior)
        {
            RemComp<WolfmedChemicalBurnComponent>(part);
            return;
        }

        EnsureComp<WolfmedChemicalBurnComponent>(part).Interval = Interval(behavior);
    }

    /// <summary>The behavior's interval in seconds, floored so a zero in data cannot make this tick every frame.</summary>
    private static float Interval(WolfmedCausticResidueBehavior behavior) =>
        Math.Max(0.5f, (float) behavior.Interval.TotalSeconds);

    /// <summary>
    /// Rinses every part of the body clean. The burns stay and heal like any other wound; what stops is the
    /// residue eating away at them. Returns how many parts had something on them.
    /// </summary>
    public int Wash(EntityUid body)
    {
        if (!_net.IsServer || TerminatingOrDeleted(body))
            return 0;

        var washed = 0;
        foreach (var (part, _) in _body.GetBodyChildren(body).ToArray())
        {
            if (!HasComp<WolfmedChemicalBurnComponent>(part))
                continue;

            RemComp<WolfmedChemicalBurnComponent>(part);
            washed++;
        }

        if (washed > 0)
            _popup.PopupEntity(Loc.GetString("wolfmed-chemical-burn-washed"), body, body);

        return washed;
    }

    /// <summary>The worst residue on the part, or null when nothing there is still corroding.</summary>
    private WolfmedCausticResidueBehavior? FindResidue(EntityUid part) => FindResidue(part, out _);

    /// <summary>The worst residue on the part and the wound prototype carrying it.</summary>
    private WolfmedCausticResidueBehavior? FindResidue(EntityUid part, out ProtoId<WoundPrototype> source)
    {
        WolfmedCausticResidueBehavior? found = null;
        source = default;
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                !_prototypes.TryIndex(wound.Comp.Prototype, out var prototype) ||
                !prototype.TryGetBehavior(wound.Comp.Severity, out WolfmedCausticResidueBehavior behavior))
                continue;

            if (found == null || behavior.Interval < found.Interval)
            {
                found = behavior;
                source = wound.Comp.Prototype;
            }
        }

        return found;
    }
}
