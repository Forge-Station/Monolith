using Content.Server.NPC.HTN;
using Content.Shared._Forge.Xenomorphs;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Stealth.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Xenomorphs;

/// <summary>
/// Grants a caste's actions. Unpossessed xenomorphs spend the combat ones on the HTN target.
/// </summary>
public sealed class ForgeXenoAiSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly MobStateSystem _mobs = default!;
    [Dependency] private readonly MobThresholdSystem _thresholds = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ForgeXenoAiComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ForgeXenoAiComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnMapInit(Entity<ForgeXenoAiComponent> ent, ref MapInitEvent args)
    {
        foreach (var action in ent.Comp.Actions)
        {
            ent.Comp.ActionEntities.Add(_actions.AddAction(ent, action) ?? EntityUid.Invalid);
        }
    }

    private void OnShutdown(Entity<ForgeXenoAiComponent> ent, ref ComponentShutdown args)
    {
        foreach (var action in ent.Comp.ActionEntities)
        {
            if (action.IsValid())
                _actions.RemoveAction(ent.Owner, action);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ForgeXenoAiComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var ai, out var htn))
        {
            if (_timing.CurTime < ai.NextUpdate)
                continue;

            ai.NextUpdate = _timing.CurTime + TimeSpan.FromSeconds(ai.UpdateInterval);

            if (HasComp<ActorComponent>(uid) || _mobs.IsDead(uid))
                continue;

            htn.Blackboard.TryGetValue<EntityUid>("Target", out var target, EntityManager);
            var targetOk = target.IsValid() && !_mobs.IsDead(target);
            var hurt = HurtFraction(uid);

            foreach (var actionEnt in ai.ActionEntities)
            {
                if (!actionEnt.IsValid())
                    continue;

                TryUse(uid, actionEnt, targetOk ? target : null, hurt);
            }
        }
    }

    private void TryUse(EntityUid uid, EntityUid actionEnt, EntityUid? target, float hurt)
    {
        if (TryComp<InstantActionComponent>(actionEnt, out var instant))
        {
            if (!_actions.ValidAction(instant))
                return;

            if (!ShouldUseInstant(uid, instant.Event, hurt))
                return;

            _actions.PerformAction(uid, null, actionEnt, instant, instant.Event, _timing.CurTime, false);
            return;
        }

        if (target == null)
            return;

        if (!Transform(uid).Coordinates.TryDistance(EntityManager, Transform(target.Value).Coordinates, out var distance))
            return;

        if (TryComp<EntityTargetActionComponent>(actionEnt, out var entityAction))
        {
            if (!_actions.ValidAction(entityAction) || distance > entityAction.Range || entityAction.Event == null)
                return;

            if (entityAction.Event is ForgeXenoTransferPlasmaActionEvent)
                return;

            entityAction.Event.Target = target.Value;
            _actions.PerformAction(uid, null, actionEnt, entityAction, entityAction.Event, _timing.CurTime, false);
            return;
        }

        if (!TryComp<WorldTargetActionComponent>(actionEnt, out var world) || world.Event == null)
            return;

        if (world.Event is ForgeXenoConstructActionEvent)
            return;

        if (!_actions.ValidAction(world) || distance > world.Range)
            return;

        var minDistance = world.Event is ForgeXenoLeapActionEvent ? 2f : 0.4f;
        if (distance < minDistance)
            return;

        world.Event.Target = Transform(target.Value).Coordinates;
        _actions.PerformAction(uid, null, actionEnt, world, world.Event, _timing.CurTime, false);
    }

    private bool ShouldUseInstant(EntityUid uid, InstantActionEvent? ev, float hurt)
    {
        switch (ev)
        {
            case ForgeXenoPheromonesActionEvent:
                return !TryComp<ForgeXenoPheromonesComponent>(uid, out var phero) || !phero.Active;

            case ForgeXenoFortifyActionEvent:
                var fortified = TryComp<ForgeXenoFortifyComponent>(uid, out var fortify) && fortify.Active;
                var wantFortify = hurt >= 0.45f && HasHostile(uid, 6f);
                return fortified != wantFortify;

            case ForgeXenoHideActionEvent:
                var hidden = TryComp<StealthComponent>(uid, out var stealth) && stealth.Enabled;
                var wantHide = !HasHostile(uid, 4f);
                return hidden != wantHide;

            case ForgeXenoScreechActionEvent screech:
                return CountHostiles(uid, screech.Range) >= 1;

            case ForgeXenoStompActionEvent stomp:
                return CountHostiles(uid, stomp.Range) >= 1;

            default:
                return false;
        }
    }

    private bool HasHostile(EntityUid uid, float range)
    {
        foreach (var _ in _factions.GetNearbyHostiles(new Entity<NpcFactionMemberComponent?, FactionExceptionComponent?>(uid, null, null), range))
            return true;

        return false;
    }

    private int CountHostiles(EntityUid uid, float range)
    {
        var count = 0;
        foreach (var _ in _factions.GetNearbyHostiles(new Entity<NpcFactionMemberComponent?, FactionExceptionComponent?>(uid, null, null), range))
            count++;

        return count;
    }

    private float HurtFraction(EntityUid uid)
    {
        if (!TryComp<DamageableComponent>(uid, out var damage))
            return 0f;

        if (!_thresholds.TryGetDeadThreshold(uid, out var dead) || dead <= 0)
            return 0f;

        return damage.TotalDamage.Float() / dead.Value.Float();
    }
}
