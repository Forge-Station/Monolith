using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._WF.Traders;
using Content.Shared.DoAfter;
using Content.Shared.Devour;
using Content.Shared.Devour.Components;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.Popups;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._WF.Traders;

/// <summary>
/// A trader who eats the people left dead or dying near his post. He walks to the body through his HTN's
/// <see cref="TraderDevourComponent.TargetKey"/>, swallows it with his devourer's do-after, and gives everyone he has
/// eaten back up when he dies.
/// </summary>
public sealed class TraderDevourSystem : EntitySystem
{
    private static readonly TimeSpan ThinkInterval = TimeSpan.FromSeconds(1);

    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TraderSystem _trader = default!;

    private readonly HashSet<Entity<MobStateComponent>> _bodies = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TraderDevourComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<TraderDevourComponent, EntityTerminatingEvent>(OnTerminating);
    }

    private void OnMobStateChanged(Entity<TraderDevourComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            Disgorge(ent);
    }

    private void OnTerminating(Entity<TraderDevourComponent> ent, ref EntityTerminatingEvent args)
    {
        Disgorge(ent);
    }

    /// <summary>Gives up everyone he has eaten, where he stands.</summary>
    private void Disgorge(EntityUid uid)
    {
        if (_container.TryGetContainer(uid, "stomach", out var stomach))
            _container.EmptyContainer(stomach, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<TraderDevourComponent, DevourerComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var devour, out var devourer, out var htn))
        {
            if (now < devour.NextThink)
                continue;

            devour.NextThink = now + ThinkInterval;
            Think((uid, devour), devourer, htn, now);
        }
    }

    private void Think(Entity<TraderDevourComponent> ent, DevourerComponent devourer, HTNComponent htn, TimeSpan now)
    {
        // His post is where he first stands awake: a site is laid and turned before its entities unpause.
        ent.Comp.Home ??= _transform.GetMapCoordinates(ent);

        // Dead, downed, or running from someone, he has other things on his mind.
        if (!_mobState.IsAlive(ent)
            || TryComp<TraderFleeComponent>(ent, out var flee) && flee.Threat != null
            || ent.Comp.Home is not { } home)
        {
            Forget(ent, htn);
            return;
        }

        if (ent.Comp.Meal is { } eating && (!IsMeal(eating, home, ent.Comp.Radius) || now - ent.Comp.MealSince > ent.Comp.GiveUp))
        {
            Forget(ent, htn);
        }

        if (ent.Comp.Meal == null)
        {
            if (FindMeal(ent, home) is not { } found)
                return;

            ent.Comp.Meal = found;
            ent.Comp.MealSince = now;
        }

        var target = ent.Comp.Meal!.Value;
        var here = _transform.GetMapCoordinates(ent);
        var there = _transform.GetMapCoordinates(target);
        if ((here.Position - there.Position).Length() > ent.Comp.Reach)
        {
            var coords = Transform(target).Coordinates;
            if (!htn.Blackboard.TryGetValue<EntityCoordinates>(TraderDevourComponent.TargetKey, out var current, EntityManager)
                || !current.Equals(coords))
            {
                if (!HasComp<ActiveNPCComponent>(ent))
                    _npc.WakeNPC(ent, htn);

                _npc.SetBlackboard(ent, TraderDevourComponent.TargetKey, coords, htn);
                _htn.Replan(htn);
            }

            return;
        }

        // At the table: whoever he was serving can wait.
        if (TryComp<TraderComponent>(ent, out var trader) && trader.Customer != null)
            _trader.EndConversation((ent, trader), false);

        htn.Blackboard.Remove<EntityCoordinates>(TraderDevourComponent.TargetKey);
        var args = new DoAfterArgs(EntityManager, ent, devourer.DevourTime, new DevourDoAfterEvent(), ent, target: target, used: ent)
        {
            BreakOnMove = true,
            // Each look round tries again; the one under way carries on.
            CancelDuplicate = false,
        };

        if (_doAfter.TryStartDoAfter(args))
            _popup.PopupEntity(Loc.GetString("trader-devour-start", ("trader", ent.Owner), ("target", target)), ent, PopupType.LargeCaution);
    }

    private void Forget(Entity<TraderDevourComponent> ent, HTNComponent htn)
    {
        ent.Comp.Meal = null;
        htn.Blackboard.Remove<EntityCoordinates>(TraderDevourComponent.TargetKey);
    }

    /// <summary>The nearest person lying dead or dying in his place.</summary>
    private EntityUid? FindMeal(Entity<TraderDevourComponent> ent, MapCoordinates home)
    {
        _bodies.Clear();
        _lookup.GetEntitiesInRange(home, ent.Comp.Radius, _bodies);
        var here = _transform.GetMapCoordinates(ent).Position;
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var body in _bodies)
        {
            if (body.Owner == ent.Owner || !IsMeal(body, home, ent.Comp.Radius))
                continue;

            var distance = (_transform.GetMapCoordinates(body).Position - here).Length();
            if (distance >= bestDistance)
                continue;

            best = body;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>A person, not another trader, down or dead, lying loose within his place.</summary>
    private bool IsMeal(EntityUid uid, MapCoordinates home, float radius)
    {
        if (TerminatingOrDeleted(uid)
            || !HasComp<HumanoidAppearanceComponent>(uid)
            || HasComp<TraderComponent>(uid)
            || _mobState.IsAlive(uid)
            || _container.IsEntityInContainer(uid))
            return false;

        var at = _transform.GetMapCoordinates(uid);
        return at.MapId == home.MapId && (at.Position - home.Position).Length() <= radius;
    }
}
