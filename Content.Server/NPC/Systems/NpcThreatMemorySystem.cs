using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.Storage.Components;
using Content.Shared.Examine;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.NPC;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Robust.Server.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server.NPC.Systems;

/// <summary>
/// Remembers where a walking NPC last saw a target, and which way gunfire came from.
/// </summary>
public sealed class NpcThreatMemorySystem : EntitySystem
{
    [Dependency] private ContainerSystem _container = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ProjectileComponent, ProjectileHitEvent>(OnProjectileHit);
        SubscribeLocalEvent<HitscanBasicRaycastComponent, HitscanRaycastFiredEvent>(OnHitscanFired);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ActiveNPCComponent, HTNComponent, InputMoverComponent>();
        while (query.MoveNext(out var uid, out _, out var htn, out _))
        {
            WatchHiding(uid, htn);
            RememberTarget(htn);
        }
    }

    private void OnProjectileHit(Entity<ProjectileComponent> ent, ref ProjectileHitEvent args)
    {
        RememberShot(args.Target, args.Shooter, ent);
    }

    private void OnHitscanFired(Entity<HitscanBasicRaycastComponent> ent, ref HitscanRaycastFiredEvent args)
    {
        if (args.Canceled || args.HitEntity is not { } hit)
            return;

        RememberShot(hit, args.Shooter, null, args.FromCoordinates);
    }

    private void RememberShot(EntityUid target, EntityUid? shooter, Entity<ProjectileComponent>? projectile, EntityCoordinates? from = null)
    {
        if (!TryComp<HTNComponent>(target, out var htn) || !HasComp<InputMoverComponent>(target))
            return;

        if (shooter is { } shot && shot.IsValid() && _factions.IsEntityFriendly(target, shot))
            return;

        if (!TryGetShotSource(target, shooter, projectile, from, out var coords))
            return;

        var blackboard = htn.Blackboard;
        blackboard.ReadOnly = false;
        blackboard.SetValue(NPCBlackboard.IncomingFireCoordinates, coords);
        blackboard.Remove<EntityCoordinates>(NPCBlackboard.LastSeenCoordinates);
        blackboard.Remove<EntityCoordinates>(NPCBlackboard.InvestigateCoordinates);
        _htn.Replan(htn);
    }

    private bool TryGetShotSource(
        EntityUid target,
        EntityUid? shooter,
        Entity<ProjectileComponent>? projectile,
        EntityCoordinates? from,
        out EntityCoordinates coords)
    {
        var targetXform = Transform(target);
        if (shooter is { } shot &&
            shot.IsValid() &&
            !TerminatingOrDeleted(shot) &&
            Transform(shot).MapID == targetXform.MapID)
        {
            coords = Transform(shot).Coordinates;
            return true;
        }

        if (from is { } originCoords &&
            originCoords.IsValid(EntityManager) &&
            _transform.ToMapCoordinates(originCoords).MapId == targetXform.MapID)
        {
            coords = originCoords;
            return true;
        }

        if (projectile == null || !TryComp<PhysicsComponent>(projectile.Value, out _))
        {
            coords = default;
            return false;
        }

        var velocity = _physics.GetMapLinearVelocity(projectile.Value);
        if (velocity.LengthSquared() < 0.01f)
        {
            coords = default;
            return false;
        }

        var origin = _transform.GetWorldPosition(targetXform);
        var mapCoords = new MapCoordinates(origin - velocity.Normalized() * 8f, targetXform.MapID);
        coords = _transform.ToCoordinates(mapCoords);
        return coords.IsValid(EntityManager);
    }

    private void RememberTarget(HTNComponent htn)
    {
        var blackboard = htn.Blackboard;

        if (blackboard.ContainsKey(NPCBlackboard.IncomingFireCoordinates))
        {
            if (!blackboard.ContainsKey(NPCBlackboard.LastSeenCoordinates))
                return;

            blackboard.ReadOnly = false;
            blackboard.Remove<EntityCoordinates>(NPCBlackboard.LastSeenCoordinates);
            return;
        }

        // The combat target is already the mob HTN can see. Another raycast here was most of the tick.
        if (blackboard.TryGetValue<EntityUid>("Target", out var target, EntityManager) &&
            target.IsValid() &&
            !TerminatingOrDeleted(target) &&
            (!TryComp<MobStateComponent>(target, out var mob) || mob.CurrentState == MobState.Alive))
        {
            var coords = Transform(target).Coordinates;
            if (blackboard.TryGetValue<EntityCoordinates>(NPCBlackboard.LastSeenCoordinates, out var last, EntityManager) &&
                last.EntityId == coords.EntityId &&
                (last.Position - coords.Position).LengthSquared() < 0.04f)
                return;

            blackboard.ReadOnly = false;
            blackboard.SetValue(NPCBlackboard.LastSeenCoordinates, coords);
            blackboard.Remove<EntityCoordinates>(NPCBlackboard.InvestigateCoordinates);
            return;
        }

        if (!blackboard.TryGetValue<EntityCoordinates>(NPCBlackboard.LastSeenCoordinates, out var lost, EntityManager))
            return;

        blackboard.ReadOnly = false;
        blackboard.Remove<EntityCoordinates>(NPCBlackboard.LastSeenCoordinates);

        if (blackboard.ContainsKey(NPCBlackboard.InvestigateCoordinates))
            return;

        blackboard.SetValue(NPCBlackboard.InvestigateCoordinates, lost);
        _htn.Replan(htn);
    }

    /// <summary>
    /// Remember a hostile only when they were the current target in the open and then shut the crate in view.
    /// One line-of-sight check, and only on that transition. Not a vision scan of every nearby faction member.
    /// </summary>
    private void WatchHiding(EntityUid uid, HTNComponent htn)
    {
        var hasTarget = htn.Blackboard.TryGetValue<EntityUid>("Target", out var current, EntityManager);
        if (!TryComp<NpcHidingMemoryComponent>(uid, out var memory))
        {
            if (!hasTarget || !IsLivingLoose(current))
                return;

            memory = EnsureComp<NpcHidingMemoryComponent>(uid);
        }

        var radius = htn.Blackboard.GetValueOrDefault<float>(htn.Blackboard.GetVisionRadiusKey(EntityManager), EntityManager);
        var range = radius + 0.5f;
        var rangeSq = range * range;

        var seen = memory.SeenScratch;
        seen.Clear();

        if (hasTarget && IsLivingLoose(current) && InRange(uid, current, rangeSq))
            seen.Add(current);

        foreach (var previous in memory.SeenLoose)
        {
            if (!previous.IsValid() || TerminatingOrDeleted(previous))
                continue;

            if (!TryComp<MobStateComponent>(previous, out var mob) || mob.CurrentState != MobState.Alive)
                continue;

            if (_container.TryGetContainingContainer(previous, out var container) &&
                HasComp<EntityStorageComponent>(container.Owner))
            {
                if (InRange(uid, container.Owner, rangeSq) &&
                    _examine.InRangeUnOccluded(uid, container.Owner, range, null))
                    memory.WitnessedContainers.Add(container.Owner);
                continue;
            }

            if (InRange(uid, previous, rangeSq))
                seen.Add(previous);
        }

        (memory.SeenLoose, memory.SeenScratch) = (seen, memory.SeenLoose);

        if (memory.WitnessedContainers.Count > 0)
            memory.WitnessedContainers.RemoveWhere(crate => !StillHidesSomeone(crate));
    }

    private bool IsLivingLoose(EntityUid uid)
    {
        if (!uid.IsValid() || TerminatingOrDeleted(uid))
            return false;

        if (!TryComp<MobStateComponent>(uid, out var mob) || mob.CurrentState != MobState.Alive)
            return false;

        return !_container.TryGetContainingContainer(uid, out _);
    }

    private bool InRange(EntityUid from, EntityUid to, float rangeSq)
    {
        var origin = Transform(from);
        var other = Transform(to);
        if (origin.MapID != other.MapID)
            return false;

        return (_transform.GetWorldPosition(origin) - _transform.GetWorldPosition(other)).LengthSquared() <= rangeSq;
    }

    private bool StillHidesSomeone(EntityUid crate)
    {
        if (!crate.IsValid() || TerminatingOrDeleted(crate) || !HasComp<EntityStorageComponent>(crate))
            return false;

        if (!_container.TryGetContainer(crate, SharedEntityStorageSystem.ContainerName, out var contents))
            return false;

        foreach (var ent in contents.ContainedEntities)
        {
            if (TryComp<MobStateComponent>(ent, out var mob) && mob.CurrentState == MobState.Alive)
                return true;
        }

        return false;
    }
}
