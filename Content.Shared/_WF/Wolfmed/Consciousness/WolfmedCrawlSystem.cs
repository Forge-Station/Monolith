using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._White.Standing;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Consciousness;

/// <summary>
/// Raised on a body after every other speed modifier, from the marked line in
/// <c>MovementSpeedModifierSystem.RefreshMovementSpeedModifiers</c>. A handler may raise or lower the final
/// modifiers; the base speeds are there to turn a floor speed into a modifier.
/// </summary>
[ByRefEvent]
public record struct WolfmedSpeedFloorEvent(float BaseWalk, float BaseSprint, float Walk, float Sprint);

/// <summary>Lets a Downed body always crawl, however hurt, until it is Unconscious or has no working limbs.</summary>
// While Downed the crawl never falls under wolfmed.crawl_floor of the normal Downed crawl; with no working leg the
// body drags itself on its arms at exactly that; with no working arm and no working leg it cannot move.
/// <remarks>
/// Shitmed sets a body's base speed from its enabled legs, so a body with none has a base of 0 and no modifier can
/// move it. The marked line in <c>SharedBodySystem.UpdateMovementSpeed</c> gives a wound host
/// <see cref="LeglessBase"/> instead, and this system sets the actual speed.
/// </remarks>
public sealed class WolfmedCrawlSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private BodyPartFunctionalitySystem _functionality = default!;
    [Dependency] private WolfmedBodyPainSystem _bodyPain = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    /// <summary>The base a legless wound host gets in place of Shitmed's 0: a healthy body's.</summary>
    public static (float Walk, float Sprint, float Acceleration) LeglessBase =>
        (MovementSpeedModifierComponent.DefaultBaseWalkSpeed, MovementSpeedModifierComponent.DefaultBaseSprintSpeed,
            MovementSpeedModifierComponent.DefaultAcceleration);

    /// <summary>Bodies whose limbs were switched on or off this tick, refreshed once the switch has landed.</summary>
    private readonly HashSet<EntityUid> _refresh = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundHostComponent, WolfmedSpeedFloorEvent>(OnSpeedFloor);
        SubscribeLocalEvent<WoundableComponent, BodyPartEnableChangedEvent>(OnPartEnableChanged);
    }

    /// <summary>
    /// An arm switching on or off moves the legless crawl. The part's own flag is set by another subscriber, maybe
    /// after this one, so the refresh waits for the update.
    /// </summary>
    private void OnPartEnableChanged(Entity<WoundableComponent> part, ref BodyPartEnableChangedEvent args)
    {
        if (CompOrNull<BodyPartComponent>(part)?.Body is { } body)
            _refresh.Add(body);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_refresh.Count == 0)
            return;

        foreach (var body in _refresh)
        {
            if (!TerminatingOrDeleted(body))
                _movement.RefreshMovementSpeedModifiers(body);
        }

        _refresh.Clear();
    }

    private void OnSpeedFloor(Entity<WoundHostComponent> ent, ref WolfmedSpeedFloorEvent args)
    {
        if (!TryComp(ent, out BodyComponent? body) || body.RequiredLegs <= 0 ||
            args.BaseWalk <= 0f || args.BaseSprint <= 0f)
            return;

        var downed = HasComp<WolfmedDownedComponent>(ent);
        if (!downed)
            return;

        var legs = HasWorkingLimb(ent, BodyPartType.Leg);
        if (!legs)
        {
            // Arms only: exactly the floor, or nothing with no arm to pull with.
            var arms = HasWorkingLimb(ent, BodyPartType.Arm);
            var (walk, sprint) = arms ? Floor(ent, downed) : (0f, 0f);
            args.Walk = walk / args.BaseWalk;
            args.Sprint = sprint / args.BaseSprint;
            return;
        }

        var (floorWalk, floorSprint) = Floor(ent, true);
        args.Walk = MathF.Max(args.Walk, floorWalk / args.BaseWalk);
        args.Sprint = MathF.Max(args.Sprint, floorSprint / args.BaseSprint);
    }

    /// <summary>The floor as speeds: the CVar's share of a healthy body's crawl, with a running adrenaline burst.</summary>
    private (float Walk, float Sprint) Floor(EntityUid body, bool downed)
    {
        var share = MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.CrawlFloor)) *
                    (CompOrNull<LayingDownComponent>(body)?.SpeedModify ?? 1f);
        if (downed && _bodyPain.HasAdrenaline(body))
            share *= _bodyPain.AdrenalineCrawlMultiplier;

        return (share * MovementSpeedModifierComponent.DefaultBaseWalkSpeed,
            share * MovementSpeedModifierComponent.DefaultBaseSprintSpeed);
    }

    /// <summary>A limb of this type is attached, switched on, and not disabled by its wounds.</summary>
    public bool HasWorkingLimb(EntityUid body, BodyPartType type)
    {
        foreach (var (part, bodyPart) in _body.GetBodyChildren(body))
        {
            if (bodyPart.PartType == type && bodyPart.Enabled &&
                _functionality.GetState(part) != BodyPartFunctionalityState.Disabled)
                return true;
        }

        return false;
    }
}

/// <summary>
/// Playtest 2: what counts towards Shitmed switching a wound host's limb off. Burns never do: they slow the hand or
/// the leg through its wounds instead (<see cref="Wounds.WolfmedLimbPenaltyBehavior"/>), and the limb works until it
/// is actually destroyed or crumbles. Read by the marked line in <c>SharedBodySystem.CheckBodyPart</c>.
/// </summary>
public static class WolfmedLimbIntegrity
{
    public static readonly ProtoId<DamageGroupPrototype> BurnGroup = "Burn";

    /// <summary>The part's total damage less its burn group.</summary>
    public static FixedPoint2 ForEnable(DamageableComponent damageable, IPrototypeManager prototypes)
    {
        var total = damageable.TotalDamage;
        if (!prototypes.TryIndex(BurnGroup, out var group))
            return total;

        foreach (var type in group.DamageTypes)
        {
            if (damageable.Damage.DamageDict.TryGetValue(type, out var amount))
                total -= amount;
        }

        return FixedPoint2.Max(total, FixedPoint2.Zero);
    }
}
