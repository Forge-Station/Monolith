using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.Planets.Flight;
using Content.Shared.Buckle;
using Content.Shared.Damage;
using Content.Shared.Explosion.Components;
using Content.Shared.Maps;
using Content.Shared.Stunnable;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Audio.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class StructuralCrashTest
{
    [Test]
    public void FracturesVaryTheirPositionAndCanShearSmallDiagonalSections()
    {
        var hull = new HashSet<Vector2i>();
        for (var x = 0; x < 40; x++)
        for (var y = 0; y < 30; y++) hull.Add(new Vector2i(x, y));
        var layouts = new HashSet<string>();
        var small = false;
        var jagged = false;
        for (var seed = 0; seed < 16; seed++)
        {
            var cut = WFCrashFractures.Plan(hull, 2, seed);
            Assert.That(cut.Select(t => t.X).Distinct().Count(), Is.GreaterThan(1));
            Assert.That(cut.Select(t => t.Y).Distinct().Count(), Is.GreaterThan(1));
            jagged |= cut.Select(t => t.X + t.Y).Distinct().Count() > 1 && cut.Select(t => t.X - t.Y).Distinct().Count() > 1;
            layouts.Add(string.Join(";", cut.OrderBy(t => t.X).ThenBy(t => t.Y)));
            var remaining = new HashSet<Vector2i>(hull);
            remaining.ExceptWith(cut);
            var pieces = WFCrashFractures.Sections(remaining);
            Assert.That(pieces.Count, Is.EqualTo(2));
            small |= pieces.Min(s => s.Count) < hull.Count / 4;
        }
        Assert.That(layouts.Count, Is.GreaterThan(2));
        Assert.That(jagged, Is.True, "Cuts must wander rather than remaining perfect diagonal lines.");
        Assert.That(small, Is.True, "Fractures must allow smaller end/side sections, not always halves.");
    }

    [TestCase(2)]
    [TestCase(4)]
    public void FracturesPreserveSubstantialSections(int parts)
    {
        foreach (var shape in new[] { 0, 1, 2 })
        {
            var tiles = new HashSet<Vector2i>();
            for (var x = 0; x < 30; x++)
            for (var y = 0; y < 20; y++)
            {
                // Rectangle, H hull, and U hull: cuts must honour gaps and narrow connections.
                if (shape == 1 && x > 7 && x < 22 && (y < 7 || y > 12)) continue;
                if (shape == 2 && x > 7 && x < 22 && y > 6) continue;
                tiles.Add(new Vector2i(x, y));
            }
            var cut = WFCrashFractures.Plan(tiles, parts);
            Assert.That(cut.Count, Is.InRange(1, tiles.Count / 8));
            tiles.ExceptWith(cut);
            var sections = WFCrashFractures.Sections(tiles);
            Assert.That(sections.Count, Is.InRange(2, parts));
            Assert.That(sections.Min(s => s.Count), Is.GreaterThanOrEqualTo(6));
        }
    }

    // Three sizes of shipyard hull.
    [TestCase("/SharedMaps/_Mono/Shuttles/Civilian/autumn.yml")]
    [TestCase("/SharedMaps/_Mono/Shuttles/CivilianExp/pelican.yml")]
    [TestCase("/SharedMaps/_Mono/Shuttles/Scrapyard/mudskipper.yml")]
    public async Task RealHullLayoutsHaveBoundedFractures(string path)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(map.MapId, new ResPath(path), out var hull), Is.True);
            var tiles = new HashSet<Vector2i>();
            var all = server.System<SharedMapSystem>().GetAllTilesEnumerator(hull!.Value.Owner, hull.Value.Comp);
            while (all.MoveNext(out var tile)) tiles.Add(tile.Value.GridIndices);
            var cut = WFCrashFractures.Plan(tiles, 4);
            Assert.That(cut.Count, Is.InRange(1, tiles.Count / 8));
            tiles.ExceptWith(cut);
            Assert.That(WFCrashFractures.Sections(tiles).Count, Is.InRange(2, 4));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(0)]
    [TestCase(35)]
    public async Task BreakupCreatesMovingSectionsAndHurtsCrewOnceWithSeatProtection(int rotation)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-32, -32), new Vector2i(64, 64));
        var hull = await BuildHull(pair, em.GetComponent<MapComponent>(ground).MapId);
        await MapInitHull(pair, hull);
        var originalTiles = 0;
        await server.WaitAssertion(() =>
        {
            server.System<SharedTransformSystem>().SetWorldRotation(hull, Angle.FromDegrees(rotation));
            var grid = em.GetComponent<MapGridComponent>(hull);
            var maps = server.System<SharedMapSystem>();
            var tiles = maps.GetAllTilesEnumerator(hull, grid);
            while (tiles.MoveNext(out _)) originalTiles++;
            var standing = em.SpawnEntity("MobHuman", new EntityCoordinates(hull, new Vector2(2.5f, 2.5f)));
            var seated = em.SpawnEntity("MobHuman", new EntityCoordinates(hull, new Vector2(3.5f, 2.5f)));
            var chair = em.SpawnEntity("Chair", new EntityCoordinates(hull, new Vector2(3.5f, 2.5f)));
            Assert.That(server.System<SharedBuckleSystem>().TryBuckle(seated, seated, chair), Is.True);
            server.System<SharedPhysicsSystem>().SetLinearVelocity(hull, new Vector2(3f, 0));
            var flight = server.System<WFFlightSystem>();
            flight.StructuralCrash((hull, grid), 1f);
            var effects = em.EntityQueryEnumerator<MetaDataComponent>();
            var bursts = 0;
            while (effects.MoveNext(out var effect, out var meta))
                if (meta.EntityPrototype?.ID == "WFCrashBurst") bursts++;
            Assert.That(bursts, Is.InRange(3, 8), "Visible bursts must cover fractures and interiors without an effect flood.");
            var standingDamage = em.GetComponent<DamageableComponent>(standing).TotalDamage;
            var seatedDamage = em.GetComponent<DamageableComponent>(seated).TotalDamage;
            Assert.That(standingDamage.Float(), Is.GreaterThan(0f));
            Assert.That(seatedDamage.Float(), Is.InRange(1f, standingDamage.Float() - 1f));
            Assert.That(em.HasComponent<KnockedDownComponent>(standing), Is.True);
            flight.StructuralCrash((hull, grid), 1f);
            Assert.That(em.GetComponent<DamageableComponent>(standing).TotalDamage, Is.EqualTo(standingDamage));
        });
        await server.WaitRunTicks(12);
        await server.WaitAssertion(() =>
        {
            var sections = 0;
            var velocities = new List<Vector2>();
            var spins = new List<float>();
            var remaining = 0;
            var latticeTiles = 0;
            var ownedTiles = new HashSet<Vector2i>();
            var latticeType = server.ResolveDependency<ITileDefinitionManager>()["Lattice"].TileId;
            var query = em.EntityQueryEnumerator<MapGridComponent, WFSkidComponent, PhysicsComponent>();
            while (query.MoveNext(out var uid, out var grid, out var skid, out var body))
            {
                if (em.GetComponent<TransformComponent>(uid).MapUid != ground) continue;
                sections++;
                velocities.Add(body.LinearVelocity);
                spins.Add(body.AngularVelocity);
                Assert.That(body.FixedRotation, Is.False);
                Assert.That(float.IsFinite(body.AngularVelocity), Is.True);
                Assert.That(MathF.Abs(body.AngularVelocity), Is.LessThanOrEqualTo(0.65f));
                Assert.That(skid.ScarredTiles.Count, Is.GreaterThan(0), "Every section must leave a terrain trail.");
                Assert.That(float.IsFinite(body.LinearVelocity.LengthSquared()), Is.True);
                Assert.That(skid.Loop, Is.Not.Null, "Every moving section needs its own grind loop.");
                var grind = em.GetComponent<AudioComponent>(skid.Loop!.Value);
                Assert.That(grind.FileName, Is.EqualTo("/Audio/_WF/Planets/Flight/ground_grind_loop.ogg"));
                Assert.That(grind.Params.MaxDistance, Is.GreaterThanOrEqualTo(80f));
                var tiles = server.System<SharedMapSystem>().GetAllTilesEnumerator(uid, grid);
                while (tiles.MoveNext(out var tile))
                {
                    remaining++;
                    Assert.That(ownedTiles.Add(tile.Value.GridIndices), Is.True, "Each original seam cell must belong to exactly one section.");
                    if (tile.Value.Tile.TypeId == latticeType) latticeTiles++;
                }
            }
            Assert.That(sections, Is.InRange(2, 4));
            Assert.That(spins.Any(s => s > 0.01f) && spins.Any(s => s < -0.01f), Is.True,
                "Detached sections should retain different spin directions after ground friction.");
            Assert.That(velocities.Any(v => Vector2.Distance(v, velocities[0]) > 0.1f), Is.True, "Sections need different velocities to separate.");
            Assert.That(latticeTiles, Is.GreaterThan(0), "The tear must retain exposed lattice on a detached section.");
            Assert.That(remaining, Is.EqualTo(originalTiles), "Seam tiles should remain as lattice, not vanish between sections.");
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A broken hull keeps the one repair snapshot; every other section links back to it for the SRD.</summary>
    [Test]
    public async Task BreakupKeepsOneRepairSnapshot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-32, -32), new Vector2i(64, 64));
        var hull = await BuildHull(pair, em.GetComponent<MapComponent>(ground).MapId);
        await MapInitHull(pair, hull);
        var chunks = 0;
        var palette = 0;
        await server.WaitAssertion(() =>
        {
            server.System<Content.Server._Mono.ShipRepair.ShipRepairSystem>().GenerateRepairData(hull);
            var data = em.GetComponent<Content.Shared._Mono.ShipRepair.Components.ShipRepairDataComponent>(hull);
            chunks = data.Chunks.Count;
            palette = data.EntityPalette.Count;
            Assert.That(chunks, Is.GreaterThan(0));
            var grid = em.GetComponent<MapGridComponent>(hull);
            server.System<SharedPhysicsSystem>().SetLinearVelocity(hull, new Vector2(3f, 0));
            server.System<WFFlightSystem>().StructuralCrash((hull, grid), 1f);
        });
        await server.WaitRunTicks(12);
        await server.WaitAssertion(() =>
        {
            var sections = 0;
            var query = em.EntityQueryEnumerator<MapGridComponent, WFSkidComponent>();
            while (query.MoveNext(out var uid, out _, out _))
            {
                if (em.GetComponent<TransformComponent>(uid).MapUid != ground || uid == hull) continue;
                sections++;
                Assert.That(em.HasComponent<Content.Shared._Mono.ShipRepair.Components.ShipRepairDataComponent>(uid), Is.False,
                    $"Section {uid} carries its own repair snapshot.");
                Assert.That(em.TryGetComponent<Content.Shared._WF.ShipRepair.WFHullSectionComponent>(uid, out var link), Is.True,
                    $"Section {uid} is not linked to its hull.");
                Assert.That(link!.Hull, Is.EqualTo(hull));
            }
            Assert.That(sections, Is.InRange(1, 3));
            var data = em.GetComponent<Content.Shared._Mono.ShipRepair.Components.ShipRepairDataComponent>(hull);
            Assert.That(data.Chunks.Count, Is.EqualTo(chunks));
            Assert.That(data.EntityPalette.Count, Is.EqualTo(palette));
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Crash bursts never set off a hazard aboard; a gyroscope's shrapnel would chain through the wreck.</summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task BreakupBurstsSpareHazards(bool packed)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-32, -32), new Vector2i(64, 64));
        var hull = await BuildHull(pair, em.GetComponent<MapComponent>(ground).MapId);
        await MapInitHull(pair, hull);

        var gyroscopes = new List<EntityUid>();
        var gyroscopeIsHazard = true;
        await server.WaitPost(() =>
        {
            var flight = server.System<WFFlightSystem>();
            var fitted = new List<EntityUid>();
            var query = em.EntityQueryEnumerator<TransformComponent>();
            while (query.MoveNext(out var uid, out var xform))
            {
                if (xform.GridUid == hull && flight.IsCrashHazard(uid))
                    fitted.Add(uid);
            }
            fitted.ForEach(uid => em.DeleteEntity(uid));

            if (!packed)
                return;
            // A gyroscope on every tile puts one under each burst.
            for (var x = 0; x < 15; x++)
            for (var y = 0; y < 15; y++)
                gyroscopes.Add(em.SpawnEntity("Gyroscope", new EntityCoordinates(hull, x + 0.5f, y + 0.5f)));
            gyroscopeIsHazard = flight.IsCrashHazard(gyroscopes[0]);
        });
        await server.WaitRunTicks(1);
        Assert.That(gyroscopeIsHazard, "A gyroscope no longer counts as a crash hazard.");

        var earlierVisuals = new HashSet<EntityUid>();
        var seam = new HashSet<EntityUid>();
        var bursts = 0;
        await server.WaitPost(() =>
        {
            var visuals = em.EntityQueryEnumerator<ExplosionVisualsComponent>();
            while (visuals.MoveNext(out var uid, out _))
                earlierVisuals.Add(uid);
            server.System<SharedPhysicsSystem>().SetLinearVelocity(hull, new Vector2(3f, 0));
            server.System<WFFlightSystem>().StructuralCrash((hull, em.GetComponent<MapGridComponent>(hull)), 1f);
            // The seam takes its machinery with it.
            seam.UnionWith(gyroscopes.Where(uid => em.IsQueuedForDeletion(uid) || em.Deleted(uid)));
            var effects = em.EntityQueryEnumerator<MetaDataComponent>();
            while (effects.MoveNext(out _, out var meta))
            {
                if (meta.EntityPrototype?.ID == "WFCrashBurst")
                    bursts++;
            }
        });

        await server.WaitRunTicks(10);
        var explosions = 0;
        await server.WaitPost(() =>
        {
            var visuals = em.EntityQueryEnumerator<ExplosionVisualsComponent>();
            while (visuals.MoveNext(out var uid, out _))
            {
                if (!earlierVisuals.Contains(uid))
                    explosions++;
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(3f));
        var setOff = 0;
        await server.WaitPost(() => setOff = gyroscopes.Count(uid => !seam.Contains(uid) && !em.EntityExists(uid)));

        TestContext.Out.WriteLine($"packed {packed}: {bursts} bursts, {explosions} explosions, {seam.Count} gyroscopes in the seam, {setOff} set off");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bursts, Is.InRange(3, 8), "Every burst must still show, exploding or not.");
            if (packed)
            {
                Assert.That(explosions, Is.Zero, "A burst exploded beside a gyroscope.");
                Assert.That(setOff, Is.Zero, "A crash burst set off a gyroscope.");
            }
            else
            {
                Assert.That(explosions, Is.GreaterThan(0), "With no hazard aboard, the bursts must still explode.");
            }
        }

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
