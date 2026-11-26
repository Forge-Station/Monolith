#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.CavernEncounters;
using Content.Server._WF.Caverns;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Chat.Systems;
using Content.Server.Light.EntitySystems;
using Content.Shared._WF.CavernEncounters;
using Content.Shared._WF.Planets;
using Content.Shared.Atmos;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Light.Components;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Stacks;
using Content.Server._WF.Traders;
using Content.Shared._WF.Traders;
using Robust.Shared.Audio;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.CavernEncounters;

/// <summary>
/// Cavern encounters: planned spaced out on every cavern, each laid out whole in its own room and never where it
/// shouldn't be, and each one's core rule: raiders run when hurt, the surveyor pays on the surface, the trapped miner is
/// dug out, crawlers hear only runners, the Lamplighter freezes in the light and the Echo repeats what it heard.
/// </summary>
[TestFixture]
[TestOf(typeof(WFCavernEncounterSystem))]
public sealed class CavernEncounterTest
{
    private const string AudioRoot = "/Audio/_WF/CavernEncounters/";

    /// <summary>A breathable world whose cavern holds every kind of person.</summary>
    private const string Surface = "WFSurfaceAsclepiu";

    /// <summary>The Old Wise Miner, from the Minigames module.</summary>
    private const string OldWiseMiner = "WFMinigameOldMiner";

    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";

    private static readonly ProtoId<WFCavernEncounterPrototype> MinerCamp = "WFCavernEncounterMinerCamp";

    /// <summary>
    /// Every world plans its area's count of encounters on its cavern alone, no kind past its limit, the first ones near
    /// the gate, spaced apart and clear of the centre; bigger worlds plan more.
    /// </summary>
    [Test]
    public async Task EveryCavernPlansSpacedEncounters()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var system = server.System<WFCavernEncounterSystem>();

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var counts = new List<(int Radius, int Count)>();

        foreach (var surface in Surfaces)
        {
            var world = await BuildWorld(pair, surface);
            try
            {
                await server.WaitAssertion(() =>
                {
                    var table = proto.EnumeratePrototypes<WFCavernEncounterTablePrototype>().Single(t => t.Surface == surface);
                    var sites = system.GetSites(world.Cavern);
                    var centre = entMan.GetComponent<WFCavernGroundComponent>(world.Ground).Centre;

                    var radius = proto.Index<WFPlanetSurfacePrototype>(surface).Radius;
                    var planned = WFCavernEncounterSystem.PlannedCount(table, radius);
                    TestContext.Out.WriteLine($"{surface}: radius {radius}, {sites.Count} encounters planned " +
                        $"({string.Join(", ", sites.GroupBy(s => s.Encounter.Id).Select(g => $"{g.Key.Replace("WFCavernEncounter", string.Empty)} {g.Count()}"))})");
                    counts.Add((radius, sites.Count));

                    Assert.That(sites, Has.Count.EqualTo(planned), $"{surface} planned the wrong number.");
                    Assert.That(planned, Is.EqualTo((int) MathF.Round(WFCavernEncounterSystem.RingArea(radius) / WFCavernEncounterSystem.AreaUnit * table.PerArea)),
                        $"{surface}'s count isn't its area's.");
                    foreach (var (kind, limit) in table.Limits)
                    {
                        Assert.That(sites.Count(s => s.Encounter == kind), Is.LessThanOrEqualTo(limit), $"{surface} has too many {kind}.");
                    }

                    for (var i = 0; i < Math.Min(table.NearGate, sites.Count); i++)
                    {
                        Assert.That(Vector2.Distance(Middle(sites[i].Planned), centre),
                            Is.LessThanOrEqualTo(WFCavernEncounterSystem.CentreClearance + WFCavernEncounterSystem.NearGateReach + 1f),
                            $"{surface}: the gate's encounter {sites[i].Encounter} is far from the gate.");
                    }

                    Assert.That(entMan.HasComponent<WFCavernEncounterLayerComponent>(world.Ground), Is.False, "The ground has encounters.");

                    for (var i = 0; i < sites.Count; i++)
                    {
                        var a = Middle(sites[i].Planned);
                        Assert.That(Vector2.Distance(a, centre), Is.GreaterThanOrEqualTo(WFCavernEncounterSystem.CentreClearance - 1f),
                            $"{surface}: {sites[i].Encounter} sits on the gate.");

                        for (var j = i + 1; j < sites.Count; j++)
                        {
                            Assert.That(Vector2.Distance(a, Middle(sites[j].Planned)), Is.GreaterThanOrEqualTo(WFCavernEncounterSystem.MinSpacing - 1f),
                                $"{surface}: {sites[i].Encounter} and {sites[j].Encounter} are too close.");
                        }
                    }
                });
            }
            finally
            {
                await Teardown(pair, world);
            }
        }

        foreach (var bigger in counts)
        {
            foreach (var smaller in counts.Where(c => c.Radius < bigger.Radius))
            {
                Assert.That(bigger.Count, Is.GreaterThan(smaller.Count), $"A world of radius {bigger.Radius} plans no more than one of {smaller.Radius}.");
            }
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Each encounter is laid out whole in a room of open, pinned cavern floor with no rock left in it, never at the
    /// gate or over another room; the planned sites lay out through the real path; the trapped miner is dug out.
    /// </summary>
    [Test]
    public async Task EachEncounterSpawnsWholeOnTheCavern()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var system = server.System<WFCavernEncounterSystem>();
        var biomes = server.System<Content.Server.Parallax.BiomeSystem>();
        var maps = server.System<SharedMapSystem>();

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            EntityUid? miner = null;
            EntityUid? rock = null;

            await server.WaitAssertion(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(world.Cavern);
                var biome = entMan.GetComponent<BiomeComponent>(world.Cavern);
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                var gate = ground.Mouths.Single(m => m.Kind == WFCavernMouthKind.Gate);
                var gateTile = new Vector2i((int) MathF.Floor(gate.Centre.X), (int) MathF.Floor(gate.Centre.Y));

                Assert.That(system.Fits(world.Cavern, gateTile, 4), Is.False, "An encounter fits over the gate.");

                var used = new List<Vector2i>();
                foreach (var encounter in proto.EnumeratePrototypes<WFCavernEncounterPrototype>().OrderBy(e => e.ID))
                {
                    var centre = FindSpot(system, world.Cavern, encounter.Radius, used);
                    Assert.That(centre, Is.Not.Null, $"No room for {encounter.ID} anywhere near the centre.");
                    used.Add(centre!.Value);

                    var spawned = system.SpawnEncounter(world.Cavern, encounter, centre.Value);
                    var ids = spawned.Select(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID).ToList();

                    foreach (var entry in encounter.Entries.Where(e => e.Ids.Count > 0))
                    {
                        Assert.That(ids.Count(entry.Ids.Select(i => i.Id).Contains), Is.GreaterThanOrEqualTo(entry.Min),
                            $"{encounter.ID} laid out too few of {string.Join(", ", entry.Ids)}.");
                    }

                    foreach (var uid in spawned)
                    {
                        var xform = entMan.GetComponent<TransformComponent>(uid);
                        Assert.That(xform.MapUid, Is.EqualTo(world.Cavern), $"{entMan.ToPrettyString(uid)} isn't on the cavern.");
                        var tile = maps.TileIndicesFor(world.Cavern, grid, xform.Coordinates);
                        Assert.That(maps.TryGetTileRef(world.Cavern, grid, tile, out var tileRef) && !tileRef.Tile.IsEmpty, Is.True,
                            $"{entMan.ToPrettyString(uid)} of {encounter.ID} stands on no floor.");
                        Assert.That((tile - centre.Value).Length, Is.LessThanOrEqualTo(encounter.Radius + 0.01f),
                            $"{entMan.ToPrettyString(uid)} of {encounter.ID} is outside its room.");
                    }

                    foreach (var tile in WFCavernEncounterSystem.Disc(centre.Value, encounter.Radius))
                    {
                        Assert.That(biomes.WfIsPinned((world.Cavern, biome), tile), Is.True, $"{encounter.ID} left {tile} unpinned.");
                        foreach (var anchored in maps.GetAnchoredEntities(world.Cavern, grid, tile))
                        {
                            Assert.That(biomes.WfIsBiomeSpawned((world.Cavern, biome), anchored, tile), Is.False,
                                $"{encounter.ID} left the biome's {entMan.ToPrettyString(anchored)} in its room.");
                        }
                    }

                    Assert.That(system.Fits(world.Cavern, centre.Value, encounter.Radius), Is.False, $"A room fits over {encounter.ID}.");
                    if (!proto.HasIndex<EntityPrototype>(OldWiseMiner))
                        Assert.That(ids, Has.No.Member(OldWiseMiner), "A pending prototype spawned before it exists.");

                    if (encounter.Collapse)
                    {
                        miner = spawned.Single(entMan.HasComponent<WFTrappedMinerComponent>);
                        var walls = spawned.Where(entMan.HasComponent<WFCavernCollapseRockComponent>).ToList();
                        Assert.That(walls, Has.Count.EqualTo(8), "The collapse doesn't ring the miner.");
                        Assert.That(entMan.GetComponent<WFTrappedMinerComponent>(miner.Value).Rocks, Is.EquivalentTo(walls));
                        rock = walls[0];
                    }
                }

                // The planned sites through the real path.
                var sites = system.GetSites(world.Cavern);
                foreach (var site in sites)
                {
                    system.TryActivate(world.Cavern, site);
                }

                Assert.That(sites.Count(s => s.State == WFCavernEncounterState.Spawned), Is.GreaterThan(0), "No planned site found room.");
                foreach (var site in sites.Where(s => s.State == WFCavernEncounterState.Spawned))
                {
                    Assert.That(Vector2.Distance(Middle(site.Centre!.Value), Middle(site.Planned)), Is.LessThanOrEqualTo(43f),
                        $"{site.Encounter} wandered off its spot.");
                    Assert.That(site.Spawned, Is.Not.Empty);
                }
            });

            Assert.That(miner, Is.Not.Null, "No encounter has a collapse.");

            // Dig one rock of the collapse out.
            await server.WaitPost(() =>
            {
                Assert.That(server.System<MobStateSystem>().IsAlive(miner!.Value), Is.True, "The trapped miner died in his pocket.");
                entMan.DeleteEntity(rock!.Value);
            });
            await server.WaitRunTicks(2);

            await server.WaitAssertion(() =>
            {
                var comp = entMan.GetComponent<WFTrappedMinerComponent>(miner!.Value);
                Assert.That(comp.Freed, Is.True, "Digging through the collapse didn't free the miner.");
                Assert.That(CashNear(entMan, miner.Value, 2f), Is.GreaterThanOrEqualTo(comp.Reward), "The miner left no reward.");
                Assert.That(server.System<WFCavernWalkerSystem>().MoveTarget(miner.Value), Is.Not.Null, "The miner isn't walking off.");
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>The surveyor follows whoever leads him, comes up when they climb out beside him, and pays them on the surface.</summary>
    [Test]
    public async Task SurveyorPaysOnTheSurface()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();
        var maps = server.System<SharedMapSystem>();

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            var spot = Vector2i.Zero;
            await server.WaitAssertion(() =>
            {
                var ground = entMan.GetComponent<WFCavernGroundComponent>(world.Ground);
                var gate = ground.Mouths.Single(m => m.Kind == WFCavernMouthKind.Gate);
                spot = new Vector2i((int) gate.Centre.X + 16, (int) gate.Centre.Y);
            });

            await LoadChunks(pair, world.Ground, spot - new Vector2i(16, 16), spot + new Vector2i(16, 16));
            await LoadChunks(pair, world.Cavern, spot - new Vector2i(16, 16), spot + new Vector2i(16, 16));

            EntityUid surveyor = default;
            EntityUid leader = default;
            await server.WaitPost(() =>
            {
                // Solid ground to come out on.
                var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);
                var floor = server.ResolveDependency<ITileDefinitionManager>()["FloorPlanetDirt"].TileId;
                for (var x = -2; x <= 2; x++)
                for (var y = -2; y <= 2; y++)
                    maps.SetTile(world.Ground, groundGrid, spot + new Vector2i(x, y), new Tile(floor));

                var cavernGrid = entMan.GetComponent<MapGridComponent>(world.Cavern);
                surveyor = entMan.SpawnEntity("WFMobLostSurveyor", maps.GridTileToLocal(world.Cavern, cavernGrid, spot));
                leader = entMan.SpawnEntity("MobHuman", maps.GridTileToLocal(world.Cavern, cavernGrid, spot + new Vector2i(1, 0)));
            });
            await server.WaitRunTicks(pair.SecondsToTicks(1));

            await server.WaitPost(() => server.System<WFCavernSurveyorSystem>().Lead((surveyor, entMan.GetComponent<WFCavernSurveyorComponent>(surveyor)), leader));
            await server.WaitAssertion(() =>
            {
                Assert.That(server.System<WFCavernWalkerSystem>().Leader(surveyor), Is.EqualTo(leader), "He isn't following his leader.");
                Assert.That(entMan.GetComponent<WFCavernSurveyorComponent>(surveyor).Paid, Is.False, "He paid in the cavern.");
            });

            // The leader climbs out beside him.
            await server.WaitPost(() =>
            {
                var groundGrid = entMan.GetComponent<MapGridComponent>(world.Ground);
                transform.SetCoordinates(leader, maps.GridTileToLocal(world.Ground, groundGrid, spot + new Vector2i(1, 0)));
            });
            await server.WaitRunTicks(pair.SecondsToTicks(2));

            await server.WaitAssertion(() =>
            {
                var comp = entMan.GetComponent<WFCavernSurveyorComponent>(surveyor);
                Assert.That(entMan.GetComponent<TransformComponent>(surveyor).MapUid, Is.EqualTo(world.Ground), "He didn't come up with his leader.");
                Assert.That(server.System<MobStateSystem>().IsAlive(surveyor), Is.True);
                Assert.That(comp.Paid, Is.True, "He didn't pay on the surface.");
                Assert.That(CashNear(entMan, leader, 2f), Is.GreaterThanOrEqualTo(comp.Reward), "His leader got no reward.");
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A raider badly hurt runs directly away from whoever hurt him, and drops scrap and cash when he dies.</summary>
    [Test]
    public async Task RaiderFleesWhenHurt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Rig(pair);
        var walker = server.System<WFCavernWalkerSystem>();

        EntityUid raider = default;
        EntityUid attacker = default;
        await server.WaitPost(() =>
        {
            raider = entMan.SpawnEntity("WFMobTunnelRaider", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            attacker = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, -2.5f, 0.5f));
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var damage = server.System<DamageableSystem>();
            var blunt = server.ResolveDependency<IPrototypeManager>().Index(Blunt);
            var comp = entMan.GetComponent<WFCavernRaiderComponent>(raider);

            damage.TryChangeDamage(raider, new DamageSpecifier(blunt, 10), origin: attacker);
            Assert.That(comp.Fleeing, Is.False, "A scratch sent him running.");
            Assert.That(walker.MoveTarget(raider), Is.Null);

            damage.TryChangeDamage(raider, new DamageSpecifier(blunt, comp.FleeAt), origin: attacker);
            Assert.That(comp.Fleeing, Is.True, "He didn't run once badly hurt.");
            Assert.That(comp.Attacker, Is.EqualTo(attacker));

            var target = walker.MoveTarget(raider);
            Assert.That(target, Is.Not.Null, "He has nowhere to run to.");
            var transform = server.System<SharedTransformSystem>();
            var away = transform.ToMapCoordinates(target!.Value).Position;
            Assert.That(away.X, Is.GreaterThan(10f), "He isn't running away from his attacker.");

            server.System<MobStateSystem>().ChangeMobState(raider, MobState.Dead);
            Assert.That(CashNear(entMan, raider, 1f), Is.InRange(comp.CashMin, comp.CashMax), "He dropped no cash.");
            Assert.That(NearbyIds(entMan, raider, 1f), Does.Contain("ScrapSteel"), "He dropped no scrap.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A crawler ignores a walker and goes for a runner, with its nest-mate, and loses the runner once they walk.</summary>
    [Test]
    public async Task CrawlersHearOnlyRunners()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Rig(pair);
        var crawlers = server.System<WFCavernCrawlerSystem>();
        var physics = server.System<SharedPhysicsSystem>();

        EntityUid crawler = default, mate = default, walker = default, runner = default;
        await server.WaitPost(() =>
        {
            crawler = entMan.SpawnEntity("WFMobBlindCrawler", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            mate = entMan.SpawnEntity("WFMobBlindCrawler", new EntityCoordinates(map.Grid.Owner, 0.5f, 3.5f));
            walker = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 5.5f, 0.5f));
            runner = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, -5.5f, 0.5f));
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var move = entMan.GetComponent<MovementSpeedModifierComponent>(walker);
            physics.SetLinearVelocity(walker, new Vector2(move.CurrentWalkSpeed, 0f));
            physics.SetLinearVelocity(runner, new Vector2(-move.CurrentSprintSpeed, 0f));

            Assert.That(crawlers.IsRunning(walker), Is.False, "A walker counts as running.");
            Assert.That(crawlers.IsRunning(runner), Is.True, "A runner counts as walking.");

            var comp = entMan.GetComponent<WFCavernCrawlerComponent>(crawler);
            var now = server.Timing.CurTime;
            crawlers.Listen((crawler, comp), entMan.GetComponent<TransformComponent>(crawler), now);

            Assert.That(crawlers.IsHunting(crawler, walker), Is.False, "The crawler went for a walker.");
            Assert.That(crawlers.IsHunting(crawler, runner), Is.True, "The crawler ignored a runner.");
            Assert.That(crawlers.IsHunting(mate, runner), Is.True, "The nest didn't join in.");

            // The runner walks; a few seconds on, he is lost.
            physics.SetLinearVelocity(runner, Vector2.Zero);
            crawlers.Listen((crawler, comp), entMan.GetComponent<TransformComponent>(crawler), now + comp.ForgetAfter + TimeSpan.FromSeconds(1));
            Assert.That(crawlers.IsHunting(crawler, runner), Is.False, "The crawler never lost a quarry that stopped running.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The Lamplighter can't move while a lit lantern shines on it, moves again once it is off, and puts lights out.</summary>
    [Test]
    public async Task LamplighterFreezesInTheLight()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Rig(pair);
        var handheld = server.System<HandheldLightSystem>();

        EntityUid lamplighter = default, lantern = default;
        await server.WaitPost(() =>
        {
            lamplighter = entMan.SpawnEntity("WFMobLamplighter", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            lantern = entMan.SpawnEntity("FlashlightLantern", new EntityCoordinates(map.Grid.Owner, 2.5f, 0.5f));
            entMan.GetComponent<WFLamplighterComponent>(lamplighter).NextDouse = TimeSpan.MaxValue;
            Assert.That(handheld.TurnOn(lantern, (lantern, entMan.GetComponent<HandheldLightComponent>(lantern))), Is.True, "The lantern won't light.");
        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFLamplighterComponent>(lamplighter).Lit, Is.True, "The lantern doesn't light it.");
            Assert.That(entMan.GetComponent<MovementSpeedModifierComponent>(lamplighter).CurrentSprintSpeed, Is.Zero, "It moves in the light.");
            handheld.TurnOff((lantern, entMan.GetComponent<HandheldLightComponent>(lantern)));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFLamplighterComponent>(lamplighter).Lit, Is.False);
            Assert.That(entMan.GetComponent<MovementSpeedModifierComponent>(lamplighter).CurrentSprintSpeed, Is.GreaterThan(0f), "It stays frozen in the dark.");

            handheld.TurnOn(lantern, (lantern, entMan.GetComponent<HandheldLightComponent>(lantern)));
            entMan.GetComponent<WFLamplighterComponent>(lamplighter).NextDouse = TimeSpan.Zero;
        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<HandheldLightComponent>(lantern).Activated, Is.False, "It didn't put the lantern out.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The Echo learns a line said near it, says it back in the speaker's name, and goes for whoever comes close.</summary>
    [Test]
    public async Task EchoRepeatsWhatItHears()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Rig(pair);
        const string line = "which way is the shaft";

        EntityUid echo = default, speaker = default;
        await server.WaitPost(() =>
        {
            echo = entMan.SpawnEntity("WFMobCavernEcho", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            speaker = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 7.5f, 0.5f));
            entMan.GetComponent<WFCavernEchoComponent>(echo).NextLine = TimeSpan.MaxValue;
        });
        await server.WaitRunTicks(5);

        await server.WaitPost(() => server.System<ChatSystem>().TrySendInGameICMessage(speaker, line, InGameICChatType.Speak, false));
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFCavernEchoComponent>(echo);
            Assert.That(comp.Lines.Select(l => l.Line), Has.Some.Contains("shaft"), "The Echo didn't hear the line.");
            comp.NextLine = TimeSpan.Zero;
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFCavernEchoComponent>(echo);
            Assert.That(comp.LastSpoken, Does.Contain("shaft"), "The Echo didn't say the line back.");
            Assert.That(comp.Prey, Is.Empty, "The Echo went for someone far off.");
        });
        // The Echo looks round on its own timer and may have wandered off, so the speaker keeps walking up to it.
        var caught = false;
        for (var i = 0; i < 10 && !caught; i++)
        {
            await server.WaitPost(() =>
            {
                var near = entMan.GetComponent<TransformComponent>(echo).Coordinates.Offset(new Vector2(2f, 0f));
                server.System<SharedTransformSystem>().SetCoordinates(speaker, near);
            });
            await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
            await server.WaitPost(() => caught = entMan.GetComponent<WFCavernEchoComponent>(echo).Prey.Contains(speaker));
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFCavernEchoComponent>(echo).Prey, Does.Contain(speaker), "The Echo let someone walk up to it.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every imported creature sound is used, and the import stays small.</summary>
    [Test]
    public async Task SoundsAreUsedAndFew()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var resources = server.ResolveDependency<IResourceManager>();

        await server.WaitAssertion(() =>
        {
            var used = new HashSet<string>();
            foreach (var collection in protos.EnumeratePrototypes<SoundCollectionPrototype>().Where(c => c.ID.StartsWith("WFCavernEncounter")))
            {
                Assert.That(collection.PickFiles, Is.Not.Empty, $"{collection.ID} is empty.");
                foreach (var file in collection.PickFiles)
                {
                    Assert.That(resources.ContentFileExists(file), Is.True, $"{collection.ID} names missing {file}.");
                    used.Add(file.ToString());
                }
            }

            var imported = resources.ContentFindFiles(new ResPath(AudioRoot)).Where(p => p.Extension == "ogg").Select(p => p.ToString()).ToList();
            Assert.That(imported, Is.Not.Empty);
            Assert.That(imported.Count, Is.LessThanOrEqualTo(16), "Keep the encounter import curated.");
            Assert.That(imported.Where(p => !used.Contains(p)), Is.Empty, "An imported encounter sound is used by no collection.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The miner's camp lays out the Old Wise Miner once the Minigames module provides him.</summary>
    [Test]
    public async Task MinerCampHasTheOldWiseMiner()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var system = server.System<WFCavernEncounterSystem>();

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);

        try
        {
            await server.WaitAssertion(() =>
            {
                Assert.That(proto.HasIndex<EntityPrototype>(OldWiseMiner), Is.True, $"{OldWiseMiner} doesn't exist yet.");
                var camp = proto.Index(MinerCamp);
                var centre = FindSpot(system, world.Cavern, camp.Radius, new List<Vector2i>());
                Assert.That(centre, Is.Not.Null);

                var spawned = system.SpawnEncounter(world.Cavern, camp, centre!.Value);
                Assert.That(spawned.Select(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID), Does.Contain(OldWiseMiner),
                    "The camp has no Old Wise Miner.");

                // He plays across the table set in front of him.
                var miner = spawned.Single(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == OldWiseMiner);
                server.System<TraderSystem>().RefreshTable((miner, entMan.GetComponent<TraderComponent>(miner)));
                Assert.That(entMan.GetComponent<TraderComponent>(miner).Table, Is.Not.Null, "The Old Wise Miner has no table to play across.");
            });
        }
        finally
        {
            await Teardown(pair, world);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>The first spot on a widening square round the cavern's centre where a room of this radius fits, away from rooms already laid.</summary>
    private static Vector2i? FindSpot(WFCavernEncounterSystem system, EntityUid cavern, int radius, List<Vector2i> used)
    {
        for (var ring = 3; ring <= 20; ring++)
        {
            for (var x = -ring; x <= ring; x++)
            for (var y = -ring; y <= ring; y++)
            {
                if (Math.Max(Math.Abs(x), Math.Abs(y)) != ring)
                    continue;

                var centre = new Vector2i(x * 12, y * 12);
                if (used.Any(u => (u - centre).Length < 20f))
                    continue;

                if (system.Fits(cavern, centre, radius))
                    return centre;
            }
        }

        return null;
    }

    /// <summary>A breathable plating floor from -12 to 12.</summary>
    private static async Task<TestMapData> Rig(TestPair pair)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        await server.WaitPost(() =>
        {
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, air);
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -12; x <= 12; x++)
            for (var y = -12; y <= 12; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
        });
        return map;
    }

    /// <summary>Spesos lying within a range of an entity, or in its hands.</summary>
    private static int CashNear(IEntityManager entMan, EntityUid uid, float range)
    {
        var lookup = entMan.System<EntityLookupSystem>();
        var total = 0;
        foreach (var near in lookup.GetEntitiesInRange(uid, range, LookupFlags.Uncontained | LookupFlags.Contained))
        {
            if (entMan.GetComponent<MetaDataComponent>(near).EntityPrototype?.ID == "SpaceCash"
                && entMan.TryGetComponent<StackComponent>(near, out var stack))
                total += stack.Count;
        }

        return total;
    }

    /// <summary>The prototype ids lying within a range of an entity.</summary>
    private static List<string> NearbyIds(IEntityManager entMan, EntityUid uid, float range)
    {
        return entMan.System<EntityLookupSystem>().GetEntitiesInRange(uid, range)
            .Select(e => entMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID ?? string.Empty).ToList();
    }

    private static Vector2 Middle(Vector2i tile)
    {
        return new Vector2(tile.X + 0.5f, tile.Y + 0.5f);
    }
}
