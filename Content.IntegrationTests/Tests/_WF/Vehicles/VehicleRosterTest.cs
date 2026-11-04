#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._Mono.Cleanup;
using Content.Server._WF.Vehicles.Safety;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared._WF.Vehicles.Weapons;
using Content.Shared.Atmos;
using Content.Shared.Buckle.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Gravity;
using Content.Shared.Interaction;
using Content.Shared.Tiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>
/// Every concrete vehicle in the roster, one after the other: its parts, turret guns included, are fitted and it can run, its interior loads with a
/// driver seat, an exit marker for each doorway and air to breathe, the safety system shields the grid and accepts every
/// landing spot, exit and seat, every ammo loader it places has its component and window, every doorway takes someone in and
/// puts them back out, and deleting the vehicle with someone aboard leaves them where it stood.
/// </summary>
[TestFixture]
public sealed class VehicleRosterTest
{
    // The vehicle stands here on a wide plating grid; its doorways are at this spot plus their offsets.
    private static readonly Vector2 VehicleSpot = new(8.5f, 8.5f);

    private const string InteriorFolder = "/Maps/_WF/Vehicles/Interiors/";
    private const string TestInterior = "wf_test_interior.yml";

    /// <summary>The farthest a doorway's landing spot may be from the nearest exit marker with its entry index; the box van's front doors are 1.8 away.</summary>
    private const float MaxLandingDistance = 2.5f;

    [Test]
    public async Task EveryVehicleRunsAndTakesPeopleThroughItsDoors()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;

            for (var x = -8; x < 24; x++)
            {
                for (var y = -8; y < 24; y++)
                {
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
                }
            }
        });

        var roster = new List<RosterEntry>();
        await server.WaitPost(() =>
        {
            var factory = entMan.ComponentFactory;
            foreach (var proto in server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract ||
                    !proto.TryComp<RMCVehicleComponent>(out _, factory) ||
                    !proto.TryComp<VehicleEnterComponent>(out var enter, factory))
                {
                    continue;
                }

                // The vehicles the other vehicle tests declare use the small test interior.
                var interiorPath = enter.InteriorPath;
                var path = interiorPath.ToString();
                if (!path.StartsWith(InteriorFolder) || path.EndsWith(TestInterior))
                    continue;

                var starting = new Dictionary<string, string>();
                if (proto.TryComp<ItemSlotsComponent>(out var itemSlots, factory))
                {
                    foreach (var (id, slot) in itemSlots.Slots)
                    {
                        if (slot.StartingItem is { } item)
                            starting[id] = item;
                    }
                }

                // A part with slots of its own, a turret, starts with parts too. Only the slots the hardpoint system owns are
                // hardpoints; a port gun sits in a plain item slot.
                var hardpointSlots = new HashSet<string>();
                if (proto.TryComp<HardpointSlotsComponent>(out var vehicleHardpoints, factory))
                    hardpointSlots.UnionWith(vehicleHardpoints.Slots.Select(s => s.Id));

                var nested = new List<NestedStart>();
                foreach (var (id, itemId) in starting)
                {
                    if (!server.ProtoMan.TryIndex<EntityPrototype>(itemId, out var held) ||
                        !held.TryComp<ItemSlotsComponent>(out var heldSlots, factory))
                    {
                        continue;
                    }

                    var heldHardpoints = new HashSet<string>();
                    if (held.TryComp<HardpointSlotsComponent>(out var partHardpoints, factory))
                        heldHardpoints.UnionWith(partHardpoints.Slots.Select(s => s.Id));

                    foreach (var (childId, childSlot) in heldSlots.Slots)
                    {
                        if (childSlot.StartingItem is { } childItem)
                            nested.Add(new NestedStart(id, childId, childItem, heldHardpoints.Contains(childId)));
                    }
                }

                var entryPoints = enter.EntryPoints;
                roster.Add(new RosterEntry(proto.ID, entryPoints.Select(e => e.Offset).ToList(), starting, nested, hardpointSlots));
            }
        });

        // RMC's 21, the 17 civilian and 4 military CMU vehicles and the 8 armed variants.
        Assert.That(roster, Has.Count.GreaterThanOrEqualTo(50), "The roster is missing vehicles.");
        Assert.That(roster.Any(e => e.NestedStarting.Count > 0), Is.True, "No vehicle in the roster starts with an armed turret.");

        var failures = new List<string>();

        foreach (var entry in roster.OrderBy(e => e.Id))
        {
            await CheckVehicle(pair, map, entry, failures);
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));

        await pair.CleanReturnAsync();
    }

    /// <summary>A vehicle prototype read on the server thread: its id, where its doorways are and the parts it starts with.</summary>
    private sealed record RosterEntry(string Id, List<Vector2> Doorways, Dictionary<string, string> StartingItems, List<NestedStart> NestedStarting, HashSet<string> HardpointSlotIds);

    /// <summary>A part a vehicle's starting part starts with: the vehicle's slot, the part's slot, the part, and whether that slot is a hardpoint slot.</summary>
    private sealed record NestedStart(string Slot, string ChildSlot, string Item, bool IsHardpoint);

    private static async Task CheckVehicle(TestPair pair, TestMapData map, RosterEntry entry, List<string> failures)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var vehicle = EntityUid.Invalid;
        var interiorMap = MapId.Nullspace;

        void Fail(string what) => failures.Add($"{entry.Id}: {what}");

        await server.WaitPost(() =>
        {
            vehicle = entMan.SpawnEntity(entry.Id, new EntityCoordinates(map.Grid.Owner, VehicleSpot));
            if (entMan.TryGetComponent<WFVehicleEngineComponent>(vehicle, out var engine))
            {
                engine.Running = true;
                engine.Electrics = true;
            }
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            if (!entMan.TryGetComponent(vehicle, out GridVehicleMoverComponent? mover))
                return;

            if (!mover.FreeSteering || mover.MaxRotationSpeedDegrees <= 0f || mover.RotationAccelerationDegrees <= 0f)
                Fail("it does not steer freely (GridVehicleMover freeSteering with a turn rate and a turn acceleration).");
        });

        // Every part the prototype starts with is fitted, and the vehicle can run.
        await server.WaitAssertion(() =>
        {
            var slots = server.System<ItemSlotsSystem>();
            foreach (var (id, expected) in entry.StartingItems)
            {
                if (!slots.TryGetSlot(vehicle, id, out var live) || live.Item is not { } item)
                    Fail($"slot {id} did not get its starting {expected}.");
                else if (entMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID != expected)
                    Fail($"slot {id} holds {entMan.ToPrettyString(item)}, not {expected}.");
            }

            // So is every part its starting turret starts with, and the vehicle's hardpoint walk finds each of them mounted.
            foreach (var start in entry.NestedStarting)
            {
                if (!slots.TryGetSlot(vehicle, start.Slot, out var turretSlot) || turretSlot.Item is not { } turret)
                    Fail($"slot {start.Slot} holds no turret to carry {start.Item}.");
                else if (!slots.TryGetSlot(turret, start.ChildSlot, out var nested) || nested.Item is not { } part)
                    Fail($"slot {start.Slot}::{start.ChildSlot} did not get its starting {start.Item}.");
                else if (entMan.GetComponent<MetaDataComponent>(part).EntityPrototype?.ID != start.Item)
                    Fail($"slot {start.Slot}::{start.ChildSlot} holds {entMan.ToPrettyString(part)}, not {start.Item}.");
            }

            var mounted = server.System<VehicleTopologySystem>().GetMountedSlots(vehicle);
            foreach (var (id, expected) in entry.StartingItems.Where(s => entry.HardpointSlotIds.Contains(s.Key)))
            {
                if (!mounted.Any(m => m.Path == new VehicleSlotPath(id) && m.Item is { } item &&
                                      entMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID == expected))
                {
                    Fail($"the hardpoint walk does not see {expected} mounted in {id}.");
                }
            }

            foreach (var start in entry.NestedStarting.Where(s => s.IsHardpoint))
            {
                if (!mounted.Any(m => m.Path == new VehicleSlotPath(start.Slot, start.ChildSlot) && m.Item is { } item &&
                                      entMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID == start.Item))
                {
                    Fail($"the hardpoint walk does not see {start.Item} mounted in {start.Slot}::{start.ChildSlot}.");
                }
            }

            var ev = new VehicleCanRunEvent((vehicle, entMan.GetComponent<RMCVehicleComponent>(vehicle)));
            entMan.EventBus.RaiseLocalEvent(vehicle, ref ev);
            if (!ev.CanRun)
                Fail("cannot run as spawned.");

            // A frame with a larger maximum than its base has to set its integrity too, or it starts worn.
            if (entMan.TryGetComponent(vehicle, out HardpointIntegrityComponent? frame) && frame.Integrity < frame.MaxIntegrity)
                Fail($"spawns at {frame.Integrity} of {frame.MaxIntegrity} integrity.");
        });

        var users = new List<EntityUid>();

        for (var index = 0; index < entry.Doorways.Count; index++)
        {
            var doorway = VehicleSpot + entry.Doorways[index];
            var user = EntityUid.Invalid;
            var at = index;

            await server.WaitPost(() =>
            {
                user = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, doorway));
                users.Add(user);
            });
            await server.WaitRunTicks(3);

            await server.WaitPost(() => server.System<SharedInteractionSystem>().InteractionActivate(user, vehicle));
            await server.WaitRunTicks(120);

            await server.WaitAssertion(() =>
            {
                if (!entMan.TryGetComponent(vehicle, out VehicleInteriorComponent? interior))
                {
                    Fail($"doorway {at} did not load an interior.");
                    return;
                }

                interiorMap = interior.MapId;

                if (entMan.GetComponent<TransformComponent>(user).MapID != interior.MapId)
                {
                    Fail($"doorway {at} did not take the user in.");
                    return;
                }

                if (at == 0)
                    CheckInterior(pair, entry, vehicle, interior, user, Fail);
            });

            // The doors inside work by hand; the user opens them before walking out.
            await server.WaitPost(() =>
            {
                if (!entMan.TryGetComponent(vehicle, out VehicleInteriorComponent? interior))
                    return;

                var doors = server.System<SharedDoorSystem>();
                var query = entMan.EntityQueryEnumerator<DoorComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var door, out var xform))
                {
                    if (xform.MapID == interior.MapId && door.State != DoorState.Open && !doors.TryOpen(uid, door, user))
                        Fail($"{entMan.ToPrettyString(uid)} in the interior cannot be opened by hand.");
                }
            });
            await server.WaitRunTicks(60);

            // Out through the exit marker that belongs to this doorway.
            var marker = EntityUid.Invalid;
            await server.WaitPost(() =>
            {
                if (!entMan.TryGetComponent(vehicle, out VehicleInteriorComponent? interior))
                    return;

                // A doorway can have several markers (the box vans' front doors have two); the user goes out by the nearest.
                var nearest = float.MaxValue;
                var userPosition = server.System<SharedTransformSystem>().GetWorldPosition(user);
                var query = entMan.EntityQueryEnumerator<VehicleExitComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var exit, out var xform))
                {
                    if (xform.MapID != interior.MapId || exit.EntryIndex != at)
                        continue;

                    var distance = Vector2.Distance(userPosition, server.System<SharedTransformSystem>().GetWorldPosition(xform));
                    if (distance < nearest)
                    {
                        nearest = distance;
                        marker = uid;
                    }
                }

                if (marker.IsValid())
                    server.System<SharedInteractionSystem>().InteractionActivate(user, marker);
            });
            await server.WaitRunTicks(120);

            await server.WaitAssertion(() =>
            {
                if (!marker.IsValid())
                {
                    Fail($"the interior has no exit marker for doorway {at}.");
                    return;
                }

                var xform = entMan.GetComponent<TransformComponent>(user);
                if (xform.MapID != map.MapId)
                {
                    var markerXform = entMan.GetComponent<TransformComponent>(marker);
                    Fail($"the exit for doorway {at} did not put the user back out (user at {xform.LocalPosition}, marker {entMan.ToPrettyString(marker)} at {markerXform.LocalPosition}, pending {entMan.GetComponent<VehicleExitComponent>(marker).PendingExit}).");
                }
                else if (entMan.HasComponent<VehicleInteriorOccupantComponent>(user))
                    Fail($"the user is still an occupant after leaving by doorway {at}.");
            });
        }

        // Deleting the vehicle with someone aboard leaves them alive where it stood.
        var occupant = EntityUid.Invalid;
        var stood = Vector2.Zero;
        await server.WaitPost(() => occupant = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, VehicleSpot + entry.Doorways[0])));
        await server.WaitRunTicks(3);
        await server.WaitPost(() => server.System<SharedInteractionSystem>().InteractionActivate(occupant, vehicle));
        await server.WaitRunTicks(120);

        var aboard = false;
        await server.WaitPost(() =>
        {
            aboard = entMan.TryGetComponent(vehicle, out VehicleInteriorComponent? interior) &&
                     entMan.GetComponent<TransformComponent>(occupant).MapID == interior.MapId;
            if (!aboard)
                return;

            stood = entMan.GetComponent<TransformComponent>(vehicle).LocalPosition;
            entMan.DeleteEntity(vehicle);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            if (!aboard)
            {
                Fail("could not take the occupant in to check deleting it with someone aboard.");
            }
            else if (entMan.Deleted(occupant))
            {
                Fail("deleted its occupant along with it.");
            }
            else
            {
                var xform = entMan.GetComponent<TransformComponent>(occupant);
                if (xform.MapID != map.MapId || xform.ParentUid != map.Grid.Owner || Vector2.Distance(xform.LocalPosition, stood) > 0.5f)
                    Fail($"left its occupant at {xform.LocalPosition} on {xform.MapID}, not where it stood ({stood}).");
            }
        });

        await server.WaitPost(() =>
        {
            foreach (var user in users)
            {
                entMan.DeleteEntity(user);
            }

            entMan.DeleteEntity(occupant);
            entMan.DeleteEntity(vehicle);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            if (entMan.EntityExists(vehicle))
                Fail("was not deleted.");

            if (interiorMap != MapId.Nullspace && server.System<SharedMapSystem>().MapExists(interiorMap))
                Fail("left its interior map behind when it was deleted.");
        });
    }

    /// <summary>What an interior has to offer: a place to drive from, a way out for every doorway and air.</summary>
    private static void CheckInterior(TestPair pair, RosterEntry entry, EntityUid vehicle, VehicleInteriorComponent interior, EntityUid user, Action<string> fail)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        var grid = interior.Grid;
        if (!grid.IsValid() || !entMan.HasComponent<MapGridComponent>(grid))
            fail("the interior has no grid.");
        else
            CheckGridIsShielded(entMan, grid, fail);

        // Everything the map places stands on the grid, or in something that does: a child of the map entity has no GridUid, is
        // not anchored, and nothing that walks the grid's children sees it.
        var interiorMap = server.System<SharedMapSystem>().GetMapOrInvalid(interior.MapId);
        var transforms = entMan.EntityQueryEnumerator<TransformComponent>();
        while (transforms.MoveNext(out var child, out var childXform))
        {
            if (childXform.MapID == interior.MapId && child != interiorMap && child != grid && childXform.ParentUid == interiorMap)
                fail($"{entMan.ToPrettyString(child)} is parented to the interior's map, not to its grid.");
        }

        var seats = 0;
        var seatQuery = entMan.EntityQueryEnumerator<VehicleDriverSeatComponent, TransformComponent>();
        while (seatQuery.MoveNext(out _, out _, out var seatXform))
        {
            if (seatXform.MapID == interior.MapId)
                seats++;
        }

        if (seats == 0)
            fail("the interior has no driver seat.");

        // A weapons seat gives its vehicle the weapons component when someone sits in it, so an unarmed vehicle has none.
        if (!entMan.HasComponent<VehicleWeaponsComponent>(vehicle))
        {
            var weaponSeatQuery = entMan.EntityQueryEnumerator<VehicleWeaponsSeatComponent, TransformComponent>();
            while (weaponSeatQuery.MoveNext(out var weaponSeat, out _, out var weaponSeatXform))
            {
                if (weaponSeatXform.MapID == interior.MapId)
                    fail($"the vehicle has no weapons component but its interior places the weapons seat {entMan.ToPrettyString(weaponSeat)}.");
            }
        }

        var exits = new HashSet<int>();
        var exitQuery = entMan.EntityQueryEnumerator<VehicleExitComponent, TransformComponent>();
        while (exitQuery.MoveNext(out _, out var exit, out var exitXform))
        {
            if (exitXform.MapID == interior.MapId)
                exits.Add(exit.EntryIndex);
        }

        for (var i = 0; i < entry.Doorways.Count; i++)
        {
            if (!exits.Contains(i))
                fail($"the interior has no exit marker for doorway {i}.");
        }

        CheckSweepAcceptsEveryPlace(pair, entry, vehicle, interior, fail);
        CheckAmmoLoadersWork(pair, interior, fail);

        var air = server.System<AtmosphereSystem>().GetContainingMixture(user);
        if (air == null)
        {
            fail("the cabin has no air at all.");
        }
        else
        {
            var oxygen = air.GetMoles(Gas.Oxygen);
            if (air.Pressure < 80f || air.Pressure > 120f || air.TotalMoles <= 0f || oxygen / air.TotalMoles < 0.16f)
                fail($"the cabin air is not breathable: {air.Pressure:0.0} kPa, {oxygen:0.0} of {air.TotalMoles:0.0} moles oxygen.");
        }
    }

    /// <summary>Every entity the interior map places whose prototype id ends in AmmoLoader has the loader component and its window.</summary>
    private static void CheckAmmoLoadersWork(TestPair pair, VehicleInteriorComponent interior, Action<string> fail)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var ui = server.System<SharedUserInterfaceSystem>();

        var query = entMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            if (xform.MapID != interior.MapId || meta.EntityPrototype?.ID is not { } id || !id.EndsWith("AmmoLoader"))
                continue;

            if (!entMan.HasComponent<VehicleAmmoLoaderComponent>(uid))
                fail($"{id} is placed by the interior without the VehicleAmmoLoader component.");
            else if (!ui.HasUi(uid, VehicleAmmoLoaderUiKey.Key))
                fail($"{id} is placed by the interior without the ammo loader window (VehicleAmmoLoaderUiKey.Key).");
        }
    }

    /// <summary>The interior grid cannot be cleaned up, edited or blown up.</summary>
    private static void CheckGridIsShielded(IEntityManager entMan, EntityUid grid, Action<string> fail)
    {
        if (!entMan.HasComponent<CleanupImmuneComponent>(grid))
            fail("the interior grid is not CleanupImmune after load.");

        if (!entMan.TryGetComponent(grid, out ProtectedGridComponent? shield))
            fail("the interior grid is not a ProtectedGrid after load.");
        else if (!shield.PreventFloorRemoval || !shield.PreventFloorPlacement || !shield.PreventRCDUse || !shield.PreventExplosions)
            fail("the interior grid's ProtectedGrid leaves floor removal, floor placement, RCD use or explosions open.");

        if (!entMan.TryGetComponent(grid, out GravityComponent? gravity) || !gravity.Enabled || !gravity.Inherent)
            fail("the interior grid has no gravity after load.");

        if (!entMan.HasComponent<MapLightComponent>(entMan.GetComponent<TransformComponent>(grid).MapUid))
            fail("the interior map has no ambient light after load.");
    }

    /// <summary>
    /// Everywhere the game puts or keeps someone in the cabin (the entry point, where each doorway lands them, each exit
    /// marker and each seat) is a place the out-of-bounds sweep leaves alone, or it would drag them away from it every second.
    /// </summary>
    private static void CheckSweepAcceptsEveryPlace(TestPair pair, RosterEntry entry, EntityUid vehicle, VehicleInteriorComponent interior, Action<string> fail)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var safety = server.System<VehicleSafetySystem>();
        var cabin = new Entity<VehicleInteriorComponent>(vehicle, interior);

        void Check(string what, EntityCoordinates at)
        {
            if (!safety.IsInsideCabin(cabin, at))
                fail($"{what} is a place the out-of-bounds sweep sends people away from (at {at.Position} on the interior grid).");
        }

        Check("the entry point", interior.Entry);

        var transformSystem = server.System<SharedTransformSystem>();

        for (var i = 0; i < entry.Doorways.Count; i++)
        {
            if (!server.System<VehicleInteriorSystem>().TryGetInteriorEntryCoordinates(vehicle, i, out var landing))
            {
                fail($"doorway {i} has no landing spot.");
                continue;
            }

            Check($"the spot doorway {i} lands people on", landing);

            // A doorway puts people down beside its own exit, not across the cabin from it.
            var landingWorld = transformSystem.ToMapCoordinates(landing).Position;
            var nearest = float.MaxValue;
            var markers = entMan.EntityQueryEnumerator<VehicleExitComponent, TransformComponent>();
            while (markers.MoveNext(out _, out var exit, out var markerXform))
            {
                if (markerXform.MapID == interior.MapId && exit.EntryIndex == i)
                    nearest = Math.Min(nearest, Vector2.Distance(landingWorld, transformSystem.GetWorldPosition(markerXform)));
            }

            if (nearest > MaxLandingDistance)
                fail($"doorway {i} lands people {nearest:0.0} tiles from its exit marker, over {MaxLandingDistance}.");
        }

        var exitQuery = entMan.EntityQueryEnumerator<VehicleExitComponent, TransformComponent>();
        while (exitQuery.MoveNext(out _, out var exit, out var xform))
        {
            if (xform.MapID == interior.MapId)
                Check($"the exit marker of doorway {exit.EntryIndex}", xform.Coordinates);
        }

        var seatQuery = entMan.EntityQueryEnumerator<StrapComponent, TransformComponent>();
        while (seatQuery.MoveNext(out var seat, out _, out var xform))
        {
            if (xform.MapID == interior.MapId)
                Check($"the seat {entMan.ToPrettyString(seat)}", xform.Coordinates);
        }
    }
}
