#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Shuttles.Events;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    [TestPrototypes]
    private const string SimulationPrototypes = @"
- type: wfEncounter
  id: WFTestSimWanderer
  name: wf-encounter-name-convoy
  start: Manual
  ships:
  - key: ship
    vessel: WFDredger
    side: wanderers
    wander: 4
    wanderRadius: 1500

- type: wfEncounter
  id: WFTestSimRaider
  name: wf-encounter-name-pirates
  start: Manual
  ships:
  - key: ship
    vessel: WFDredger
    side: reavers
    faction: PirateNF
    company: WFAshfallReavers
    objectives:
    - kind: Hold

- type: wfEncounter
  id: WFTestSimHauler
  name: wf-encounter-name-convoy
  start: Manual
  ships:
  - key: ship
    vessel: WFDredger
    side: freight
    company: PDV
    objectives:
    - kind: Hold

- type: wfEncounter
  id: WFTestSimPair
  name: wf-encounter-name-pirates
  start: Manual
  ships:
  - key: lead
    vessel: WFDredger
    side: pirates
    objectives:
    - kind: GoTo
      offset: 0, 4000
  - key: wing
    vessel: WFDredger
    side: pirates
    offset: 150, 0
    objectives:
    - kind: Escort
      target: lead
      range: 150

- type: wfEncounter
  id: WFTestSimExtorter
  name: wf-encounter-name-pirates
  start: Manual
  lifetime: Persistent
  bounty:
    ship: ship
    target: Crew
    spesos: 5000
    title: wf-bounty-title-fugitive-wanted
    description: wf-bounty-desc-fugitive-wanted
  ships:
  - key: ship
    vessel: WFDredger
    side: pirates
    faction: PirateNF
    company: WFAshfallReavers
    wander: 2
    wanderRadius: 800
    extort:
      toll: 1000
      reach: 4000
      standoff: 300
      markMin: 2
      markMax: 3
";

    /// <summary>Turns simulation on for a test, with quick spawning and going back, and restores it after.</summary>
    private async Task WithSimulation(Func<Task> body)
    {
        var config = Server.ResolveDependency<IConfigurationManager>();
        await Server.WaitPost(() =>
        {
            config.SetCVar(EncountersCVars.Simulate, true);
            config.SetCVar(EncountersCVars.DematerializeDelay, 1f);
            config.SetCVar(EncountersCVars.MaterializeInterval, 0.1f);
        });
        try
        {
            await body();
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                config.SetCVar(EncountersCVars.Simulate, false);
                config.SetCVar(EncountersCVars.DematerializeDelay, EncountersCVars.DematerializeDelay.DefaultValue);
                config.SetCVar(EncountersCVars.MaterializeInterval, EncountersCVars.MaterializeInterval.DefaultValue);
                config.SetCVar(EncountersCVars.DuelMeanSeconds, EncountersCVars.DuelMeanSeconds.DefaultValue);
            });
        }
    }

    /// <summary>Runs ticks a second at a time until a condition holds on the server, failing past a limit.</summary>
    private async Task Until(Func<bool> condition, int seconds, string what)
    {
        var met = false;
        for (var i = 0; i < seconds && !met; i++)
        {
            await RunTicks(60);
            await Server.WaitPost(() => met = condition());
        }

        Assert.That(met, Is.True, what);
    }

    private WFEncounterShipState Ship(EntityUid encounter) => SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["ship"];

    private void MovePlayer(Vector2 to)
    {
        Server.System<SharedTransformSystem>().SetMapCoordinates(SPlayer, new MapCoordinates(to, MapData.MapId));
    }

    /// <summary>
    /// A ship far from everyone starts simulated and flies its orders as a point; a player coming near spawns its hull,
    /// crewed and still on its orders; once the player has gone, the untouched ship goes back to simulation.
    /// </summary>
    [Test]
    public async Task SimulatedShipsSpawnWhenAPlayerComesNearAndGoBackWhenLeft()
    {
        await WithSimulation(async () =>
        {
            EntityUid encounter = default, proxy = default;
            var start = new Vector2(30000, 30000);
            await Server.WaitAssertion(() =>
            {
                var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestSimWanderer");
                Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(start, MapData.MapId), out encounter), Is.True);
                var ship = Ship(encounter);
                proxy = ship.Grid;
                Assert.That(ship.Virtual, Is.True, "Nobody near: the ship starts simulated.");
                Assert.That(SEntMan.HasComponent<MapGridComponent>(proxy), Is.False, "Its stand-in is no grid.");
                Assert.That(SEntMan.HasComponent<WFCrewVirtualShipComponent>(proxy), Is.True);
                Assert.That(Server.System<WFCrewObjectiveSystem>().QueueStatus(proxy, ship.Group), Is.Not.Null, "Its orders are on the stand-in.");
            });

            await Until(() => (Server.System<SharedTransformSystem>().GetWorldPosition(proxy) - start).Length() > 50f, 30,
                "The simulated ship flies its wander legs.");

            await Server.WaitPost(() => MovePlayer(Server.System<SharedTransformSystem>().GetWorldPosition(proxy) + new Vector2(0f, 1500f)));
            await Until(() => !Ship(encounter).Virtual, 30, "A player within range spawns the hull.");
            await Server.WaitAssertion(() =>
            {
                var ship = Ship(encounter);
                Assert.That(SEntMan.HasComponent<MapGridComponent>(ship.Grid), Is.True);
                Assert.That(SEntMan.EntityExists(proxy), Is.False, "The stand-in is gone.");
                Assert.That(Server.System<WFEncounterSystem>().HasLivingCrew(ship, anywhere: true), Is.True, "The hull is crewed.");
                Assert.That(Server.System<WFCrewObjectiveSystem>().QueueStatus(ship.Grid, ship.Group), Is.Not.Null, "Its orders came with it.");
                Assert.That(SEntMan.GetComponent<WFEncounterComponent>(encounter).Resolution, Is.Null, "The encounter runs on.");
            });

            EntityUid hull = default;
            await Server.WaitPost(() =>
            {
                hull = Ship(encounter).Grid;
                MovePlayer(Vector2.Zero);
            });
            await Until(() => Ship(encounter).Virtual, 30, "Left alone, the untouched ship goes back to simulation.");
            await Server.WaitAssertion(() =>
            {
                Assert.That(SEntMan.EntityExists(hull), Is.False, "Its hull is gone.");
                Assert.That(Server.System<WFEncounterSystem>().InFight(Ship(encounter)), Is.True, "And it is still in the encounter.");
                Server.System<WFEncounterSystem>().End(encounter);
            });
        });
    }

    /// <summary>A jump plotted to a point near a simulated ship spawns its hull while the jumper is in transit.</summary>
    [Test]
    public async Task AJumpPlottedNearASimulatedShipSpawnsIt()
    {
        await WithSimulation(async () =>
        {
            EntityUid encounter = default;
            var at = new Vector2(-30000, 30000);
            await Server.WaitAssertion(() =>
            {
                var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestSimHauler");
                Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(at, MapData.MapId), out encounter), Is.True);
                Assert.That(Ship(encounter).Virtual, Is.True);
                var ev = new FTLStartedEvent(MapData.Grid, new EntityCoordinates(MapData.MapUid, at + new Vector2(500f, 0f)), MapData.MapUid,
                    Matrix3x2.Identity, Angle.Zero);
                SEntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
            });

            await Until(() => !Ship(encounter).Virtual, 30, "The ship near the jump's exit is spawned.");
            await Server.WaitPost(() => Server.System<WFEncounterSystem>().End(encounter));
        });
    }

    /// <summary>Two simulated ships ordered to attack each other fight it out by a roll, nobody watching: one is lost and the other's side has it.</summary>
    [Test]
    public async Task UnwatchedSimulatedShipsFightItOutByARoll()
    {
        await WithSimulation(async () =>
        {
            EntityUid raider = default, hauler = default;
            await Server.WaitAssertion(() =>
            {
                Server.ResolveDependency<IConfigurationManager>().SetCVar(EncountersCVars.DuelMeanSeconds, 1f);
                var prototypes = Server.ResolveDependency<IPrototypeManager>();
                var encounters = Server.System<WFEncounterSystem>();
                Assert.That(encounters.TrySpawn(prototypes.Index<WFEncounterPrototype>("WFTestSimRaider"),
                    new MapCoordinates(new Vector2(30000, -30000), MapData.MapId), out raider), Is.True);
                Assert.That(encounters.TrySpawn(prototypes.Index<WFEncounterPrototype>("WFTestSimHauler"),
                    new MapCoordinates(new Vector2(30800, -30000), MapData.MapId), out hauler), Is.True);
                var attack = new List<WFCrewObjective>
                {
                    new() { Kind = WFCrewObjectiveKind.Attack, Target = SEntMan.GetNetEntity(Ship(hauler).Grid), Range = 600f },
                };
                Assert.That(encounters.SetOrders(raider, "ship", attack), Is.True);
            });

            await Until(() => SEntMan.Deleted(Ship(raider).Grid) || SEntMan.Deleted(Ship(hauler).Grid), 30,
                "One of the two is lost in the simulated fight.");
            await Until(() => SEntMan.Deleted(raider) || SEntMan.GetComponent<WFEncounterComponent>(raider).Resolution != null
                              || SEntMan.Deleted(hauler) || SEntMan.GetComponent<WFEncounterComponent>(hauler).Resolution != null, 15,
                "The beaten ship's encounter is over.");
            await Server.WaitPost(() =>
            {
                foreach (var uid in new[] { raider, hauler })
                {
                    if (!SEntMan.Deleted(uid))
                        Server.System<WFEncounterSystem>().End(uid);
                }
            });
        });
    }

    /// <summary>
    /// A simulated ship sent to dock with another counts as docked once it reaches it, and keeps counting as docked
    /// when both are spawned, while it comes alongside for real.
    /// </summary>
    [Test]
    public async Task SimulatedDockingCarriesOverWhenTheShipsSpawn()
    {
        await WithSimulation(async () =>
        {
            EntityUid host = default, visitor = default;
            var at = new Vector2(-30000, -30000);
            await Server.WaitAssertion(() =>
            {
                var prototypes = Server.ResolveDependency<IPrototypeManager>();
                var encounters = Server.System<WFEncounterSystem>();
                Assert.That(encounters.TrySpawn(prototypes.Index<WFEncounterPrototype>("WFTestSimHauler"),
                    new MapCoordinates(at, MapData.MapId), out host), Is.True);
                Assert.That(encounters.TrySpawn(prototypes.Index<WFEncounterPrototype>("WFTestSimHauler"),
                    new MapCoordinates(at + new Vector2(400f, 0f), MapData.MapId), out visitor), Is.True);
                var dock = new List<WFCrewObjective>
                {
                    new() { Kind = WFCrewObjectiveKind.Dock, Target = SEntMan.GetNetEntity(Ship(host).Grid) },
                    new() { Kind = WFCrewObjectiveKind.Hold },
                };
                Assert.That(encounters.SetOrders(visitor, "ship", dock), Is.True);
            });

            await Until(() => Server.System<WFEncounterSystem>().AreDocked(Ship(visitor), Ship(host)), 40,
                "The simulated visitor reaches the host and counts as docked.");

            await Server.WaitPost(() => MovePlayer(at + new Vector2(0f, 1500f)));
            await Until(() => !Ship(host).Virtual && !Ship(visitor).Virtual, 30, "Both hulls are spawned.");
            await Server.WaitAssertion(() =>
            {
                var encounters = Server.System<WFEncounterSystem>();
                Assert.That(encounters.AreDocked(Ship(visitor), Ship(host)), Is.True, "Still docked while it comes alongside.");
                Assert.That(Ship(visitor).RedockTo, Is.EqualTo(Ship(host).Grid), "It is docking with the host's hull.");
                var bounds = Server.System<EntityLookupSystem>();
                Assert.That(bounds.GetWorldAABB(Ship(visitor).Grid).Intersects(bounds.GetWorldAABB(Ship(host).Grid)), Is.False,
                    "The visitor's hull is spawned clear of the host's.");
                encounters.End(host);
                encounters.End(visitor);
                MovePlayer(Vector2.Zero);
            });
        });
    }

    /// <summary>
    /// A simulated extorter roams and picks a player ship off the map as its mark, closes on it as a point, and is
    /// spawned on the way, its crew then marked on its warrant.
    /// </summary>
    [Test]
    public async Task SimulatedExtortersRoamPickAMarkAndSpawnOnTheWay()
    {
        await WithSimulation(async () =>
        {
            EntityUid encounter = default, grid = default;
            EntityUid? board = null;
            var config = Server.ResolveDependency<IConfigurationManager>();
            await Server.WaitAssertion(() =>
            {
                config.SetCVar(EncountersCVars.MaterializeRange, 700f);
                board = EnsureBoard();
                grid = PutPlayerOnGrid(new Vector2(20000, 20000));
                encounter = SpawnBountyEncounter("WFTestSimExtorter", new Vector2(23000, 20000));
                Assert.That(Ship(encounter).Virtual, Is.True, "Far from the player, the extorter starts simulated.");
                Assert.That(SEntMan.HasComponent<WFEncounterBountyComponent>(encounter), Is.True, "Its crew warrant stands while it is simulated.");
            });

            await Until(() => Ship(encounter).Extort is { Phase: WFExtortPhase.Close } extort && extort.Mark == grid, 20,
                "Simulated, it picks the player's ship as its mark.");
            EntityUid? markWhenSpawned = null;
            var spawnedAt = 0f;
            await Until(() =>
            {
                var ship = Ship(encounter);
                if (!ship.Virtual)
                    return true;

                markWhenSpawned = ship.Extort!.Mark;
                spawnedAt = (Server.System<SharedTransformSystem>().GetWorldPosition(ship.Grid)
                             - Server.System<SharedTransformSystem>().GetWorldPosition(grid)).Length();
                return false;
            }, 120, "It closes on its mark and is spawned on the way.");
            await Server.WaitAssertion(() =>
            {
                // Spawned, it hails at once; the test grid has no console to answer, so what it does next is not this test's.
                Assert.That(markWhenSpawned, Is.EqualTo(grid), "It held its mark all the way in.");
                Assert.That(spawnedAt, Is.GreaterThan(300f), "It is spawned before it reaches its standoff.");
                Assert.That(SEntMan.GetComponent<WFEncounterBountyComponent>(encounter).Crew, Is.Not.Empty, "Its new crew are on the warrant.");
                Server.System<WFEncounterSystem>().End(encounter);
                SendPlayerHome(grid);
                if (board is { } spawned)
                    SEntMan.DeleteEntity(spawned);
                config.SetCVar(EncountersCVars.MaterializeRange, EncountersCVars.MaterializeRange.DefaultValue);
            });
        });
    }

    /// <summary>A real ship another ship has shot at stays real when the players leave it: it would come back whole.</summary>
    [Test]
    public async Task AShipThatWasShotStaysReal()
    {
        await WithSimulation(async () =>
        {
            EntityUid encounter = default;
            var at = new Vector2(30000, -20000);
            await Server.WaitPost(() =>
            {
                MovePlayer(at + new Vector2(0f, 1500f));
                var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestSimHauler");
                Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(at, MapData.MapId), out encounter), Is.True);
            });
            await Until(() => !Ship(encounter).Virtual, 30, "The hauler near the player is spawned.");
            await Server.WaitPost(() =>
            {
                var hit = new WFCrewHullHitEvent(Ship(encounter).Grid, MapData.Grid);
                SEntMan.EventBus.RaiseEvent(EventSource.Local, ref hit);
                MovePlayer(Vector2.Zero);
            });
            await RunTicks(60 * 12);
            await Server.WaitAssertion(() =>
            {
                Assert.That(Ship(encounter).Virtual, Is.False, "Shot, it stays real with nobody near.");
                Assert.That(Ship(encounter).Touched, Is.True);
                Server.System<WFEncounterSystem>().End(encounter);
            });
        });
    }

    /// <summary>
    /// A simulated wing escorting its lead keeps pace with it: it stays at its range, and the velocity it reports for the
    /// markers is the lead's, so the map shows the pair moving together and their zone ring holds steady.
    /// </summary>
    [Test]
    public async Task ASimulatedEscortKeepsPaceWithItsLead()
    {
        await WithSimulation(async () =>
        {
            EntityUid encounter = default;
            await Server.WaitAssertion(() =>
            {
                var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestSimPair");
                Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-30000, 0), MapData.MapId), out encounter), Is.True);
            });

            // Settled in behind its lead first.
            await RunTicks(60 * 10);
            var worst = 0f;
            var drift = 0f;
            for (var i = 0; i < 30; i++)
            {
                await RunTicks(30);
                await Server.WaitPost(() =>
                {
                    var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
                    var lead = comp.Ships["lead"].Grid;
                    var wing = comp.Ships["wing"].Grid;
                    var transform = Server.System<SharedTransformSystem>();
                    worst = MathF.Max(worst, (transform.GetWorldPosition(lead) - transform.GetWorldPosition(wing)).Length());
                    drift = MathF.Max(drift, (SEntMan.GetComponent<WFCrewVirtualShipComponent>(lead).Velocity
                                              - SEntMan.GetComponent<WFCrewVirtualShipComponent>(wing).Velocity).Length());
                });
            }

            Assert.That(worst, Is.LessThan(200f), "The wing stays at its range from the lead.");
            Assert.That(drift, Is.LessThan(2f), "It reports the lead's velocity, not a stop-start sprint.");
            await Server.WaitPost(() => Server.System<WFEncounterSystem>().End(encounter));
        });
    }

    /// <summary>
    /// A real ship whose queue is held when it goes back to simulation flies on as a simulated ship; one stopped for a
    /// lost target keeps saying so, for the encounter to skip the order.
    /// </summary>
    [Test]
    public async Task AHeldShipFliesOnOnceSimulated()
    {
        await WithSimulation(async () =>
        {
            EntityUid encounter = default;
            var at = new Vector2(-30000, 20000);
            await Server.WaitPost(() =>
            {
                MovePlayer(at + new Vector2(0f, 1500f));
                var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestSimWanderer");
                Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(at, MapData.MapId), out encounter), Is.True);
            });
            await Until(() => !Ship(encounter).Virtual && !Ship(encounter).Building, 30, "The wanderer near the player is spawned.");

            Vector2 heldAt = default;
            await Server.WaitPost(() =>
            {
                var ship = Ship(encounter);
                // Held, as for visitors or a hail.
                Server.System<WFCrewObjectiveSystem>().Control(ship.Grid, ship.Group, WFCrewSetupAction.Pause);
                MovePlayer(Vector2.Zero);
            });
            await Until(() => Ship(encounter).Virtual, 30, "Left alone, it goes back to simulation.");
            await Server.WaitPost(() => heldAt = Server.System<SharedTransformSystem>().GetWorldPosition(Ship(encounter).Grid));
            await Until(() => (Server.System<SharedTransformSystem>().GetWorldPosition(Ship(encounter).Grid) - heldAt).Length() > 50f, 20,
                "Simulated, it flies on with nobody to hold for.");
            await Server.WaitPost(() => Server.System<WFEncounterSystem>().End(encounter));
        });
    }

    /// <summary>A simulated ship ordered to circle a station flies round it at its range, as a real patrol does, and does not park.</summary>
    [Test]
    public async Task ASimulatedPatrolCirclesItsStation()
    {
        await WithSimulation(async () =>
        {
            EntityUid encounter = default, station = default;
            var centre = new Vector2(30000, 25000);
            await Server.WaitPost(() =>
            {
                var maps = Server.System<SharedMapSystem>();
                var grid = maps.CreateGridEntity(MapData.MapId);
                maps.SetTile(grid, Vector2i.Zero, new Tile(1));
                Server.System<SharedTransformSystem>().SetCoordinates(grid.Owner, new EntityCoordinates(MapData.MapUid, centre));
                station = grid.Owner;
                var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestSimHauler");
                var encounters = Server.System<WFEncounterSystem>();
                Assert.That(encounters.TrySpawn(prototype, new MapCoordinates(centre + new Vector2(1500f, 0f), MapData.MapId), out encounter), Is.True);
                Assert.That(encounters.SetOrders(encounter, "ship", new List<WFCrewObjective>
                {
                    new() { Kind = WFCrewObjectiveKind.Circle, Target = SEntMan.GetNetEntity(station), Range = 700f },
                }), Is.True);
            });

            // Out to its circle first.
            await RunTicks(60 * 40);
            var angles = new List<float>();
            Vector2? last = null;
            for (var i = 0; i < 10; i++)
            {
                await RunTicks(60 * 3);
                await Server.WaitPost(() =>
                {
                    var at = Server.System<SharedTransformSystem>().GetWorldPosition(Ship(encounter).Grid);
                    var offset = at - Server.System<SharedTransformSystem>().GetWorldPosition(station);
                    Assert.That(offset.Length(), Is.InRange(600f, 800f), "It keeps to its range.");
                    var speed = SEntMan.GetComponent<WFCrewVirtualShipComponent>(Ship(encounter).Grid).Speed;
                    if (last is { } before)
                        Assert.That((at - before).Length(), Is.LessThanOrEqualTo(speed * 180f / STiming.TickRate * 1.1f), "No faster than its cruise speed.");
                    last = at;
                    angles.Add(MathF.Atan2(offset.Y, offset.X));
                });
            }

            var swept = 0f;
            for (var i = 1; i < angles.Count; i++)
            {
                var step = angles[i] - angles[i - 1];
                swept += MathF.Abs(MathF.IEEERemainder(step, MathF.Tau));
            }

            Assert.That(swept, Is.GreaterThan(0.5f), "It goes round the station rather than parking off it.");
            await Server.WaitPost(() =>
            {
                Server.System<WFEncounterSystem>().End(encounter);
                SEntMan.DeleteEntity(station);
            });
        });
    }

    /// <summary>An encounter ended while one of its ships is being built leaves neither the half-built hull nor the stand-in behind.</summary>
    [Test]
    public async Task AnEncounterEndedMidBuildLeavesNothingBehind()
    {
        await WithSimulation(async () =>
        {
            EntityUid encounter = default, proxy = default, hull = default;
            var at = new Vector2(30000, -25000);
            await Server.WaitPost(() =>
            {
                var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestSimHauler");
                Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(at, MapData.MapId), out encounter), Is.True);
                proxy = Ship(encounter).Grid;
                MovePlayer(at + new Vector2(0f, 1500f));
            });

            // A tick at a time until its hull has taken over but its crew and orders are not yet aboard.
            var caught = false;
            for (var i = 0; i < 60 * 30 && !caught; i++)
            {
                await RunTicks(1);
                await Server.WaitPost(() =>
                {
                    var ship = Ship(encounter);
                    if (!ship.Building || ship.Virtual)
                        return;

                    hull = ship.Grid;
                    caught = true;
                    Server.System<WFEncounterSystem>().End(encounter);
                });
            }

            Assert.That(caught, Is.True, "The ship was caught mid-build.");
            await RunTicks(30);
            await Server.WaitAssertion(() =>
            {
                Assert.That(SEntMan.EntityExists(hull), Is.False, "The half-built hull is gone.");
                Assert.That(SEntMan.EntityExists(proxy), Is.False, "And so is the stand-in.");
                MovePlayer(Vector2.Zero);
            });
        });
    }
}
