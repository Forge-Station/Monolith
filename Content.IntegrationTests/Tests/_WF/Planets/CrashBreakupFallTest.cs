#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Caverns;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.Planets.Flight;
using Content.Server.Shuttles.Components;
using Content.Shared._CE.ZLevels.Core.Components;
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

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>A real hull that loses lift over a planet falls through the landing code and breaks into sections.</summary>
[TestFixture]
[TestOf(typeof(WFFlightSystem))]
public sealed class CrashBreakupFallTest
{
    /// <summary>Tiles of real terrain loaded around the hull before it reaches the ground.</summary>
    private const int GroundMargin = 48;

    // gridSplitting false is the development config preset a DebugOpt playtest server loads.
    [TestCase("WFSurfaceCarcinoma", "/SharedMaps/_WF/OldVessels/vaquita.yml", false)]
    [TestCase("WFSurfaceAsclepiu", "/SharedMaps/_WF/Shipyard/Shuttles/dredger.yml", false)]
    [TestCase("WFSurfaceCarcinoma", "/SharedMaps/_WF/OldVessels/vaquita.yml", true)]
    public async Task LiftLostHullBreaksIntoLatticedSections(string surface, string path, bool gridSplitting)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var levels = server.System<CEZLevelsSystem>();

        await CavernFixture.EnableCaverns(pair);
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.GridSplitting, gridSplitting));
        var world = await CavernFixture.BuildWorld(pair, surface);
        var ground = world.Ground;
        var orbitMap = await CavernFixture.MapIdOf(pair, world.Layers[^1]);
        var lattice = server.ResolveDependency<ITileDefinitionManager>()["Lattice"].TileId;

        var hull = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(orbitMap, new ResPath(path), out var loaded), Is.True);
            hull = loaded!.Value.Owner;
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        // The crew switches every engine off, so the hull has no lift left to hold it up.
        await server.WaitPost(() =>
        {
            foreach (var uid in Children(em, hull))
            {
                if (em.TryGetComponent<ThrusterComponent>(uid, out var thruster) && thruster.Type == ThrusterType.Linear && thruster.Enabled)
                    em.EventBus.RaiseLocalEvent(uid, new ActivateInWorldEvent(uid, uid, true));
            }
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        var latticeBefore = 0;
        await server.WaitPost(() => latticeBefore = CountTiles(em, maps, new[] { hull }, lattice));

        var refusal = await EnterAtmosphere(pair, hull, settle: 0f);
        Assert.That(refusal, Is.Null, $"The confirmed descent was refused: {refusal}");

        await server.WaitAssertion(() =>
        {
            Assert.That(levels.WfTryGetLiftRatio(hull, out var ratio), Is.True);
            Assert.That(ratio, Is.LessThan(CEZLevelsSystem.WFPartialLiftRatio),
                "The hull must free-fall, so its touchdown is a crash rather than a hard landing.");
        });

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

            // Terrain only generates near viewers; a player aboard would have loaded this.
            terrainLoaded = true;
            await CavernFixture.LoadChunks(pair, ground,
                new Vector2i((int) MathF.Floor(box.Left) - GroundMargin, (int) MathF.Floor(box.Bottom) - GroundMargin),
                new Vector2i((int) MathF.Ceiling(box.Right) + GroundMargin, (int) MathF.Ceiling(box.Top) + GroundMargin));
        }

        Assert.That(terrainLoaded, Is.True, "The hull never entered the gap above the ground.");
        Assert.That(landed, Is.True, "The hull never reached the ground.");

        await server.WaitRunTicks(12);

        var crashed = false;
        var sections = new List<EntityUid>();
        var velocities = new List<Vector2>();
        var latticeAfter = 0;
        var splittingAfter = false;
        await server.WaitPost(() =>
        {
            crashed = em.HasComponent<WFCrashImpactComponent>(hull);
            var query = em.EntityQueryEnumerator<MapGridComponent, WFCrashImpactComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out _, out var xform))
            {
                if (xform.MapUid == ground)
                    sections.Add(uid);
            }

            velocities.AddRange(sections.Select(uid => em.GetComponent<PhysicsComponent>(uid).LinearVelocity));
            latticeAfter = CountTiles(em, maps, sections, lattice);
            splittingAfter = server.CfgMan.GetCVar(CVars.GridSplitting);
        });

        TestContext.Out.WriteLine($"{surface}: {sections.Count} sections, lattice {latticeBefore} -> {latticeAfter}, " +
                                  $"velocities {string.Join(", ", velocities)}");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(crashed, Is.True, "The landing did not take the structural crash path.");
            Assert.That(sections, Has.Count.GreaterThan(1), "The crashed hull did not split into sections.");
            Assert.That(latticeAfter, Is.GreaterThan(latticeBefore), "The fracture seam was not left as lattice.");
            Assert.That(velocities.Any(v => Vector2.Distance(v, velocities[0]) > 0.1f), Is.True,
                "The sections are not moving apart.");
            Assert.That(splittingAfter, Is.EqualTo(gridSplitting), "The crash left the server's grid splitting setting changed.");
        }

        await CavernFixture.Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>Tiles of one type across a set of grids.</summary>
    private static int CountTiles(IEntityManager entMan, SharedMapSystem maps, IEnumerable<EntityUid> grids, int typeId)
    {
        var count = 0;
        foreach (var grid in grids)
        {
            var tiles = maps.GetAllTilesEnumerator(grid, entMan.GetComponent<MapGridComponent>(grid));
            while (tiles.MoveNext(out var tile))
            {
                if (tile.Value.Tile.TypeId == typeId)
                    count++;
            }
        }

        return count;
    }
}
