#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Caverns;
using Content.Server._CE.ZLevels.Core;
using Content.Server._Mono.ShipRepair;
using Content.Server._WF.Planets.Flight;
using Content.Server.Shuttles.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._WF.ShipRepair;
using Content.Shared.Charges.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Robust.Shared;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.ShipRepair;

/// <summary>A real hull breaks up in a planet crash, and the SRD reattaches every section and rebuilds the seam.</summary>
[TestFixture]
[TestOf(typeof(WFHullSectionSystem))]
public sealed class CrashReattachTest
{
    /// <summary>Tiles of real terrain loaded around the hull before it reaches the ground.</summary>
    private const int GroundMargin = 48;

    [Test]
    public async Task CrashedHullIsReattachedAndRebuiltWhole()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var levels = server.System<CEZLevelsSystem>();

        // gridSplitting off is the development preset a playtest server loads; the crash forces its own cut.
        await CavernFixture.EnableCaverns(pair);
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.GridSplitting, false));
        var world = await CavernFixture.BuildWorld(pair, "WFSurfaceCarcinoma");
        var ground = world.Ground;
        var orbitMap = await CavernFixture.MapIdOf(pair, world.Layers[^1]);

        var hull = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(orbitMap, new ResPath("/SharedMaps/_WF/OldVessels/vaquita.yml"), out var loaded), Is.True);
            hull = loaded!.Value.Owner;
            server.System<ShipRepairSystem>().GenerateRepairData(hull);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        // Every engine off: no lift, so the touchdown is a crash.
        await server.WaitPost(() =>
        {
            foreach (var uid in Children(em, hull))
            {
                if (em.TryGetComponent<ThrusterComponent>(uid, out var thruster) && thruster.Type == ThrusterType.Linear && thruster.Enabled)
                    em.EventBus.RaiseLocalEvent(uid, new ActivateInWorldEvent(uid, uid, true));
            }
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        var refusal = await EnterAtmosphere(pair, hull, settle: 0f);
        Assert.That(refusal, Is.Null, $"The confirmed descent was refused: {refusal}");

        var terrainLoaded = false;
        var landed = false;
        for (var step = 0; step < 150 * 6 && !landed; step++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(1f / 6f));

            Box2? footprint = null;
            await server.WaitPost(() =>
            {
                var map = em.GetComponent<TransformComponent>(hull).MapUid;
                landed = map == ground;

                if (!terrainLoaded && em.TryGetComponent<CEZTransitMapComponent>(map, out var transit) && transit.LowerMap == ground)
                    footprint = xforms.GetWorldMatrix(hull).TransformBox(em.GetComponent<MapGridComponent>(hull).LocalAABB);
            });

            if (footprint is not { } box)
                continue;

            terrainLoaded = true;
            await CavernFixture.LoadChunks(pair, ground,
                new Vector2i((int) MathF.Floor(box.Left) - GroundMargin, (int) MathF.Floor(box.Bottom) - GroundMargin),
                new Vector2i((int) MathF.Ceiling(box.Right) + GroundMargin, (int) MathF.Ceiling(box.Top) + GroundMargin));
        }

        Assert.That(landed, Is.True, "The hull never reached the ground.");

        // Let the wreck skid to rest.
        await server.WaitRunTicks(pair.SecondsToTicks(8f));

        var user = EntityUid.Invalid;
        var tool = EntityUid.Invalid;
        var detached = 0;
        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WFCrashImpactComponent>(hull), Is.True, "The landing did not take the structural crash path.");
            Assert.That(em.HasComponent<ShipRepairDataComponent>(hull), Is.True, "The hull lost its blueprint in the crash.");
            var sections = SectionsOf(em, hull);
            detached = sections.Count;
            Assert.That(sections, Is.Not.Empty, "The crash left no sections to reattach.");
            Assert.That(sections.Any(uid => em.HasComponent<ShipRepairDataComponent>(uid)), Is.False, "A section carries its own blueprint.");

            // The worker stands on clear deck, so nothing shoves them off a do-after.
            var lattice = server.ResolveDependency<ITileDefinitionManager>()["Lattice"].TileId;
            var grid = em.GetComponent<MapGridComponent>(hull);
            var floor = maps.GetAllTiles(hull, grid)
                .First(tile => tile.Tile.TypeId != lattice
                               && !maps.GetAnchoredEntities(hull, grid, tile.GridIndices)
                                   .Any(uid => em.TryGetComponent<PhysicsComponent>(uid, out var body) && body.CanCollide && body.Hard));
            var spot = new EntityCoordinates(hull, maps.TileCenterToVector((hull, grid), floor.GridIndices));
            user = em.SpawnEntity("MobHuman", spot);
            tool = em.SpawnEntity("ShipRepairDevice", spot);
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(user, tool), Is.True);
            em.GetComponent<ShipRepairToolComponent>(tool).RepairTimeMultiplier = 0.02f;
        });

        // Reattach every section; one lying in another's place goes after it.
        for (var round = 0; round < 8; round++)
        {
            var sections = new List<EntityUid>();
            await server.WaitPost(() => sections = SectionsOf(em, hull));
            if (sections.Count == 0)
                break;

            foreach (var section in sections)
            {
                await server.WaitPost(() =>
                {
                    if (!em.EntityExists(section))
                        return;

                    var grid = em.GetComponent<MapGridComponent>(section);
                    var tile = maps.GetAllTiles(section, grid).First().GridIndices;
                    TestContext.Out.WriteLine($"round {round}: section {section}, " +
                                              $"{server.System<WFHullSectionSystem>().CountTiles((section, grid))} tiles, " +
                                              $"{server.System<WFHullSectionSystem>().HomeOffset(hull, (section, grid)):F1} from its place");
                    server.System<SharedChargesSystem>().AddCharges(tool, 10000);
                    Click(em, user, tool, new EntityCoordinates(section, maps.TileCenterToVector((section, grid), tile)));
                });
                await server.WaitRunTicks(pair.SecondsToTicks(1f));
            }
        }

        // Then rebuild the seam, lattice and all, from the blueprint.
        var expected = new Dictionary<Vector2i, int>();
        var rebuilt = 0;
        await server.WaitPost(() =>
        {
            Assert.That(SectionsOf(em, hull), Is.Empty, "Some sections could not be reattached.");
            em.GetComponent<ShipRepairToolComponent>(tool).RepairTimeMultiplier = 0f;
            var data = em.GetComponent<ShipRepairDataComponent>(hull);
            foreach (var (chunkIndex, chunk) in data.Chunks)
            {
                for (var i = 0; i < chunk.Tiles.Length; i++)
                {
                    if (chunk.Tiles[i] != Tile.Empty.TypeId)
                        expected[chunkIndex * data.ChunkSize + new Vector2i(i % data.ChunkSize, i / data.ChunkSize)] = chunk.Tiles[i];
                }
            }

            var grid = em.GetComponent<MapGridComponent>(hull);
            foreach (var (index, type) in expected)
            {
                if (maps.GetTileRef(hull, grid, index).Tile.TypeId == type)
                    continue;

                rebuilt++;
                server.System<SharedChargesSystem>().AddCharges(tool, 10000);
                Click(em, user, tool, new EntityCoordinates(hull, maps.TileCenterToVector((hull, grid), index)));
            }
        });
        await server.WaitRunTicks(1);

        var wrong = new List<Vector2i>();
        var extra = 0;
        await server.WaitPost(() =>
        {
            var grid = em.GetComponent<MapGridComponent>(hull);
            wrong.AddRange(expected.Where(pair => maps.GetTileRef(hull, grid, pair.Key).Tile.TypeId != pair.Value).Select(pair => pair.Key));
            extra = maps.GetAllTiles(hull, grid).Count(tile => !expected.ContainsKey(tile.GridIndices));
        });

        TestContext.Out.WriteLine($"{detached} sections reattached, {rebuilt} seam tiles rebuilt, {wrong.Count} wrong, {extra} extra");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rebuilt, Is.GreaterThan(0), "The crash left no seam to rebuild.");
            Assert.That(wrong, Is.Empty, $"Hull tiles differ from the blueprint: {string.Join(", ", wrong.Take(10))}");
            Assert.That(extra, Is.Zero, "The hull has tiles the blueprint doesn't.");
        }

        await CavernFixture.Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>Every grid linked to a hull.</summary>
    private static List<EntityUid> SectionsOf(IEntityManager entMan, EntityUid hull)
    {
        var found = new List<EntityUid>();
        var query = entMan.EntityQueryEnumerator<WFHullSectionComponent>();
        while (query.MoveNext(out var uid, out var section))
        {
            if (section.Hull == hull)
                found.Add(uid);
        }

        return found;
    }

    /// <summary>Uses the SRD at a spot, as a click within reach does.</summary>
    private static void Click(IEntityManager entMan, EntityUid user, EntityUid tool, EntityCoordinates at)
    {
        entMan.EventBus.RaiseLocalEvent(tool, new AfterInteractEvent(user, tool, null, at, true));
    }
}
