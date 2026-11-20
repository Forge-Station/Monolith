#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Caverns;
using Content.Shared.Ghost;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Parallax.Biomes;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>
/// The eye cap: a viewer standing on the ground loads the cavern under it only while one of its holes is in view, a
/// viewer above the ground never does, and a cavern viewer keeps the ground above loaded.
/// </summary>
[TestFixture]
[TestOf(typeof(WFCavernEyeSystem))]
public sealed class CavernViewerEyeTest
{
    /// <summary>The z-level eye prototype UpdateViewer spawns on each map it looks into.</summary>
    private const string EyeProto = "CEZLevelEye";

    private const string Surface = "WFSurfaceAsclepiu";

    /// <summary>How far east of the gate's hole the far viewer stands, well past any view range.</summary>
    private const int FarAway = 160;

    /// <summary>A ground viewer far from every hole has no eye on the cavern, loads none of it and is sent none.</summary>
    [Test]
    public async Task GroundViewerFarFromMouthsLoadsNoCavern()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableCaverns(pair);
        await DisableClaims(pair);
        await EnablePvs(pair);
        var world = await BuildWorld(pair, Surface);
        var gate = await Gate(pair, world);
        var tile = new Vector2i(gate.Max.X + FarAway, gate.Origin.Y);
        var pos = TileCentre(tile);

        // Terrain under the viewer before it spawns, so it stands on ground from its first tick.
        await LoadChunks(pair, world.Ground, tile - new Vector2i(ChunkSize, ChunkSize), tile + new Vector2i(ChunkSize, ChunkSize));

        var viewer = await PlanetFixture.AttachViewer(pair, world.Ground, pos);
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var cavernBiome = entMan.GetComponent<BiomeComponent>(world.Cavern);
            var groundBiome = entMan.GetComponent<BiomeComponent>(world.Ground);
            var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
            // Past the hand-loaded patch, so only the viewer's own loader can have loaded it.
            var farChunk = SharedMapSystem.GetChunkIndices(tile + new Vector2i(3 * ChunkSize, 0), ChunkSize) * ChunkSize;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(viewer).MapUid, Is.EqualTo(world.Ground),
                    "Precondition: the viewer left the ground.");
                Assert.That(WFCavernEyeSystem.AnyHoleWithin(ground, pos, FarAway / 2f), Is.False,
                    "Precondition: a hole lies near the far viewer.");
                Assert.That(EyesOn(entMan, world.Cavern), Is.Empty, "A ground viewer far from every hole has an eye on the cavern.");
                Assert.That(EyesOn(entMan, world.Layers[1]), Is.Not.Empty,
                    "Precondition: the ground viewer has no eye on the air layer above it.");
                Assert.That(groundBiome.LoadedChunks, Does.Contain(farChunk),
                    "Precondition: the viewer's own chunk loading never ran.");
                Assert.That(cavernBiome.LoadedChunks, Is.Empty, "A ground viewer far from every hole loaded cavern chunks.");
                Assert.That(entMan.HasComponent<WFCavernViewerComponent>(viewer), Is.False,
                    "A ground viewer far from every hole is recorded as seeing the cavern.");
            }
        });

        var (serverEntities, clientEntities) = await CavernEntities(pair, world);
        TestContext.Out.WriteLine($"Far viewer: {serverEntities} entities on the cavern, {clientEntities} sent to the client.");
        Assert.That(clientEntities, Is.Zero, "A ground viewer far from every hole was sent cavern entities.");

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A ground viewer on a mouth's lip has one eye on the cavern, the cavern chunks under the hole and around it load,
    /// and PVS sends it the cavern around it. Logs what that costs.
    /// </summary>
    [Test]
    public async Task GroundViewerNearMouthLoadsCavern()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableCaverns(pair);
        await DisableClaims(pair);
        await EnablePvs(pair);
        var world = await BuildWorld(pair, Surface);
        var gate = await Gate(pair, world);

        var viewer = await PlanetFixture.AttachViewer(pair, world.Ground, TileCentre(gate.ClimbTile));
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var cavernBiome = entMan.GetComponent<BiomeComponent>(world.Cavern);
            var underHole = SharedMapSystem.GetChunkIndices(gate.Origin, ChunkSize) * ChunkSize;
            // Three chunks east of the climb tile: off the pad, so only a loader around the eye loads it.
            var aside = SharedMapSystem.GetChunkIndices(gate.ClimbTile + new Vector2i(3 * ChunkSize, 0), ChunkSize) * ChunkSize;
            var roofs = server.System<SharedRoofSystem>();
            Entity<MapGridComponent, RoofComponent> cavernRoof =
                (world.Cavern, entMan.GetComponent<MapGridComponent>(world.Cavern), entMan.GetComponent<RoofComponent>(world.Cavern));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(viewer).MapUid, Is.EqualTo(world.Ground),
                    "Precondition: the viewer left the ground.");
                Assert.That(EyesOn(entMan, world.Cavern), Has.Count.EqualTo(1),
                    "A ground viewer beside a mouth has no single eye on the cavern.");
                Assert.That(cavernBiome.LoadedChunks, Does.Contain(underHole), "The cavern chunk under the hole never loaded.");
                Assert.That(cavernBiome.LoadedChunks, Does.Contain(aside), "The cavern around the eye never loaded.");
                Assert.That(entMan.GetComponent<WFCavernViewerComponent>(viewer).Ground, Is.EqualTo(world.Ground),
                    "The viewer is not recorded as seeing this ground's cavern.");
                // What the view shows is lit as it should be: open to the sky under the hole, roofed under the lip.
                Assert.That(roofs.IsRooved(cavernRoof, gate.Origin), Is.False, "The cavern under the hole is roofed.");
                Assert.That(roofs.IsRooved(cavernRoof, gate.ClimbTile), Is.True, "The cavern under the lip is open to the sky.");
            }
        });

        // What the cavern view costs: chunks generated, entities on the cavern, and how many PVS sends.
        var chunks = 0;
        await server.WaitPost(() => chunks = entMan.GetComponent<BiomeComponent>(world.Cavern).LoadedChunks.Count);
        var (serverEntities, clientEntities) = await CavernEntities(pair, world);

        TestContext.Out.WriteLine($"Cavern view cost: {chunks} cavern chunks loaded, {serverEntities} entities on the cavern, " +
                                  $"{clientEntities} of them sent to the client.");
        Assert.That(clientEntities, Is.GreaterThan(0), "No cavern entity reached the client.");

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The cavern eye comes within <see cref="WFCavernEyeSystem.EnterMargin"/> of the view range and goes only past
    /// <see cref="WFCavernEyeSystem.LeaveMargin"/>, so a viewer at the edge doesn't churn eyes.
    /// </summary>
    [Test]
    public async Task CavernEyeKeepsAMarginBeforeLeaving()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);
        var gate = await Gate(pair, world);
        // An eye sees a square whose side is net.pvs_range.
        var range = server.ResolveDependency<IConfigurationManager>().GetCVar(CVars.NetMaxUpdateRange) / 2f;
        var between = (WFCavernEyeSystem.EnterMargin + WFCavernEyeSystem.LeaveMargin) / 2f;

        var viewer = await PlanetFixture.AttachViewer(pair, world.Ground, TileCentre(gate.ClimbTile));
        // It stands over unloaded ground as it moves; the test is about eyes, not falling.
        await server.WaitPost(() => entMan.RemoveComponent<CEZPhysicsComponent>(viewer));
        await pair.RunTicksSync(10);

        await AssertCavernEye(pair, world, true, "beside the mouth");

        await MoveEastOfHole(pair, gate, viewer, range + between);
        await AssertCavernEye(pair, world, true, "between the margins after seeing it");

        await MoveEastOfHole(pair, gate, viewer, range + WFCavernEyeSystem.LeaveMargin + 4f);
        await AssertCavernEye(pair, world, false, "past the leave margin");

        await MoveEastOfHole(pair, gate, viewer, range + between);
        await AssertCavernEye(pair, world, false, "between the margins after losing it");

        await MoveEastOfHole(pair, gate, viewer, range + WFCavernEyeSystem.EnterMargin / 2f);
        await AssertCavernEye(pair, world, true, "inside the enter margin");

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A ghost that may not generate terrain gets no eye on the cavern beside a mouth, so it never generates it; an admin
    /// ghost, which may, does.
    /// </summary>
    [TestCase("MobObserver", false)]
    [TestCase("AdminObserver", true)]
    public async Task GhostSeesCavernOnlyIfItMayLoadTerrain(string ghost, bool sees)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);
        var gate = await Gate(pair, world);

        var viewer = await PlanetFixture.AttachViewer(pair, world.Ground, TileCentre(gate.ClimbTile), ghost);
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var cavernBiome = entMan.GetComponent<BiomeComponent>(world.Cavern);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<GhostComponent>(viewer), Is.True, $"Precondition: {ghost} is no ghost.");
                Assert.That(entMan.GetComponent<TransformComponent>(viewer).MapUid, Is.EqualTo(world.Ground),
                    "Precondition: the ghost left the ground.");
                Assert.That(EyesOn(entMan, world.Cavern), sees ? Has.Count.EqualTo(1) : Is.Empty,
                    sees ? "An admin ghost beside a mouth has no eye on the cavern." : "A ghost has an eye on the cavern.");
                Assert.That(cavernBiome.LoadedChunks, sees ? Is.Not.Empty : Is.Empty,
                    sees ? "An admin ghost beside a mouth loaded no cavern." : "A ghost loaded cavern chunks.");
            }
        });

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>A viewer on the first air layer right over a mouth has an eye on the ground but none on the cavern, and loads none of it.</summary>
    [Test]
    public async Task AirViewerOverMouthLoadsNoCavern()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);
        var gate = await Gate(pair, world);

        var viewer = await PlanetFixture.AttachViewer(pair, world.Layers[1], TileCentre(gate.Origin));
        // Hovering: without z-physics it stays on the air layer.
        await server.WaitPost(() => entMan.RemoveComponent<CEZPhysicsComponent>(viewer));
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(viewer).MapUid, Is.EqualTo(world.Layers[1]),
                    "Precondition: the viewer left the air layer.");
                Assert.That(WFCavernEyeSystem.AnyHoleWithin(ground, TileCentre(gate.Origin), 1f), Is.True,
                    "Precondition: the viewer is not over the mouth.");
                Assert.That(EyesOn(entMan, world.Ground), Has.Count.EqualTo(1), "An air viewer has no eye on the ground.");
                Assert.That(EyesOn(entMan, world.Cavern), Is.Empty, "An air viewer over a mouth has an eye on the cavern.");
                Assert.That(entMan.GetComponent<BiomeComponent>(world.Cavern).LoadedChunks, Is.Empty,
                    "An air viewer over a mouth loaded cavern chunks.");
                Assert.That(entMan.HasComponent<WFCavernViewerComponent>(viewer), Is.False,
                    "An air viewer is recorded as seeing the cavern.");
            }
        });

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>A cavern viewer has an eye on the ground above it, and the ground chunks over it load.</summary>
    [Test]
    public async Task CavernViewerLoadsGroundAbove()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var viewerPos = new Vector2(40.5f, 40.5f);

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);

        var viewer = await PlanetFixture.AttachViewer(pair, world.Cavern, viewerPos);
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var groundBiome = entMan.GetComponent<BiomeComponent>(world.Ground);
            var tile = new Vector2i((int) viewerPos.X, (int) viewerPos.Y);
            var overhead = SharedMapSystem.GetChunkIndices(tile, ChunkSize) * ChunkSize;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(viewer).MapUid, Is.EqualTo(world.Cavern),
                    "Precondition: the viewer left the cavern.");
                Assert.That(EyesOn(entMan, world.Ground), Is.Not.Empty, "A cavern viewer has no eye on the ground above it.");
                Assert.That(groundBiome.LoadedChunks, Does.Contain(overhead),
                    "The ground chunk over a cavern viewer never loaded, so nothing roofs it.");
            }
        });

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>Puts the viewer this far east of the gate's hole, level with its anchor, and lets the eye check run twice.</summary>
    private static async Task MoveEastOfHole(TestPair pair, WFCavernMouth gate, EntityUid viewer, float distance)
    {
        var server = pair.Server;
        var map = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            map = server.EntMan.GetComponent<TransformComponent>(viewer).MapUid!.Value;
            server.System<SharedTransformSystem>().SetCoordinates(viewer,
                new EntityCoordinates(map, new Vector2(gate.Max.X + 1 + distance, gate.Origin.Y + 0.5f)));
        });

        await pair.RunTicksSync(pair.SecondsToTicks((float) WFCavernEyeSystem.CheckInterval.TotalSeconds * 2) + 2);
    }

    private static async Task AssertCavernEye(TestPair pair, World world, bool expected, string where)
    {
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(EyesOn(pair.Server.EntMan, world.Cavern), expected ? Has.Count.EqualTo(1) : Is.Empty,
                expected ? $"No eye on the cavern {where}." : $"An eye on the cavern {where}.");
        });
    }

    /// <summary>Every z-level eye standing on a map.</summary>
    private static List<EntityUid> EyesOn(IEntityManager entMan, EntityUid map)
    {
        var found = new List<EntityUid>();
        var query = entMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            if (meta.EntityPrototype?.ID == EyeProto && xform.MapUid == map)
                found.Add(uid);
        }

        return found;
    }

    /// <summary>Turns PVS on, which test pairs run without, so the client gets only what its eyes see.</summary>
    // TestPair reverts it when the pair is returned.
    private static Task EnablePvs(TestPair pair)
    {
        return pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(CVars.NetPVS, true));
    }

    /// <summary>How many entities stand on the cavern on the server, and on the client once PVS has sent them.</summary>
    private static async Task<(int Server, int Client)> CavernEntities(TestPair pair, World world)
    {
        var serverCount = 0;
        var clientCount = 0;
        var cavernNet = NetEntity.Invalid;

        // At PVS's budget of 50 new entities a tick, 90 ticks covers the cavern eye's square several times over.
        await pair.RunTicksSync(90);
        await pair.Server.WaitPost(() =>
        {
            serverCount = CountOn(pair.Server.EntMan, world.Cavern);
            cavernNet = pair.Server.EntMan.GetNetEntity(world.Cavern);
        });
        await pair.Client.WaitPost(() =>
            clientCount = CountOn(pair.Client.EntMan, pair.Client.EntMan.GetEntity(cavernNet)));

        return (serverCount, clientCount);
    }

    /// <summary>How many entities stand on a map, the map itself aside.</summary>
    private static int CountOn(IEntityManager entMan, EntityUid map)
    {
        var count = 0;
        var query = entMan.AllEntityQueryEnumerator<TransformComponent>();

        while (query.MoveNext(out var uid, out var xform))
        {
            if (uid != map && xform.MapUid == map)
                count++;
        }

        return count;
    }
}
