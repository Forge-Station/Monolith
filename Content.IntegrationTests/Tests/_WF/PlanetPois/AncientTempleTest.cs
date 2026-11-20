#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Caverns;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Bounds;
using Content.Server.NPC.HTN;
using Content.Server.Parallax;
using Content.Shared.Xenoarchaeology.Artifact.Components;
using Content.Shared._WF.Caverns;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.NPC.Systems;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>
/// The ancient temple: a shrine on the surface over stairs down to its halls in the cavern, guarded by skeletons with
/// swords, with relics in its vault, on every world but the gut.
/// </summary>
[TestFixture]
[TestOf(typeof(WFPlanetPoiSystem))]
public sealed class AncientTempleTest
{
    private const string TemplePath = "/Maps/_WF/PlanetPois/ancient_temple.yml";
    private const string BelowPath = "/Maps/_WF/PlanetPois/ancient_temple_below.yml";
    private const int Radius = 128;
    private const int Guardians = 7;
    private const int Relics = 3;

    [TestPrototypes]
    private const string Prototypes = @"
- type: wfPlanetPoiTable
  id: WFPoisTestTemple
  minCount: 1
  maxCount: 1
  spacing: 32
  edgeMargin: 40
  centreClear: 24
  entries:
  - mapPath: /Maps/_WF/PlanetPois/ancient_temple.yml
    name: wf-poi-ancient-temple
    expeditions: false
    footprint: -15,-1,14,31
    minCount: 1
    maxCount: 1
    connection: Isolated
    sockets:
    - offset: 2,-1
      direction: South
    rotate: true
    below: /Maps/_WF/PlanetPois/ancient_temple_below.yml
";

    [Test]
    public async Task TempleIsOnEveryWorldButTheGut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            foreach (var table in new[] { "WFPoisAsclepiu", "WFPoisMerak", "WFPoisFervidus", "WFPoisThrascias", "WFPoisAerumna" })
            {
                var entries = server.ProtoMan.Index<WFPlanetPoiTablePrototype>(table).Entries.Where(e => e.MapPath == TemplePath).ToList();
                Assert.That(entries, Has.Count.EqualTo(1), $"{table} has no temple entry.");
                Assert.That(entries[0].MinCount, Is.Zero, $"{table} always places a temple; it should be a find.");
                Assert.That(entries[0].MaxCount, Is.EqualTo(1), table);
                Assert.That(entries[0].Below, Is.EqualTo(BelowPath), table);
                Assert.That(entries[0].Name.Id, Is.EqualTo("wf-poi-ancient-temple"), table);
            }

            Assert.That(server.ProtoMan.Index<WFPlanetPoiTablePrototype>("WFPoisCarcinoma").Entries.Any(e => e.MapPath == TemplePath),
                Is.False, "The gut world has a temple.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Placed on a world with caverns, turned however the seed turns it, the shrine's floor opens over the stairs, the
    /// halls lie under it with the stairs coming up onto the shrine's floor, the guardians stand with their swords and
    /// the relic spawners have left their relics.
    /// </summary>
    [TestCase(1977)]
    [TestCase(8231)]
    public async Task TemplePlacesOverItsHalls(int seed)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, ServerSeed = seed });
        var server = pair.Server;
        var entMan = server.EntMan;
        await PlanetFixture.EnableFeature(pair);
        var layers = new List<EntityUid>();
        var lower = new List<EntityUid>();
        var holds = new List<Task>();

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(PlanetCVars.Bounds, true);
            server.CfgMan.SetCVar(PlanetCVars.Radius, Radius);
            server.CfgMan.SetCVar(PlanetCVars.PoiTable, "WFPoisTestTemple");
            server.CfgMan.SetCVar(CavernCVars.Caverns, true);
            var surface = server.ProtoMan.Index<WFPlanetSurfacePrototype>(PlanetFixture.SurfaceProto);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Asclepiu", null);
            Assert.That(network, Is.Not.Null);
            var comp = entMan.GetComponent<WFPlanetNetworkComponent>(network!.Value);
            layers.AddRange(comp.Layers);
            lower.AddRange(comp.LowerLayers);
            var preload = new WFPlanetPreloadStartingEvent(layers[0], Vector2.Zero, Radius, holds);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref preload);
        });

        try
        {
            for (var attempt = 0; attempt < 900 && holds.Any(hold => !hold.IsCompleted); attempt++)
                await pair.RunTicksSync(10);
            // The hole queue fits out the opened stairwell over the next ticks.
            await pair.RunTicksSync(30);

            await server.WaitAssertion(() =>
            {
                Assert.That(holds.All(hold => hold.IsCompletedSuccessfully), Is.True, "The world never finished placing its sites.");
                var ground = layers[0];
                var record = entMan.GetComponent<WFPlanetPoiComponent>(ground);
                Assert.That(record.GenerationError, Is.Null);
                var temple = record.Sites.SingleOrDefault(site => site.MapPath == TemplePath);
                Assert.That(temple, Is.Not.Null, "The temple was not placed.");
                Assert.That(lower, Is.Not.Empty, "The world has no cavern for the temple's halls.");

                var maps = server.System<SharedMapSystem>();
                var groundGrid = entMan.GetComponent<MapGridComponent>(ground);
                var below = lower[0];
                var belowGrid = entMan.GetComponent<MapGridComponent>(below);

                // One opening in the shrine's floor, unroofed, over the stairs; the stairs come up onto its floor.
                Assert.That(temple!.Shafts, Has.Count.EqualTo(1), "The shrine's floor isn't open over the stairs.");
                var shaft = temple.Shafts.Single();
                Assert.That(maps.GetTileRef(ground, groundGrid, shaft).Tile.IsEmpty, Is.True, "The stairwell is floored over.");
                var roofs = server.System<SharedRoofSystem>();
                Assert.That(roofs.IsRooved((ground, groundGrid, entMan.GetComponent<RoofComponent>(ground)), shaft), Is.False, "The stairwell is roofed.");
                Assert.That(maps.GetTileRef(below, belowGrid, shaft).Tile.IsEmpty, Is.False, "There's no hall under the stairwell.");
                var stairs = maps.GetAnchoredEntities((below, belowGrid), shaft)
                    .FirstOrDefault(uid => entMan.HasComponent<WFCavernStairsComponent>(uid));
                var found = string.Join("; ", entMan.AllEntities<WFCavernStairsComponent>().Select(ent =>
                {
                    var xf = entMan.GetComponent<TransformComponent>(ent);
                    return $"{entMan.ToPrettyString(ent)} map {xf.MapUid} parent {xf.ParentUid} pos {xf.LocalPosition} anchored {xf.Anchored}";
                }));
                Assert.That(stairs, Is.Not.EqualTo(EntityUid.Invalid), $"The temple's stairs aren't under the stairwell at {shaft} (below {below}, origin {temple.Origin}): {found}");
                var exit = SharedWFCavernStairsSystem.ExitOf(shaft, entMan.GetComponent<TransformComponent>(stairs).LocalRotation.GetCardinalDir());
                Assert.That(maps.GetTileRef(ground, groundGrid, exit).Tile.IsEmpty, Is.False, "The stairs come up into the stairwell's edge, not onto floor.");
                Assert.That(Math.Abs(exit.X - shaft.X) + Math.Abs(exit.Y - shaft.Y), Is.EqualTo(1));
                Assert.That(roofs.IsRooved((ground, groundGrid, entMan.GetComponent<RoofComponent>(ground)), exit), Is.True, "The stairs come up outside the shrine.");
                var hardAtExit = maps.GetAnchoredEntities((ground, groundGrid), exit).Any(uid =>
                    entMan.TryGetComponent<PhysicsComponent>(uid, out var body) && body.CanCollide && body.Hard);
                Assert.That(hardAtExit, Is.False, "Something stands at the top of the stairs.");
                var holes = entMan.GetComponent<WFCavernGroundComponent>(ground);
                Assert.That(holes.Shades.ContainsKey(shaft), Is.True, "The stairwell was never opened as a hole.");

                // No other gap left in the shrine's ground.
                var biome = (ground, entMan.GetComponent<BiomeComponent>(ground));
                var biomes = server.System<BiomeSystem>();
                var gaps = 0;
                for (var x = temple.Bounds.Left; x < temple.Bounds.Right; x++)
                for (var y = temple.Bounds.Bottom; y < temple.Bounds.Top; y++)
                {
                    var index = new Vector2i(x, y);
                    if (biomes.WfIsPinned(biome, index) && maps.GetTileRef(ground, groundGrid, index).Tile.IsEmpty && !temple.Shafts.Contains(index))
                        gaps++;
                }
                Assert.That(gaps, Is.Zero, $"The temple left {gaps} empty tiles in its footprint.");

                // The guardians, each holding its blade, hostile, with a brain that fights.
                var hands = server.System<SharedHandsSystem>();
                var factions = server.System<NpcFactionSystem>();
                var guardians = entMan.AllEntities<HTNComponent>().Select(ent => ent.Owner)
                    .Where(uid => entMan.GetComponent<TransformComponent>(uid).MapUid == below && Proto(entMan, uid) == "WFTempleSkeleton").ToList();
                Assert.That(guardians, Has.Count.EqualTo(Guardians), "The halls don't have their guardians.");
                foreach (var guardian in guardians)
                {
                    Assert.That(hands.EnumerateHeld(guardian).Any(item => Proto(entMan, item) == "WFTempleBlade"), Is.True, $"{entMan.ToPrettyString(guardian)} has no blade.");
                    Assert.That(factions.IsMember(guardian, "SimpleHostile"), Is.True, $"{entMan.ToPrettyString(guardian)} isn't hostile.");
                    Assert.That(entMan.GetComponent<HTNComponent>(guardian).RootTask.Task, Is.EqualTo("SimpleHumanoidHostileCompound"));
                }

                // Every relic spawner has left an artifact.
                var spawners = entMan.AllEntities<MetaDataComponent>().Count(uid => Proto(entMan, uid) == "WFTempleRelicSpawner");
                Assert.That(spawners, Is.Zero, "A relic spawner never spawned.");
                var artifacts = entMan.AllEntities<XenoArtifactComponent>()
                    .Count(uid => entMan.GetComponent<TransformComponent>(uid).MapUid == below && Proto(entMan, uid).EndsWith("XenoArtifactItem"));
                Assert.That(artifacts, Is.EqualTo(Relics), "The temple's relics are missing.");
            });
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A guardian next to someone cuts them with its blade.</summary>
    [Test]
    public async Task GuardianAttacksWithItsBlade()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, false));
        var map = await pair.CreateTestMap();
        EntityUid guardian = default;
        EntityUid victim = default;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -4; x <= 4; x++)
            for (var y = -4; y <= 4; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            victim = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 1.5f, 0.5f));
            // Held in place, so the guardian swings rather than shoving it about.
            server.System<SharedPhysicsSystem>().SetBodyType(victim, BodyType.Static);
            guardian = entMan.SpawnEntity("WFTempleSkeleton", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
        });

        var slashed = false;
        for (var second = 0; second < 15 && !slashed; second++)
        {
            await pair.RunSeconds(1);
            await server.WaitPost(() =>
            {
                var damage = entMan.GetComponent<DamageableComponent>(victim).Damage.DamageDict;
                // The blade cuts; the airless test room only bruises and chokes.
                slashed = damage.TryGetValue("Slash", out var slash) && slash > FixedPoint2.Zero;
            });
        }

        await server.WaitAssertion(() =>
        {
            var hands = server.System<SharedHandsSystem>();
            Assert.That(hands.GetActiveItem(guardian) is { } held && Proto(entMan, held) == "WFTempleBlade", Is.True, "The guardian isn't wielding its blade.");
            Assert.That(slashed, Is.True, "The guardian never cut the man beside it.");
        });

        await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, CCVars.NPCPauseWhenNoPlayersInRange.DefaultValue));
        await pair.CleanReturnAsync();
    }

    private static string Proto(IEntityManager entMan, EntityUid uid) =>
        entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID ?? string.Empty;
}
