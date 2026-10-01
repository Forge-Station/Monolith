using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Overheating: a chassis that has taken too much heat runs slow until it sheds it again. The only wound
/// in the system whose treatment is time - nothing in a medkit and no tool touches it, it loses severity
/// on its own tick, faster when something cold reaches the part and in one step when the body is doused.
/// </summary>
/// <remarks>
/// Shared so the part flag is networked and so the dousing entity effect can reach it; every write is
/// server-gated, because wound severity is. Parts carry <see cref="WolfmedOverheatingComponent"/> as the
/// index, following <see cref="WolfmedChemicalBurnSystem"/>.
/// </remarks>
public sealed class WolfmedOverheatingSystem : EntitySystem
{
    /// <summary>Seconds between cooling steps. Cooling is measured in minutes; this only has to be smooth.</summary>
    private const float TickSeconds = 2f;

    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private WoundSystem _wounds = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
        SubscribeLocalEvent<WolfmedPartDamageEvent>(OnPartDamage);
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        // The overheat behavior sits on the base behaviors too, so this matches at any severity, including
        // the zero a removal reports.
        if (!_net.IsServer || !_prototypes.TryIndex(args.Prototype, out var prototype) ||
            !prototype.TryGetBehavior(args.Severity, out WolfmedOverheatBehavior _))
            return;

        Refresh(args.Part);
    }

    // W4's seam. Cold on a hot part is the cheap version of "put it somewhere cold": a cold atmosphere, a
    // cryo spray and a thrown ice pack all arrive here as Cold damage on the part.
    private void OnPartDamage(ref WolfmedPartDamageEvent args)
    {
        if (!_net.IsServer || args.DamageType != "Cold" || args.Amount <= FixedPoint2.Zero ||
            !HasComp<WolfmedOverheatingComponent>(args.Part))
            return;

        var cold = args.Amount.Float();
        Cool(args.Part, behavior => behavior.CoolingPerCold * cold);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        // Buffered: shedding severity raises wound events that can remove the component.
        var due = new List<EntityUid>();
        var query = EntityQueryEnumerator<WolfmedOverheatingComponent>();
        while (query.MoveNext(out var uid, out var overheating))
        {
            overheating.Accumulator += frameTime;
            if (overheating.Accumulator < TickSeconds)
                continue;

            due.Add(uid);
        }

        foreach (var part in due)
        {
            if (!TryComp(part, out WolfmedOverheatingComponent? overheating))
                continue;

            var elapsed = overheating.Accumulator;
            overheating.Accumulator = 0f;
            Cool(part, behavior => behavior.CoolingPerMinute.Float() * elapsed / 60f);
        }
    }

    /// <summary>Arms or disarms the part from the overheating it is actually carrying.</summary>
    public void Refresh(EntityUid part)
    {
        if (TerminatingOrDeleted(part))
            return;

        if (Worst(part) is not { } behavior)
        {
            RemComp<WolfmedOverheatingComponent>(part);
            return;
        }

        var overheating = EnsureComp<WolfmedOverheatingComponent>(part);
        var rate = behavior.CoolingPerMinute.Float();
        if (overheating.CoolingPerMinute == rate)
            return;

        overheating.CoolingPerMinute = rate;
        Dirty(part, overheating);
    }

    /// <summary>
    /// Cools every overheated part on the body by one dousing. Returns how many parts were hot. The caller
    /// is the <c>WolfmedCoolOverheating</c> entity effect on the water touch reaction, so an extinguisher,
    /// a spray bottle, a puddle and a shower all reach this.
    /// </summary>
    public int Douse(EntityUid body)
    {
        if (!_net.IsServer || TerminatingOrDeleted(body))
            return 0;

        var cooled = 0;
        foreach (var (part, _) in _body.GetBodyChildren(body).ToArray())
        {
            if (!HasComp<WolfmedOverheatingComponent>(part))
                continue;

            Cool(part, behavior => behavior.CoolingPerDousing.Float());
            cooled++;
        }

        if (cooled > 0)
            _popup.PopupEntity(Loc.GetString("wolfmed-overheating-doused"), body, body);

        return cooled;
    }

    /// <summary>Whether this part is currently running hot. The analyzer's and a test's read.</summary>
    public bool IsOverheating(EntityUid part) => HasComp<WolfmedOverheatingComponent>(part);

    /// <summary>
    /// Takes severity off every overheating wound on the part, by whatever its own behavior says one step
    /// of this kind is worth. Public so a test can cool a part without waiting.
    /// </summary>
    public void Cool(EntityUid part, Func<WolfmedOverheatBehavior, float> amount)
    {
        if (!_net.IsServer || TerminatingOrDeleted(part))
            return;

        foreach (var wound in _wounds.GetWounds(part).ToArray())
        {
            if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                !_traits.TryGetBehavior(wound.Owner, out WolfmedOverheatBehavior behavior))
                continue;

            var shed = amount(behavior);
            if (shed > 0f)
                _wounds.ChangeSeverity(wound.Owner, -FixedPoint2.New(shed));
        }

        Refresh(part);
    }

    /// <summary>The slowest-cooling overheat on the part, or null when nothing there is hot.</summary>
    private WolfmedOverheatBehavior? Worst(EntityUid part)
    {
        WolfmedOverheatBehavior? found = null;
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                !_prototypes.TryIndex(wound.Comp.Prototype, out var prototype) ||
                !prototype.TryGetBehavior(wound.Comp.Severity, out WolfmedOverheatBehavior behavior))
                continue;

            if (found == null || behavior.CoolingPerMinute < found.CoolingPerMinute)
                found = behavior;
        }

        return found;
    }
}
