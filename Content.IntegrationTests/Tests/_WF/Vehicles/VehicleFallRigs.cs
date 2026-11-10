#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>A vehicle rig on an upper deck with a plated lower map under it, linked as a CE z-network.</summary>
internal sealed class FallLevels
{
    public required VehicleRig Rig;
    public required TestMapData Lower;
    public EntityUid Network;

    public TestPair Pair => Rig.Pair;
    public EntityUid Vehicle => Rig.Vehicle;
    public EntityUid LowerGrid => Lower.Grid.Owner;
    public MapId LowerMap => Lower.MapId;
}

internal static class FallLevelRigs
{
    /// <summary>The first tile past the end of the upper deck.</summary>
    public const int EdgeX = 20;

    /// <summary>Lays an upper deck to <see cref="EdgeX"/> with the vehicle on it, and a plated map one level below.</summary>
    public static async Task<FallLevels> Build(TestPair pair, string proto, float startX = 2.5f, float startY = 2.5f, bool facingRight = false)
    {
        var rig = await VehicleRigs.Build(pair, maxX: EdgeX, startX: startX, startY: startY, proto: proto, facingRight: facingRight);
        var lower = await pair.CreateTestMap();
        var levels = new FallLevels { Rig = rig, Lower = lower };
        var server = pair.Server;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;

            for (var x = -8; x < 60; x++)
            {
                for (var y = -8; y < 16; y++)
                {
                    maps.SetTile(lower.Grid.Owner, lower.Grid.Comp, new Vector2i(x, y), new Tile(plating));
                }
            }

            var zLevels = server.System<CEZLevelsSystem>();
            var network = zLevels.CreateMapNetwork();
            levels.Network = network.Owner;
            Assert.That(
                zLevels.TryAddMapsIntoNetwork(network, new Dictionary<EntityUid, int>
                {
                    [rig.Map.MapUid] = 0,
                    [lower.MapUid] = -1,
                }),
                Is.True,
                "The test maps could not be linked.");
        });
        await server.WaitRunTicks(2);

        return levels;
    }

    public static async Task Teardown(FallLevels levels)
    {
        var server = levels.Pair.Server;
        await server.WaitPost(() =>
        {
            if (!server.EntMan.Deleted(levels.Network))
                server.System<CEZLevelsSystem>().DeleteMapNetwork(levels.Network);
        });
        await server.WaitRunTicks(2);
    }

    public static MapId MapOf(FallLevels levels, EntityUid uid)
    {
        return levels.Pair.Server.EntMan.GetComponent<TransformComponent>(uid).MapID;
    }

    /// <summary>
    /// Runs one tick at a time until the vehicle is on the lower map, so it can be read as it lands. A vehicle the landing
    /// destroyed has landed too: nothing but the landing hurts it in these tests.
    /// </summary>
    public static async Task<bool> RunUntilLanded(FallLevels levels, int maxTicks)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            await levels.Pair.Server.WaitRunTicks(1);

            var landed = false;
            await levels.Pair.Server.WaitPost(() =>
            {
                var entities = levels.Pair.Server.EntMan;
                landed = !entities.EntityExists(levels.Vehicle) ||
                         entities.IsQueuedForDeletion(levels.Vehicle) ||
                         MapOf(levels, levels.Vehicle) == levels.LowerMap;
            });
            if (landed)
                return true;
        }

        return false;
    }
}
