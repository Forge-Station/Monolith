using System.Collections.Generic;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Ghost;
using Content.Shared.Item;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>Moving everyone out of the interior.</summary>
public sealed partial class VehicleInteriorSystem
{
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private PullingSystem _pulling = default!;

    /// <summary>Moves everyone inside, and the items loose on the floor, to <paramref name="destination"/>.</summary>
    /// <returns>How many entities were moved.</returns>
    public int EjectAllOccupants(EntityUid vehicle, EntityCoordinates destination)
    {
        if (_net.IsClient ||
            !TryComp(vehicle, out VehicleInteriorComponent? interior) ||
            interior.MapId == MapId.Nullspace ||
            !destination.IsValid(EntityManager))
        {
            return 0;
        }

        var evacuees = CollectEvacuees(interior);

        var moved = 0;
        foreach (var evacuee in evacuees)
        {
            if (TerminatingOrDeleted(evacuee))
                continue;

            if (TryComp(evacuee, out BuckleComponent? buckle) && buckle.BuckledTo != null)
                _buckle.Unbuckle((evacuee, buckle), null);

            if (TryComp(evacuee, out PullableComponent? pullable) && pullable.Puller is { } puller)
                _pulling.TryStopPull(evacuee, pullable, puller);

            if (TryComp(evacuee, out PullerComponent? pulling) &&
                pulling.Pulling is { } pulled &&
                TryComp(pulled, out PullableComponent? pulledComp))
            {
                _pulling.TryStopPull(pulled, pulledComp, evacuee);
            }

            if (_container.IsEntityInContainer(evacuee))
                _container.TryRemoveFromContainer(evacuee, force: true);

            _transform.SetCoordinates(evacuee, destination);
            moved++;
        }

        return moved;
    }

    /// <summary>Every mob and ghost on the interior map and the loose items on its floor.</summary>
    private List<EntityUid> CollectEvacuees(VehicleInteriorComponent interior)
    {
        var evacuees = new List<EntityUid>();

        foreach (var passenger in interior.Passengers)
        {
            if (!TerminatingOrDeleted(passenger) && _transform.GetMapId(passenger) == interior.MapId)
                evacuees.Add(passenger);
        }

        if (!interior.Map.IsValid() || TerminatingOrDeleted(interior.Map))
            return evacuees;

        var walk = new Stack<EntityUid>();
        walk.Push(interior.Map);

        while (walk.TryPop(out var current))
        {
            var children = Transform(current).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (HasComp<MobStateComponent>(child) ||
                    HasComp<MindContainerComponent>(child) ||
                    HasComp<GhostComponent>(child))
                {
                    if (!evacuees.Contains(child))
                        evacuees.Add(child);

                    continue;
                }

                if (IsLooseItem(child, current, interior))
                {
                    evacuees.Add(child);
                    continue;
                }

                walk.Push(child);
            }
        }

        return evacuees;
    }

    /// <summary>An unanchored item lying on the interior's floor (or map), not inside a container or a piece of furniture; the vehicle's own key is left.</summary>
    private bool IsLooseItem(EntityUid uid, EntityUid parent, VehicleInteriorComponent interior)
    {
        return (parent == interior.Grid || parent == interior.Map) &&
               HasComp<ItemComponent>(uid) &&
               !HasComp<VehicleKeyComponent>(uid) &&
               !Transform(uid).Anchored &&
               !_container.IsEntityInContainer(uid);
    }
}
