#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>A chassis and a driver on a strip of floor, for the mover tests.</summary>
internal sealed class VehicleRig
{
    public required TestPair Pair;
    public required TestMapData Map;
    public EntityUid Vehicle;
    public EntityUid Driver;

    public EntityUid Grid => Map.Grid.Owner;
}

internal static class VehicleRigs
{
    /// <summary>
    /// Lays plating over x in [<paramref name="minX"/>, <paramref name="maxX"/>) and y in [<paramref name="minY"/>, <paramref name="maxY"/>) of the test grid, turns the
    /// grid by <paramref name="degrees"/> about its origin, and puts the vehicle at the given grid-local point.
    /// </summary>
    public static async Task<VehicleRig> Build(
        TestPair pair,
        int minX = -3,
        int maxX = 40,
        int minY = 0,
        int maxY = 6,
        float degrees = 0f,
        float startX = 2.5f,
        float startY = 2.5f,
        string proto = "WFVehicleTestChassis",
        IEnumerable<Vector2i>? walls = null,
        bool facingRight = false)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var rig = new VehicleRig { Pair = pair, Map = map };

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            var maps = server.System<SharedMapSystem>();
            var xforms = server.System<SharedTransformSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;

            // The pair is pooled, so a handler flag or event log left by an earlier test would still be there.
            server.System<VehicleEventProbeSystem>().Reset();

            for (var x = minX; x < maxX; x++)
            {
                for (var y = minY; y < maxY; y++)
                {
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
                }
            }

            if (degrees != 0f)
                xforms.SetLocalRotation(map.Grid.Owner, Angle.FromDegrees(degrees));

            if (walls != null)
            {
                foreach (var wall in walls)
                {
                    entMan.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid.Owner, wall.X + 0.5f, wall.Y + 0.5f));
                }
            }

            rig.Vehicle = entMan.SpawnEntity(proto, new EntityCoordinates(map.Grid.Owner, startX, startY));
            if (facingRight)
                xforms.SetLocalRotation(rig.Vehicle, Angle.FromWorldVec(Vector2.UnitX));

            rig.Driver = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, minX + 0.5f, minY + 0.5f));
        });
        await server.WaitRunTicks(5);

        return rig;
    }

    /// <summary>Adds a second grid to the rig's map with plating over the given tiles.</summary>
    public static async Task<EntityUid> AddGrid(VehicleRig rig, Vector2 position, float degrees, IEnumerable<Vector2i> tiles)
    {
        var server = rig.Pair.Server;
        var grid = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var mapMan = server.System<SharedMapSystem>();
            var maps = server.System<SharedMapSystem>();
            var xforms = server.System<SharedTransformSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;

            var created = mapMan.CreateGridEntity(rig.Map.MapId);
            grid = created.Owner;
            xforms.SetLocalPosition(grid, position);
            xforms.SetLocalRotation(grid, Angle.FromDegrees(degrees));

            foreach (var tile in tiles)
            {
                maps.SetTile(created.Owner, created.Comp, tile, new Tile(plating));
            }
        });
        await server.WaitRunTicks(2);

        return grid;
    }

    public static async Task Seat(VehicleRig rig)
    {
        await rig.Pair.Server.WaitAssertion(() =>
        {
            var vehicles = rig.Pair.Server.System<RMCVehicleSystem>();
            var vehicle = rig.Pair.Server.EntMan.GetComponent<RMCVehicleComponent>(rig.Vehicle);

            Assert.That(vehicles.TrySetOperator((rig.Vehicle, vehicle), rig.Driver), Is.True, "The human could not take the wheel.");
        });
    }

    public static void Hold(VehicleRig rig, MoveButtons buttons)
    {
        rig.Pair.Server.EntMan.GetComponent<InputMoverComponent>(rig.Driver).HeldMoveButtons = buttons;
    }

    public static Vector2 LocalPosition(VehicleRig rig)
    {
        return rig.Pair.Server.EntMan.GetComponent<TransformComponent>(rig.Vehicle).LocalPosition;
    }

    public static Vector2 WorldPosition(VehicleRig rig)
    {
        return rig.Pair.Server.System<SharedTransformSystem>().GetWorldPosition(rig.Vehicle);
    }

    /// <summary>The distance between two points; NUnit cannot apply a tolerance to a vector.</summary>
    public static float Gap(Vector2 a, Vector2 b)
    {
        return (a - b).Length();
    }

    public static GridVehicleMoverComponent Mover(VehicleRig rig)
    {
        return rig.Pair.Server.EntMan.GetComponent<GridVehicleMoverComponent>(rig.Vehicle);
    }
}

/// <summary>The gun lookup with the gun's id and component apart, as the vehicle tests read it.</summary>
internal static class VehicleGunLookup
{
    public static bool TryGetGun(this SharedGunSystem guns, EntityUid entity, out EntityUid gunUid, [NotNullWhen(true)] out GunComponent? gun)
    {
        return guns.TryGetGun(entity, out gunUid, out gun);
    }
}
