using Content.Shared._Goobstation.DoAfter;
using System.Linq;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.ActionBlocker;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Pulling.Events;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Content.Shared.Buckle.Components;

namespace Content.Shared._WF.Wolfmed.Consciousness;

/// <summary>
/// What Downed costs: the body stays on the floor and can only reach itself and loose items within reach.
/// Crawling, talking, radio and examining are untouched; doors, buttons, containers, other people, melee and
/// guns are not.
/// </summary>
/// <remarks>
/// Every restriction is an existing ActionBlocker attempt event, so no item, door or gun needed changing.
/// The floor itself is Wolfgate's standing state, the same one knockdown uses.
/// </remarks>
public sealed class WolfmedDownedSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private WolfmedBodyPainSystem _bodyPain = default!;
    [Dependency] private WolfmedDownedClimbSystem _climb = default!; // playtest 3
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Guns a Downed body may still fire (playtest 4): the pistols, revolvers and small energy guns.</summary>
    private static readonly ProtoId<TagPrototype> SidearmTag = "Sidearm";
    [Dependency] private TagSystem _tags = default!; // M2

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedDownedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WolfmedDownedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WolfmedDownedComponent, StandAttemptEvent>(OnStandAttempt);
        SubscribeLocalEvent<WolfmedDownedComponent, InteractionAttemptEvent>(OnInteractionAttempt);
        SubscribeLocalEvent<WolfmedDownedComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<WolfmedDownedComponent, ShotAttemptedEvent>(OnShotAttempt);
        SubscribeLocalEvent<WolfmedDownedComponent, ThrowAttemptEvent>(OnThrowAttempt);
        SubscribeLocalEvent<WolfmedDownedComponent, StartPullAttemptEvent>(OnPullAttempt);
        SubscribeLocalEvent<WolfmedDownedComponent, GetDoAfterDelayMultiplierEvent>(OnGetDelayMultiplier);
        SubscribeLocalEvent<WolfmedDownedComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    /// <summary>
    /// The hands let go on the first tick of being down. Not from the component's own startup: that runs
    /// inside the consciousness evaluation, where a hand's container will not give its item up yet.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_net.IsServer)
            return;

        var query = EntityQueryEnumerator<WolfmedDownedComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Dropped)
                continue;

            comp.Dropped = true;
            DropEverything(uid);
        }
    }

    private void OnStartup(Entity<WolfmedDownedComponent> ent, ref ComponentStartup args)
    {
        // Idempotent: a stun, crit or buckle already has them on the floor, and downing them again played
        // the body-fall sound again. Only a standing body is put down, so only the first Down sounds.
        if (!_standing.IsDown(ent) && !HasComp<KnockedDownComponent>(ent))
            _standing.Down(ent, true, false, false);

        // Playtest 3: on the floor, but not under (and so on top of) the tables.
        _climb.HoldTables(ent);

        // M1a: the alert is the condition alert system's now, one per cause.
        _blocker.UpdateCanMove(ent);

        // Playtest 2: the crawl floor holds only while Downed (WolfmedCrawlSystem).
        _movement.RefreshMovementSpeedModifiers(ent);
    }

    private void OnShutdown(Entity<WolfmedDownedComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        // Anything else holding them down (knockdown, crit, buckling) keeps them there. Asking while a stun
        // still runs only burns a cancelled attempt, and standing them up to drop them again is the spam.
        // Pre-merge review: a body strapped to a bed stays strapped, and one that passed out is held by Critical anyway;
        // standing either up only dropped it again, and unbuckled the bed patient on the way.
        var buckled = TryComp(ent, out BuckleComponent? buckle) && buckle.Buckled;
        var unconscious = TryComp(ent, out WolfmedConsciousnessComponent? consciousness) &&
                          consciousness.State == WolfmedConsciousness.Unconscious;
        if (!buckled && !unconscious && _standing.IsDown(ent) && !HasComp<KnockedDownComponent>(ent))
            _standing.Stand(ent);

        // Playtest 3: still lying for another reason, so upstream's crawl under tables again.
        _climb.ReleaseTables(ent);

        _blocker.UpdateCanMove(ent);
        _movement.RefreshMovementSpeedModifiers(ent);
    }

    /// <summary>
    /// Everything in both hands, on the floor: going down means letting go, the way a knockdown does.
    /// Called on the transition into Downed rather than from this component's startup, where a hand's
    /// container will not give an item up yet. Picking things back up while Downed is still allowed.
    /// </summary>
    public int DropEverything(EntityUid body)
    {
        if (!TryComp(body, out HandsComponent? hands))
            return 0;

        var dropped = 0;
        foreach (var hand in hands.Hands.Values.ToArray())
        {
            if (hand.HeldEntity != null && _hands.TryDrop(body, hand, checkActionBlocker: false, handsComp: hands))
                dropped++;
        }

        return dropped;
    }

    private void OnStandAttempt(EntityUid uid, WolfmedDownedComponent component, StandAttemptEvent args)
    {
        if (component.LifeStage <= ComponentLifeStage.Running)
            args.Cancel();
    }

    /// <summary>
    /// "Self only", plus loose items within reach (M1a, OD7 (b)): the target has to be the body, something
    /// it carries, a pod marked reachable, or an item lying on the floor next to it.
    /// </summary>
    private void OnInteractionAttempt(Entity<WolfmedDownedComponent> ent, ref InteractionAttemptEvent args)
    {
        // AUTODOC: a pod marked reachable is the one thing off the body a Downed player may still touch.
        // M2 (OD7 (c)): and a Downed neighbour, with gauze in hand.
        if (args.Target is { } target && !IsSelfOrCarried(ent, target) &&
            !HasComp<WolfmedDownedReachableComponent>(target) && !IsWithinReach(ent, target) &&
            !CanAidAdjacent(ent, target))
            args.Cancelled = true;
    }

    /// <summary>
    /// M2 (OD7 (c), plan §5.3): a Downed body may press gauze on another Downed body within <c>wolfmed.downed_reach</c>,
    /// and nothing else. The gauze has to be the item in the active hand; the Downed do-after penalty still applies.
    /// </summary>
    public bool CanAidAdjacent(EntityUid user, EntityUid target)
    {
        if (target == user || !HasComp<WolfmedDownedComponent>(target) ||
            _hands.GetActiveItem(user) is not { } item || !_tags.HasTag(item, AidTag))
            return false;

        return _transform.InRange(Transform(user).Coordinates, Transform(target).Coordinates,
            _cfg.GetCVar(WolfmedCVars.DownedReach));
    }

    /// <summary>What a Downed body may press on a Downed neighbour: gauze, and anything else tagged as a dressing like it.</summary>
    private static readonly ProtoId<TagPrototype> AidTag = "Gauze";

    /// <summary>
    /// An item lying loose on the floor within <c>wolfmed.downed_reach</c>: the body's own tile and the ones
    /// next to it. Not anchored, not inside anything.
    /// </summary>
    public bool IsWithinReach(EntityUid user, EntityUid target)
    {
        if (!HasComp<ItemComponent>(target) || _container.IsEntityInContainer(target))
            return false;

        var xform = Transform(target);
        if (xform.Anchored)
            return false;

        return _transform.InRange(Transform(user).Coordinates, xform.Coordinates, _cfg.GetCVar(WolfmedCVars.DownedReach));
    }

    private void OnAttackAttempt(EntityUid uid, WolfmedDownedComponent component, AttackAttemptEvent args)
    {
        // Playtest 4: the gun system asks CanAttack with no target and no weapon before a shot, so a sidearm in the
        // active hand passes here and OnShotAttempt does the rest; melee (a target, a weapon) stays out.
        if (args.Target == null && args.Weapon == null && TryComp(uid, out HandsComponent? hands) &&
            _hands.GetActiveItem((uid, hands)) is { } held && _tags.HasTag(held, SidearmTag))
            return;

        args.Cancel();
    }

    private void OnShotAttempt(Entity<WolfmedDownedComponent> ent, ref ShotAttemptedEvent args)
    {
        // Playtest 4: a sidearm (the Sidearm tag: pistols, revolvers, the small energy guns) fires from the floor;
        // anything bigger needs a stance, so it stays out while Downed.
        if (_tags.HasTag(args.Used, SidearmTag))
            return;

        args.Cancel();
    }

    private void OnThrowAttempt(EntityUid uid, WolfmedDownedComponent component, ThrowAttemptEvent args)
    {
        args.Cancel();
    }

    /// <summary>
    /// Only the attempt raised on the puller. A medic dragging the body away is the point of Downed, so the
    /// event raised on the pullable is left alone.
    /// </summary>
    private void OnPullAttempt(EntityUid uid, WolfmedDownedComponent component, StartPullAttemptEvent args)
    {
        args.Cancel();
    }

    /// <summary>Slower on your back, unless a pain shock's adrenaline is running (OD5).</summary>
    private void OnGetDelayMultiplier(Entity<WolfmedDownedComponent> ent, ref GetDoAfterDelayMultiplierEvent args)
    {
        if (!_bodyPain.HasAdrenaline(ent))
            args.Multiplier *= ent.Comp.DoAfterMultiplier;
    }

    /// <summary>OD5: a pain shock's adrenaline is a burst of crawling, "drag yourself to safety".</summary>
    private void OnRefreshSpeed(Entity<WolfmedDownedComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!_bodyPain.HasAdrenaline(ent))
            return;

        var multiplier = _bodyPain.AdrenalineCrawlMultiplier;
        args.ModifySpeed(multiplier, multiplier);
    }

    /// <summary>True for the body itself and for anything in its hands, pockets, bag or belt.</summary>
    public bool IsSelfOrCarried(EntityUid user, EntityUid target)
    {
        if (target == user)
            return true;

        var parent = Transform(target).ParentUid;
        while (parent.IsValid())
        {
            if (parent == user)
                return true;

            parent = Transform(parent).ParentUid;
        }

        return false;
    }
}
