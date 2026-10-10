#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Client._WF.Encounters;
using Content.Shared._WF.CCVar;
using Robust.Shared.Configuration;
using Content.Shared._Mono.CCVar;
using Content.Server.Gravity;
using Robust.Shared.Map.Components;
using System;
using System.Collections.Generic;
using Robust.Shared.Maths;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

/// <summary>
/// Encounters: a prototype's ships spawn crewed, in their sides, with their rules and orders; an encounter resolves
/// when no ship is left and ending one removes its ships and crews.
/// </summary>
[TestOf(typeof(WFEncounterSystem))]
public sealed partial class WFEncounterTest : InteractionTest
{
    private const string Convoy = "WFTestEncounterConvoy";

    // Spawned ships play sounds, and the client's room echo asserts on audio whose parent changes mid-setup.
    [SetUp]
    public async Task DisableRoomEcho()
    {
        await Client.WaitPost(() => Client.ResolveDependency<IConfigurationManager>().SetCVar(MonoCVars.AreaEchoEnabled, false));
    }

    [TearDown]
    public async Task RestoreRoomEcho()
    {
        await Client.WaitPost(() => Client.ResolveDependency<IConfigurationManager>().SetCVar(MonoCVars.AreaEchoEnabled, MonoCVars.AreaEchoEnabled.DefaultValue));
    }

    [TestPrototypes]
    private const string Prototypes = @"
- type: wfEncounter
  id: WFTestEncounterConvoy
  name: wf-encounter-name-convoy
  start: Manual
  ships:
  - key: lead
    vessel: WFDredger
    side: convoy
    disengage: Deter
    disengageRange: 800
    skill: Green
  - key: escort
    vessel: WFDredger
    offset: 200, 0
    side: convoy
    captain: false
    objectives:
    - kind: Escort
      target: lead

- type: wfEncounter
  id: WFTestEncounterCheap
  name: wf-encounter-name-convoy
  category: Threat
  cost: 1
  hidden: true
  announcement: wf-encounter-announce-ambush
  minDistance: 700
  maxDistance: 900
  ships:
  - key: lone
    vessel: WFDredger
    objectives:
    - kind: Hold
      duration: 5

- type: wfEncounter
  id: WFTestEncounterZoned
  name: wf-encounter-name-convoy
  start: Manual
  ships:
  - key: guarded
    vessel: WFDredger
    warnRange: 500
    attackRange: 150

- type: wfEncounter
  id: WFTestEncounterHunter
  name: wf-encounter-name-convoy
  start: Manual
  ships:
  - key: hunter
    vessel: WFDredger
    captain: false
    hunt: true

- type: wfEncounter
  id: WFTestEncounterWaiting
  name: wf-encounter-name-convoy
  start: Manual
  startRadius: 300
  ships:
  - key: lone
    vessel: WFDredger
    objectives:
    - kind: Hold
      duration: 600

- type: wfEncounter
  id: WFTestEncounterSkirmish
  name: wf-encounter-name-convoy
  start: Manual
  rewards:
  - side: good
    spesos: 9000
  ships:
  - key: friend
    vessel: WFDredger
    side: good
  - key: foe
    vessel: WFDredger
    offset: 400, 0
    side: bad

- type: wfEncounter
  id: WFTestEncounterWarZone
  name: wf-encounter-name-convoy
  start: Manual
  ships:
  - key: patrol
    vessel: WFDredger
    company: TSF
    attackRange: 400
    zoneTargets: AtWar

- type: wfEncounter
  id: WFTestEncounterCostly
  name: wf-encounter-name-convoy
  cost: 50
  minDistance: 700
  maxDistance: 900
  ships:
  - key: lone
    vessel: WFDredger
";

    [Test]
    public async Task EncounterSpawnsCrewedShipsAndEndsCleanly()
    {
        EntityUid encounter = default;
        await Server.WaitAssertion(() =>
        {
            var system = Server.System<WFEncounterSystem>();
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(Convoy);
            Assert.That(system.TrySpawn(prototype, new MapCoordinates(new Vector2(600, 600), MapData.MapId), out encounter), Is.True);
            Assert.That(system.ActiveCount(), Is.EqualTo(1));
        });
        await RunTicks(5);

        EntityUid lead = default, escort = default;
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            lead = comp.Ships["lead"].Grid;
            escort = comp.Ships["escort"].Grid;
            Assert.That(SEntMan.GetComponent<WFEncounterGridComponent>(lead).Encounter, Is.EqualTo(encounter));

            var crews = Server.System<WFCrewObjectiveSystem>().Snapshot().ToDictionary(crew => SEntMan.GetEntity(crew.Grid));
            Assert.That(crews[lead].Alive, Is.GreaterThanOrEqualTo(2));
            Assert.That(crews[lead].Settings.Disengage, Is.EqualTo(WFCrewDisengage.Deter));
            Assert.That(crews[lead].Settings.DisengageRange, Is.EqualTo(800f));
            Assert.That(crews[lead].Settings.Skill, Is.EqualTo(WFCrewSkill.Green));
            Assert.That(crews[escort].Settings.Skill, Is.EqualTo(WFCrewSkill.Veteran));
            Assert.That(crews[escort].Objectives.Single().Kind, Is.EqualTo(WFCrewObjectiveKind.Escort));
            Assert.That(crews[escort].Objectives.Single().Target, Is.EqualTo(SEntMan.GetNetEntity(lead)));
            Assert.That(crews[lead].Settings.Battlegroup, Is.Not.Empty);
            Assert.That(crews[lead].Settings.Battlegroup, Is.EqualTo(crews[escort].Settings.Battlegroup));
            Assert.That(Server.System<WFCrewEscortSystem>().AreInFormation(lead, escort), Is.True);

            var system = Server.System<WFEncounterSystem>();
            system.Resolve(encounter, WFEncounterResolution.Expired);
            Assert.That(comp.Resolution, Is.EqualTo(WFEncounterResolution.Expired));
            Assert.That(system.ActiveCount(), Is.Zero, "A resolved encounter no longer counts against the cap.");
            Assert.That(SEntMan.Deleted(lead), Is.False, "Resolved ships stay until players have left them.");
            system.End(encounter);
        });
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(lead) && SEntMan.Deleted(escort) && SEntMan.Deleted(encounter), Is.True);
            Assert.That(SEntMan.EntityQuery<WFCrewComponent>().Any(), Is.False, "Ending an encounter removes its crews.");
        });
    }

    /// <summary>The admin window's requests start, list and end encounters and set the scheduler; the window builds.</summary>
    [Test]
    public async Task EncounterAdminControlsEncountersAndScheduler()
    {
        await Server.WaitAssertion(() =>
        {
            var admin = Server.System<WFEncounterAdminSystem>();
            var config = Server.ResolveDependency<IConfigurationManager>();
            var cap = config.GetCVar(EncountersCVars.MaxActive);

            Assert.That(admin.Handle(new WFEncounterAdminRequest
            {
                Action = WFEncounterAdminAction.Scheduler, IntervalMin = 5, IntervalMax = 60, MaxActive = 3,
            }, ServerSession), Is.Not.Null, "An interval under 30 seconds is refused.");
            Assert.That(config.GetCVar(EncountersCVars.MaxActive), Is.EqualTo(cap));
            Assert.That(admin.Handle(new WFEncounterAdminRequest
            {
                Action = WFEncounterAdminAction.Scheduler, IntervalMin = 60, IntervalMax = 120, MaxActive = 3, Paused = true,
            }, ServerSession), Is.Null);
            var state = admin.BuildState();
            Assert.That(state.MaxActive, Is.EqualTo(3));
            Assert.That(state.Paused, Is.True);
            Assert.That(state.Enabled, Is.False);
            Assert.That(state.NextIn, Is.LessThan(0f), "A disabled scheduler has no next attempt.");

            Assert.That(admin.Handle(new WFEncounterAdminRequest { Action = WFEncounterAdminAction.Spawn, Prototype = "Nope" }, ServerSession),
                Is.Not.Null);
            Assert.That(admin.Handle(new WFEncounterAdminRequest
            {
                Action = WFEncounterAdminAction.Spawn, Prototype = Convoy, Distance = 800,
            }, ServerSession), Is.Null);
            state = admin.BuildState();
            Assert.That(state.Encounters.Single().Ships, Has.Count.EqualTo(2));
            Assert.That(state.Encounters.Single().Ships.All(ship => ship.Exists && ship.Crew > 0), Is.True);
            Assert.That(state.Prototypes.Single(prototype => prototype.Id == Convoy).Running, Is.True);
            Assert.That(Server.System<WFEncounterSystem>().IsRunning(Convoy), Is.True);

            Assert.That(admin.Handle(new WFEncounterAdminRequest
            {
                Action = WFEncounterAdminAction.End, Target = state.Encounters.Single().Uid,
            }, ServerSession), Is.Null);
            Assert.That(Server.System<WFEncounterSystem>().IsRunning(Convoy), Is.False);

            config.SetCVar(EncountersCVars.MaxActive, cap);
            config.SetCVar(EncountersCVars.IntervalMin, EncountersCVars.IntervalMin.DefaultValue);
            config.SetCVar(EncountersCVars.IntervalMax, EncountersCVars.IntervalMax.DefaultValue);
            Server.System<WFEncounterSchedulerSystem>().Paused = false;
        });
        await RunTicks(10);
        await Server.WaitAssertion(() => Assert.That(Server.System<WFEncounterAdminSystem>().BuildState().Encounters, Is.Empty));

        await Client.WaitAssertion(() =>
        {
            var window = new WFEncounterWindow();
            window.OpenCentered();
            window.Close();
        });
    }

    /// <summary>Every encounter prototype spawns all its ships crewed and with valid orders, and ends cleanly.</summary>
    [Test]
    public async Task EveryEncounterPrototypeSpawnsAndEnds()
    {
        EntityUid from = default, to = default;
        await Server.WaitPost(() =>
        {
            // Stand-ins for the stations that Station and Route placements hand to the orders.
            var maps = Server.ResolveDependency<IMapManager>();
            from = maps.CreateGridEntity(MapData.MapId).Owner;
            to = maps.CreateGridEntity(MapData.MapId).Owner;
            var transform = Server.System<SharedTransformSystem>();
            transform.SetCoordinates(from, new EntityCoordinates(MapData.MapUid, new Vector2(3000, 0)));
            transform.SetCoordinates(to, new EntityCoordinates(MapData.MapUid, new Vector2(3000, 3000)));
        });
        var ids = Server.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<WFEncounterPrototype>()
            .Where(prototype => !prototype.ID.StartsWith("WFTest")).Select(prototype => prototype.ID).ToList();
        Assert.That(ids, Is.Not.Empty);
        var x = 3500f;
        foreach (var id in ids)
        {
            EntityUid encounter = default;
            var origin = new MapCoordinates(new Vector2(x, 1500), MapData.MapId);
            x += 1500f;
            await Server.WaitAssertion(() =>
            {
                var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(id);
                Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, origin, out encounter, null, new[] { from, to }), Is.True, id);
                var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
                Assert.That(comp.Ships, Has.Count.EqualTo(prototype.Ships.Count), id);
                var crews = Server.System<WFCrewObjectiveSystem>().Snapshot();
                foreach (var ship in comp.Ships.Values)
                {
                    var hidden = SEntMan.TryGetComponent<Content.Shared.Shuttles.Components.IFFComponent>(ship.Grid, out var iff)
                                 && (iff.Flags & Content.Shared.Shuttles.Components.IFFFlags.HideLabel) != 0;
                    Assert.That(hidden, Is.EqualTo(ship.Lurking), $"{id}: only a ship lying in wait flies with its IFF off");

                    // A hulk has squatters, not a crew.
                    if (ship.Derelict)
                        continue;

                    Assert.That(crews.Any(crew => crew.Group == ship.Group && crew.Alive > 0), Is.True, $"{id}: {ship.Group} has no crew");
                }
            });
            await RunTicks(5);
            await Server.WaitPost(() => Server.System<WFEncounterSystem>().End(encounter));
            await RunTicks(5);
        }

        await Server.WaitPost(() =>
        {
            SEntMan.DeleteEntity(from);
            SEntMan.DeleteEntity(to);
        });
    }

    /// <summary>The storyteller keeps to its budget and to one of a kind; a hidden encounter announces itself when revealed.</summary>
    [Test]
    public async Task StorytellerKeepsToBudgetAndRevealsHiddenEncounters()
    {
        EntityUid encounter = default;
        await Server.WaitAssertion(() =>
        {
            var config = Server.ResolveDependency<IConfigurationManager>();
            var scheduler = Server.System<WFEncounterSchedulerSystem>();
            var system = Server.System<WFEncounterSystem>();
            config.SetCVar(EncountersCVars.Preset, "WFEncounterPresetQuiet");
            Assert.That(scheduler.Preset, Is.Not.Null);
            Assert.That(scheduler.Budget(), Is.GreaterThanOrEqualTo(2).And.LessThan(50));
            Assert.That(scheduler.StartRound(), Is.Zero, "No stations on a test map, so nothing can be placed beside one.");

            // Whatever it picks fits the budget; the costly test encounter never does.
            Assert.That(scheduler.TrySchedule(out var scheduled), Is.True);
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(scheduled).Prototype.Id, Is.Not.EqualTo("WFTestEncounterCostly"));
            scheduler.TrySchedule(out _);
            scheduler.TrySchedule(out _);
            Assert.That(system.ActiveCost(), Is.GreaterThan(0).And.LessThanOrEqualTo(scheduler.Budget()));
            foreach (var running in SEntMan.EntityQuery<WFEncounterComponent>().ToList())
            {
                Assert.That(SEntMan.EntityQuery<WFEncounterComponent>().Count(other => other.Resolution == null && other.Prototype == running.Prototype),
                    Is.EqualTo(1), "One of a kind at a time.");
            }

            foreach (var uid in SEntMan.EntityQuery<WFEncounterComponent>().Select(running => running.Owner).ToList())
            {
                system.End(uid);
            }

            var cheap = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestEncounterCheap");
            Assert.That(system.TrySpawn(cheap, new MapCoordinates(new Vector2(13000, 13000), MapData.MapId), out encounter), Is.True);
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            Assert.That(comp.Hidden, Is.True);
            Assert.That(comp.Announcement, Is.Not.Null, "A hidden encounter keeps its announcement back.");
            system.Reveal(encounter);
            Assert.That(comp.Hidden, Is.False);
            Assert.That(comp.Announcement, Is.Null);

            system.Resolve(encounter, WFEncounterResolution.Completed);
            Assert.That(comp.JumpAt, Is.Not.Null, "A transient encounter jumps out once its orders are flown.");
            system.End(encounter);
            config.SetCVar(EncountersCVars.Preset, EncountersCVars.Preset.DefaultValue);
        });
        await RunTicks(10);
    }

    /// <summary>A player ship is warned inside the warning zone and counts as an attacker inside the attack zone.</summary>
    [Test]
    public async Task ZonesWarnThenTreatIntrudersAsAttackers()
    {
        EntityUid encounter = default, ship = default, intruder = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestEncounterZoned");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(5000, 5000), MapData.MapId), out encounter), Is.True);
            ship = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["guarded"].Grid;

            // The player's ship: a bare grid with the test player standing on it, 300 m off.
            var grid = Server.ResolveDependency<IMapManager>().CreateGridEntity(MapData.MapId);
            intruder = grid.Owner;
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            var transform = Server.System<SharedTransformSystem>();
            transform.SetCoordinates(intruder, new EntityCoordinates(MapData.MapUid, new Vector2(5300, 5000)));
            transform.SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(intruder, new Vector2(0.5f)));
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["guarded"];
            Assert.That(state.Warned.ContainsKey(intruder), Is.True, "Inside the warning zone the intruder is warned.");
            Assert.That(state.Engaged, Is.Empty);
            Assert.That(Server.System<WFCrewAlertSystem>().GetHostileShips(ship, state.Group), Is.Empty);
            Server.System<SharedTransformSystem>().SetCoordinates(intruder, new EntityCoordinates(MapData.MapUid, new Vector2(5100, 5000)));
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["guarded"];
            Assert.That(state.Engaged.Contains(intruder), Is.True);
            Assert.That(Server.System<WFCrewAlertSystem>().GetHostileShips(ship, state.Group), Does.Contain(intruder),
                "Inside the attack zone the intruder is an attacker.");
            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WFEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(intruder);
        });
        await RunTicks(10);
    }

    /// <summary>Every body and loadout of every crew profile makes a living, dressed crewman with his role's kit.</summary>
    [Test]
    public async Task EveryCrewProfileBodyAndLoadoutSpawns()
    {
        var spawned = new List<EntityUid>();
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            var crew = Server.System<WFCrewSystem>();
            var grid = Server.ResolveDependency<IMapManager>().CreateGridEntity(MapData.MapId);
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            var post = new EntityCoordinates(grid.Owner, new Vector2(0.5f));
            foreach (var profile in prototypes.EnumeratePrototypes<WFCrewProfilePrototype>())
            {
                Assert.That(profile.Bodies, Is.Not.Empty, profile.ID);
                foreach (var body in profile.Bodies.Distinct())
                {
                    var hand = crew.SpawnCrewman(WFCrewRoles.Deckhand, post, "bodies", profile.Loadouts[WFCrewRoles.Deckhand][0], body);
                    Assert.That(hand, Is.Not.Null, $"{profile.ID}: {body}");
                    SEntMan.GetComponent<Content.Server.NPC.HTN.HTNComponent>(hand!.Value).Enabled = false;
                    Assert.That(SEntMan.HasComponent<WFCrewRepairComponent>(hand.Value), Is.True, $"{body} got no deckhand kit");
                    spawned.Add(hand.Value);
                }

                foreach (var (role, loadouts) in profile.Loadouts)
                {
                    foreach (var loadout in loadouts.Distinct())
                    {
                        var member = crew.SpawnCrewman(role, post, "bodies", loadout, profile.Bodies[0]);
                        Assert.That(member, Is.Not.Null, $"{profile.ID}: {role} in {loadout}");
                        SEntMan.GetComponent<Content.Server.NPC.HTN.HTNComponent>(member!.Value).Enabled = false;
                        Assert.That(Server.System<Content.Shared.Inventory.InventorySystem>().TryGetSlotEntity(member.Value, "jumpsuit", out _),
                            Is.True, $"{loadout} left {role} undressed");
                        spawned.Add(member.Value);
                    }
                }
            }
        });
        await RunTicks(30);
        await Server.WaitAssertion(() =>
        {
            foreach (var uid in spawned)
            {
                Assert.That(Server.System<Content.Shared.Mobs.Systems.MobStateSystem>().IsAlive(uid), Is.True, SEntMan.ToPrettyString(uid).ToString());
                SEntMan.DeleteEntity(uid);
            }
        });
    }

    /// <summary>A hunting ship is ordered to dock with and loot the nearest player ship, then leave.</summary>
    [Test]
    public async Task HunterRaidsNearestPlayerShip()
    {
        EntityUid encounter = default, hunter = default, prey = default;
        await Server.WaitAssertion(() =>
        {
            var grid = Server.ResolveDependency<IMapManager>().CreateGridEntity(MapData.MapId);
            prey = grid.Owner;
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 5; x++)
            {
                for (var y = 0; y < 5; y++)
                {
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
                }
            }

            Server.System<SharedMapSystem>().SetTiles(grid, tiles);
            var transform = Server.System<SharedTransformSystem>();
            transform.SetCoordinates(prey, new EntityCoordinates(MapData.MapUid, new Vector2(8400, 8000)));
            transform.SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(prey, new Vector2(2.5f)));

            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestEncounterHunter");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(8000, 8000), MapData.MapId), out encounter), Is.True);
            hunter = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["hunter"].Grid;
        });
        await WaitUntilServer(() => SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["hunter"].Prey == prey, 900);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["hunter"];
            var crews = Server.System<WFCrewObjectiveSystem>().Snapshot();
            var orders = crews.Single(crew => crew.Group == state.Group).Objectives;
            Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[]
            {
                WFCrewObjectiveKind.Loot, WFCrewObjectiveKind.Hold, WFCrewObjectiveKind.Undock, WFCrewObjectiveKind.GoTo,
            }), "Dock and loot the prey, stay a while, cast off, fly clear.");
            Assert.That(orders[0].Target, Is.EqualTo(SEntMan.GetNetEntity(prey)));
            Assert.That(state.Raided, Is.False);

            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WFEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(prey);
        });
        await RunTicks(10);
    }

/// <summary>An encounter with a start radius holds its orders back until a player comes that close.</summary>
    [Test]
    public async Task EncounterWaitsForAPlayerBeforeItBegins()
    {
        EntityUid encounter = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestEncounterWaiting");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(11000, 11000), MapData.MapId), out encounter), Is.True);
        });
        await RunTicks(400);
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            Assert.That(comp.Begun, Is.False, "Nobody is near yet.");
            Assert.That(comp.Ships["lone"].HasOrders, Is.False);
            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player),
                new EntityCoordinates(MapData.MapUid, new Vector2(11100, 11000)));
        });
        await WaitUntilServer(() => SEntMan.GetComponent<WFEncounterComponent>(encounter).Begun, 900);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["lone"];
            Assert.That(state.HasOrders, Is.True);
            Assert.That(Server.System<WFCrewObjectiveSystem>().Snapshot().Single(crew => crew.Group == state.Group).Objectives.Single().Kind,
                Is.EqualTo(WFCrewObjectiveKind.Hold));
            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WFEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }

    /// <summary>The wandering trader can be hurt, sells a few random things cheaply, and runs out.</summary>
    [Test]
    public async Task WanderingTraderSellsALimitedDiscountedStock()
    {
        await Server.WaitAssertion(() =>
        {
            var grid = Server.ResolveDependency<IMapManager>().CreateGridEntity(MapData.MapId);
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            var trader = SEntMan.SpawnAtPosition("WFTraderWanderer", new EntityCoordinates(grid.Owner, new Vector2(0.5f)));
            Assert.That(SEntMan.HasComponent<Content.Shared.Damage.Components.GodmodeComponent>(trader), Is.False, "This trader can be killed.");

            var shops = Server.System<Content.Server._WF.Traders.TraderShopSystem>();
            var shop = SEntMan.GetComponent<Content.Shared._WF.Traders.TraderShopComponent>(trader);
            var stock = shops.GetStock((trader, shop));
            Assert.That(stock, Is.Not.Empty);
            Assert.That(stock.Count, Is.LessThanOrEqualTo(shop.RandomStock));
            Assert.That(shop.Limited, Is.Not.Null);
            Assert.That(shop.Limited!.Values.All(left => left >= shop.StockMin && left <= shop.StockMax), Is.True);

            // The same item at a full-price trader costs more.
            shop.PriceMultiplier = 1f;
            var full = shops.GetStock((trader, shop)).ToDictionary(entry => entry.Item, entry => entry.Price);
            Assert.That(stock.All(entry => entry.Price <= full[entry.Item]), Is.True);
            Assert.That(stock.Any(entry => entry.Price < full[entry.Item]), Is.True);

            // Sold out of something, it is off the shelf.
            var gone = stock[0].Item;
            shop.Limited[gone] = 0;
            Assert.That(shops.GetStock((trader, shop)).Any(entry => entry.Item == gone), Is.False);
            SEntMan.DeleteEntity(trader);
            SEntMan.DeleteEntity(grid.Owner);
        });
    }

/// <summary>Hitting one side makes a player ship the other side's ally, and counts toward that side's reward.</summary>
    [Test]
    public async Task HittingOneSideAlliesAPlayerShipWithTheOther()
    {
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestEncounterSkirmish");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(15000, 15000), MapData.MapId), out var encounter), Is.True);
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            var friend = comp.Ships["friend"].Grid;
            var foe = comp.Ships["foe"].Grid;

            var grid = Server.ResolveDependency<IMapManager>().CreateGridEntity(MapData.MapId);
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            var transform = Server.System<SharedTransformSystem>();
            transform.SetCoordinates(grid.Owner, new EntityCoordinates(MapData.MapUid, new Vector2(15200, 15300)));
            transform.SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(grid.Owner, new Vector2(0.5f)));

            var rewards = Server.System<WFEncounterRewardSystem>();
            var escorts = Server.System<WFCrewEscortSystem>();
            for (var i = 0; i < WFEncounterRewardSystem.MinimumHits - 1; i++)
            {
                rewards.RecordHit(foe, grid.Owner);
            }

            Assert.That(escorts.AreInFormation(friend, grid.Owner), Is.False, "Not yet enough to count as fighting them.");
            rewards.RecordHit(foe, grid.Owner);
            Assert.That(escorts.AreInFormation(friend, grid.Owner), Is.True, "The side being helped takes the ship for an ally.");
            Assert.That(escorts.AreInFormation(foe, grid.Owner), Is.False);
            Assert.That(comp.Hits[ServerSession.UserId]["bad"], Is.EqualTo(WFEncounterRewardSystem.MinimumHits));

            Server.System<WFEncounterSystem>().Resolve(encounter, WFEncounterResolution.Ended);
            Assert.That(escorts.AreInFormation(friend, grid.Owner), Is.False, "The alliance ends with the encounter.");

            transform.SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WFEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(grid.Owner);
        });
        await RunTicks(10);
    }

/// <summary>
    /// A faction patrol's zone only answers to ships of a company it is at war with, and holds its fire on the other
    /// navy until war has been declared.
    /// </summary>
    [Test]
    public async Task PatrolZoneOnlyMindsCompaniesAtWar()
    {
        EntityUid encounter = default, patrol = default, intruder = default, host = default;
        await Server.WaitAssertion(() =>
        {
            // The war level lives on the sector service entity, which a station's host component brings up.
            host = SEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            SEntMan.AddComponent<Content.Server._NF.SectorServices.StationSectorServiceHostComponent>(host);

            var zones = Server.System<WFEncounterZoneSystem>();
            Assert.That(zones.AtWar("TSF", "RedSailCorsairs"), Is.True);
            Assert.That(zones.AtWar("RedSailCorsairs", "TSF"), Is.True, "War is mutual.");
            Assert.That(zones.AtWar("TSF", "PDV"), Is.False, "The ceasefire holds until war is declared.");
            Assert.That(zones.UnderCeasefire("PDV", "TSF"), Is.True);
            Assert.That(zones.UnderCeasefire("TSF", "USSP"), Is.False);
            Assert.That(zones.AtWar("TSF", "USSP"), Is.False);
            Assert.That(zones.AtWar("TSF", string.Empty), Is.False, "The unaffiliated are nobody's enemy.");

            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestEncounterWarZone");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(17000, 17000), MapData.MapId), out encounter), Is.True);
            patrol = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["patrol"].Grid;

            var grid = Server.ResolveDependency<IMapManager>().CreateGridEntity(MapData.MapId);
            intruder = grid.Owner;
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            var transform = Server.System<SharedTransformSystem>();
            transform.SetCoordinates(intruder, new EntityCoordinates(MapData.MapUid, new Vector2(17200, 17000)));
            transform.SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(intruder, new Vector2(0.5f)));
            SEntMan.EnsureComponent<Content.Shared._Mono.Company.CompanyComponent>(intruder).CompanyName = "USSP";
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["patrol"];
            Assert.That(state.Engaged, Is.Empty, "A USSP ship inside the ring is left alone.");
            SEntMan.GetComponent<Content.Shared._Mono.Company.CompanyComponent>(intruder).CompanyName = "PDV";
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["patrol"];
            Assert.That(state.Engaged, Is.Empty, "Under the ceasefire a PDV ship inside the ring is not fired on.");
            Server.System<Content.Server._Mono.AlertLevel.WarLevelSystem>().SetLevel(true);
            Assert.That(Server.System<WFEncounterZoneSystem>().AtWar("PDV", "TSF"), Is.True, "A declared war is mutual.");
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["patrol"];
            Assert.That(state.Engaged.Contains(intruder), Is.True, "Once war is declared a PDV ship inside the ring is an enemy.");
            Assert.That(Server.System<WFCrewAlertSystem>().GetHostileShips(patrol, state.Group), Does.Contain(intruder));
            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WFEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(intruder);
            SEntMan.DeleteEntity(host);
        });
        await RunTicks(10);
    }

/// <summary>Each specialist trader has its stock, and a killed one leaves a box with a few things out of it.</summary>
    [Test]
    public async Task KilledTraderLeavesABoxOfItsStock()
    {
        await Server.WaitAssertion(() =>
        {
            var grid = Server.ResolveDependency<IMapManager>().CreateGridEntity(MapData.MapId);
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            var shops = Server.System<Content.Server._WF.Traders.TraderShopSystem>();
            foreach (var kind in new[] { "WFTraderSurplus", "WFTraderCurios", "WFTraderMedic" })
            {
                var trader = SEntMan.SpawnAtPosition(kind, new EntityCoordinates(grid.Owner, new Vector2(0.5f)));
                var shop = SEntMan.GetComponent<Content.Shared._WF.Traders.TraderShopComponent>(trader);
                Assert.That(shops.GetStock((trader, shop)).Count, Is.EqualTo(shop.RandomStock), $"{kind} has a full stock.");

                Server.System<Content.Shared.Mobs.Systems.MobStateSystem>().ChangeMobState(trader, Content.Shared.Mobs.MobState.Dead);
                Assert.That(shop.LootDropped, Is.True, $"{kind} drops its box.");
                Assert.That(shop.Limited, Is.Empty, "What is not in the box is lost.");

                var crates = new List<EntityUid>();
                var query = SEntMan.EntityQueryEnumerator<Content.Server.Storage.Components.EntityStorageComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out _, out var xform))
                {
                    // Only the loot box itself: an item of stock can be a container of its own.
                    if (xform.GridUid == grid.Owner && SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == shop.LootCrate.Id)
                        crates.Add(uid);
                }

                Assert.That(crates, Has.Count.EqualTo(1));
                var held = SEntMan.GetComponent<Content.Server.Storage.Components.EntityStorageComponent>(crates[0]).Contents.ContainedEntities.Count;
                Assert.That(held, Is.InRange(1, shop.LootItems));
                SEntMan.DeleteEntity(crates[0]);
                SEntMan.DeleteEntity(trader);
            }

            SEntMan.DeleteEntity(grid.Owner);
        });
        await RunTicks(5);
    }

    /// <summary>An encounter whose crews are all dead is over, whatever state the hulls are in.</summary>
    [Test]
    public async Task EncounterWhoseCrewAreAllDeadIsOver()
    {
        EntityUid encounter = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestEncounterWarZone");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(19000, 19000), MapData.MapId), out encounter), Is.True);
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["patrol"];
            var crew = new List<EntityUid>();
            var query = SEntMan.EntityQueryEnumerator<WFCrewComponent>();
            while (query.MoveNext(out var uid, out var member))
            {
                if (member.Group == state.Group)
                    crew.Add(uid);
            }

            Assert.That(crew, Is.Not.Empty);
            foreach (var uid in crew)
            {
                SEntMan.DeleteEntity(uid);
            }
        });
        await RunTicks(400);
        await Server.WaitAssertion(() =>
        {
            // Resolved, or resolved and already cleared away.
            Assert.That(!SEntMan.TryGetComponent(encounter, out WFEncounterComponent? comp)
                || comp.Resolution == WFEncounterResolution.Destroyed, Is.True);
            Server.System<WFEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }

    private async Task WaitUntilServer(Func<bool> condition, int maxTicks)
    {
        var met = false;
        for (var waited = 0; waited < maxTicks && !met; waited += 20)
        {
            await RunTicks(20);
            await Server.WaitPost(() => met = condition());
        }

        Assert.That(met, Is.True, "The condition was not met in time.");
    }
}
