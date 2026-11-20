using System.Linq;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stunnable;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>
/// What a shock leaves past the surface: current takes the shortest path through the body, cooking tissue
/// on the way, and it does not care what organ is in that path. A strong enough discharge reaches the
/// heart, and the muscles it crosses lock and then let go of whatever they were holding.
/// </summary>
/// <remarks>
/// Server-side: organ health is. The spasm answers <see cref="WolfmedWoundLifecycleEvent"/> because the directed
/// wound-lifecycle subscriptions are taken. M3 (OD15): the heart no longer rolls; each hit's Shock over
/// <c>wolfmed.electric_heart_from</c> takes <c>wolfmed.electric_heart_factor</c> health per point, read off
/// <see cref="WolfmedPartDamageEvent"/>, the broadcast seam for systems that answer a hit.
/// </remarks>
public sealed class WolfmedElectricalBurnSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private OrganHealthSystem _organs = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedStunSystem _stun = default!;

    private const string Shock = "Shock";

    /// <summary>The Shitmed organ slot the current reaches. A body with nothing there takes no heart damage.</summary>
    public const string HeartSlot = "heart";

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
        SubscribeLocalEvent<WolfmedPartDamageEvent>(OnPartDamage);
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        // A second shock merges into the existing wound, so worsening shocks them again.
        if (args.Kind == WolfmedWoundLifecycle.Removed || args.Severity <= args.OldSeverity ||
            !_prototypes.TryIndex(args.Prototype, out var prototype) ||
            !prototype.TryGetBehavior(args.Severity, out WolfmedElectricalShockBehavior behavior) ||
            CompOrNull<BodyPartComponent>(args.Part)?.Body is not { } body)
            return;

        Spasm(body, behavior.Spasm, behavior.DropHeld);
    }

    /// <summary>
    /// M3 (OD15): the band. One hit's Shock on flesh (the parts the internal burn can form on), whole and after
    /// armour, past the line takes its excess times the factor off the heart, every time.
    /// </summary>
    private void OnPartDamage(ref WolfmedPartDamageEvent args)
    {
        if (args.DamageType != Shock || !IsFlesh(args.Part))
            return;

        ShockHeart(args.Body, args.Amount);
    }

    /// <summary>
    /// Heart damage from one hit's Shock: <c>wolfmed.electric_heart_factor</c> × (Shock - <c>wolfmed.electric_heart_from</c>),
    /// nothing at or under the line. Returns what the heart took.
    /// </summary>
    public FixedPoint2 ShockHeart(EntityUid body, FixedPoint2 shock)
    {
        var from = _config.GetCVar(WolfmedCVars.ElectricHeartFrom);
        var factor = _config.GetCVar(WolfmedCVars.ElectricHeartFactor);
        var excess = shock.Float() - from;
        if (excess <= 0f || factor <= 0f)
            return FixedPoint2.Zero;

        var damage = FixedPoint2.New(excess * factor);
        return TryShockOrgan(body, HeartSlot, damage) is not null ? damage : FixedPoint2.Zero;
    }

    private bool IsFlesh(EntityUid part) =>
        TryComp(part, out WoundableComponent? woundable) &&
        _prototypes.TryIndex(woundable.Profile, out var profile) &&
        profile.TreatmentCapabilities.Contains(TreatmentCapability.Biological);

    /// <summary>
    /// Takes health off the organ in the named slot. Returns the organ, or null when the body has none
    /// there. Public so a test or a future effect can reach an organ directly.
    /// </summary>
    public EntityUid? TryShockOrgan(EntityUid body, string slot, FixedPoint2 damage)
    {
        if (damage <= FixedPoint2.Zero || TerminatingOrDeleted(body))
            return null;

        foreach (var (organ, component) in _body.GetBodyOrgans(body).ToArray())
        {
            if (!string.Equals(component.SlotId, slot, StringComparison.OrdinalIgnoreCase) ||
                !TryComp(organ, out WolfmedOrganComponent? health) ||
                health.Health <= FixedPoint2.Zero)
                continue;

            _organs.ChangeHealth((organ, health), -FixedPoint2.Min(damage, health.Health));
            return organ;
        }

        return null;
    }

    /// <summary>Locks the patient up for a moment and, optionally, empties their hands.</summary>
    public void Spasm(EntityUid body, TimeSpan duration, bool dropHeld)
    {
        if (TerminatingOrDeleted(body))
            return;

        if (dropHeld && HasComp<BodyComponent>(body))
        {
            foreach (var held in _hands.EnumerateHeld(body).ToArray())
                _hands.TryDrop(body, held, checkActionBlocker: false);
        }

        _stun.TryUpdateParalyzeDuration(body, duration);
    }
}
