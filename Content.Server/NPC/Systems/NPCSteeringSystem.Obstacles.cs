using Content.Server.Destructible;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Climbing;
using Content.Shared.CombatMode;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.NPC;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Utility;
using ClimbableComponent = Content.Shared.Climbing.Components.ClimbableComponent;
using ClimbingComponent = Content.Shared.Climbing.Components.ClimbingComponent;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCSteeringSystem
{
    /*
     * For any custom path handlers, e.g. destroying walls, opening airlocks, etc.
     * Putting it onto steering seemed easier than trying to make a custom compound task for it.
     * I also considered task interrupts although the problem is handling stuff like pathfinding overlaps
     * Ideally we could do interrupts but that's TODO.
     */

    /*
     * TODO:
     * - Add path cap
     * - Circle cast BFS in LOS to determine targets.
     * - Store last known coordinates of X targets.
     * - Require line of sight for melee
     * - Add new behavior where they move to melee target's last known position (diffing theirs and current)
     *  then do the thing like from dishonored where it gets passed to a search system that opens random stuff.
     *
     * Also need to make sure it picks nearest obstacle path so it starts smashing in front of it.
     */


    private SteeringObstacleStatus TryHandleFlags(EntityUid uid, NPCSteeringComponent component, PathPoly poly)
    {
        DebugTools.Assert(!poly.Data.IsFreeSpace);
        // TODO: Store PathFlags on the steering comp
        // and be able to re-check it.

        var layer = 0;
        var mask = 0;

        if (TryComp<FixturesComponent>(uid, out var manager))
        {
            (layer, mask) = _physics.GetHardCollision(uid, manager);
        }
        else
        {
            return SteeringObstacleStatus.Failed;
        }

        // TODO: Should cache the fact we're doing this somewhere.
        // See https://github.com/space-wizards/space-station-14/issues/11475
        if ((poly.Data.CollisionLayer & mask) != 0x0 ||
            (poly.Data.CollisionMask & layer) != 0x0)
        {
            var id = component.DoAfterId;

            // Still doing what we were doing before.
            var doAfterStatus = _doAfter.GetStatus(id);

            switch (doAfterStatus)
            {
                case DoAfterStatus.Running:
                    return SteeringObstacleStatus.Continuing;
                case DoAfterStatus.Cancelled:
                    return SteeringObstacleStatus.Failed;
            }

            var obstacleEnts = new List<EntityUid>();

            GetObstacleEntities(poly, mask, layer, obstacleEnts);
            var isDoor = (poly.Data.Flags & PathfindingBreadcrumbFlag.Door) != 0x0;
            var isClimbable = (poly.Data.Flags & PathfindingBreadcrumbFlag.Climb) != 0x0;

            if (isDoor && TryGetDoor(poly, obstacleEnts, out var doorEnt, out var door) && door != null)
            {
                var doorStatus = HandleDoor(uid, component, doorEnt, door);
                if (doorStatus != SteeringObstacleStatus.Failed)
                    return doorStatus;
            }

            // Try climbing obstacles. A door we could not open still falls through to smashing.
            if ((component.Flags & PathFlags.Climbing) != 0x0 && isClimbable)
            {
                if (TryComp<ClimbingComponent>(uid, out var climbing))
                {
                    if (climbing.IsClimbing)
                    {
                        return SteeringObstacleStatus.Completed;
                    }
                    else if (climbing.NextTransition != null)
                    {
                        return SteeringObstacleStatus.Continuing;
                    }

                    var climbableQuery = GetEntityQuery<ClimbableComponent>();

                    // Get the relevant obstacle
                    foreach (var ent in obstacleEnts)
                    {
                        if (climbableQuery.TryGetComponent(ent, out var table) &&
                            _climb.CanVault(table, uid, uid, out _) &&
                            _climb.TryClimb(uid, uid, ent, out id, table, climbing))
                        {
                            component.DoAfterId = id;
                            return SteeringObstacleStatus.Continuing;
                        }
                    }
                }

                if (obstacleEnts.Count == 0)
                    return SteeringObstacleStatus.Completed;
            }
            // A railing we failed to vault is still in the way. Smash it instead of giving up the path.
            if ((component.Flags & PathFlags.Smashing) != 0x0)
            {
                if (_melee.TryGetWeapon(uid, out _, out var meleeWeapon) && meleeWeapon.NextAttack <= _timing.CurTime && TryComp<CombatModeComponent>(uid, out var combatMode))
                {
                    // Turning combat off here drops NPCMeleeCombatComponent and they stop dead on the broken tile.
                    var wasInCombat = combatMode.IsInCombatMode;
                    if (!wasInCombat)
                        _combat.SetInCombatMode(uid, true, combatMode);
                    var destructibleQuery = GetEntityQuery<DestructibleComponent>();

                    // TODO: This is a hack around grilles and windows.
                    _random.Shuffle(obstacleEnts);
                    var attackResult = false;

                    foreach (var ent in obstacleEnts)
                    {
                        // TODO: Validate we can damage it
                        if (destructibleQuery.HasComponent(ent))
                        {
                            attackResult = _melee.AttemptLightAttack(uid, uid, meleeWeapon, ent);
                            break;
                        }
                    }

                    if (!wasInCombat)
                        _combat.SetInCombatMode(uid, false, combatMode);

                    // Blocked or the likes?
                    if (!attackResult)
                        return SteeringObstacleStatus.Failed;

                    if (obstacleEnts.Count == 0)
                        return SteeringObstacleStatus.Completed;

                    return SteeringObstacleStatus.Continuing;
                }
            }

            return SteeringObstacleStatus.Failed;
        }

        return SteeringObstacleStatus.Completed;
    }

    /// <summary>
    /// Open, wait for, or pry a door without cancelling the path while it is still moving.
    /// Failed means this door cannot be opened, so smashing may still run.
    /// </summary>
    private SteeringObstacleStatus HandleDoor(EntityUid uid, NPCSteeringComponent component, EntityUid doorEnt, DoorComponent door)
    {
        switch (door.State)
        {
            case DoorState.Open:
                return SteeringObstacleStatus.Completed;
            case DoorState.Opening:
            case DoorState.Emagging:
            case DoorState.Closing:
            case DoorState.Denying:
                return SteeringObstacleStatus.Continuing;
        }

        if (door.State == DoorState.Closed && door.BumpOpen)
            return SteeringObstacleStatus.Completed;

        // A deny just played. Stand until it finishes instead of pathing away and back.
        var now = _timing.CurTime;
        if (now < component.NextDoorAttempt)
            return SteeringObstacleStatus.Continuing;

        // NextDoorAttempt stays set after the wait, so a second refusal can fall through to prying or smashing.
        var alreadyWaited = component.NextDoorAttempt != default;

        if (door.State == DoorState.Closed && (component.Flags & PathFlags.Interact) != 0x0)
        {
            if (_doors.TryOpen(doorEnt, door, uid))
            {
                component.NextDoorAttempt = default;
                return SteeringObstacleStatus.Continuing;
            }

            if (!alreadyWaited)
            {
                component.NextDoorAttempt = now + TimeSpan.FromSeconds(1.25);
                return SteeringObstacleStatus.Continuing;
            }

            component.NextDoorAttempt = default;
        }

        if ((component.Flags & PathFlags.Prying) != 0x0 && door.State != DoorState.Open)
        {
            if (_pryingSystem.TryPry(doorEnt, uid, out var pryId, uid))
            {
                component.DoAfterId = pryId;
                return SteeringObstacleStatus.Continuing;
            }
        }

        return SteeringObstacleStatus.Failed;
    }

    private bool TryGetDoor(PathPoly poly, List<EntityUid> obstacleEnts, out EntityUid doorEnt, out DoorComponent? door)
    {
        var doorQuery = GetEntityQuery<DoorComponent>();
        foreach (var ent in obstacleEnts)
        {
            if (!doorQuery.TryGetComponent(ent, out door))
                continue;

            doorEnt = ent;
            return true;
        }

        if (TryComp<MapGridComponent>(poly.GraphUid, out var grid))
        {
            foreach (var ent in _mapSystem.GetLocalAnchoredEntities(poly.GraphUid, grid, poly.Box))
            {
                if (!doorQuery.TryGetComponent(ent, out door))
                    continue;

                doorEnt = ent;
                return true;
            }
        }

        doorEnt = default;
        door = null;
        return false;
    }

    private void GetObstacleEntities(PathPoly poly, int mask, int layer, List<EntityUid> ents)
    {
        // TODO: Can probably re-use this from pathfinding or something
        if (!TryComp<MapGridComponent>(poly.GraphUid, out var grid))
        {
            return;
        }

        foreach (var ent in _mapSystem.GetLocalAnchoredEntities(poly.GraphUid, grid, poly.Box))
        {
            if (!_physicsQuery.TryGetComponent(ent, out var body) ||
                !body.Hard ||
                !body.CanCollide ||
                (body.CollisionMask & layer) == 0x0 && (body.CollisionLayer & mask) == 0x0)
            {
                continue;
            }

            ents.Add(ent);
        }
    }

    private enum SteeringObstacleStatus : byte
    {
        Completed,
        Failed,
        Continuing
    }
}
