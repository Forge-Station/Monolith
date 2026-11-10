#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._NF.Bank;
using Content.Server._NF.BountyContracts;
using Content.Server._NF.SectorServices;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.BountyContracts;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    private static ProtoId<WFEncounterPrototype> WFTestBountyCrewProto => "WFTestBountyCrew";

    [TestPrototypes]
    private const string BountyPrototypes = @"
- type: entity
  id: WFTestSectorHost
  components:
  - type: StationSectorServiceHost

- type: wfEncounter
  id: WFTestBountyCaptain
  name: wf-encounter-name-convoy
  start: Manual
  bounty:
    ship: runner
    target: Captain
    spesos: 20000
    title: wf-bounty-title-fugitive-petty
    description: wf-bounty-desc-fugitive-petty
  ships:
  - key: runner
    vessel: WFDredger
    side: runner
    deckhands: 1
    evades: false
    flees: 1500
    wander: 2
    wanderRadius: 1000
    objectives:
    - kind: Hold

- type: wfEncounter
  id: WFTestBountyCrew
  name: wf-encounter-name-convoy
  start: Manual
  bounty:
    ship: raider
    target: Crew
    spesos: 8000
    title: wf-bounty-title-fugitive-wanted
    description: wf-bounty-desc-fugitive-wanted
  ships:
  - key: raider
    vessel: WFDredger
    side: raiders
    deckhands: 2
";

    private static readonly string[] FugitiveTiers =
    {
        "WFEncounterFugitivePetty", "WFEncounterFugitiveWanted", "WFEncounterFugitiveDangerous", "WFEncounterFugitiveMostWanted",
    };

    /// <summary>Makes sure the sector's bounty board exists, as it does on a real station; null if it already did.</summary>
    private EntityUid? EnsureBoard()
    {
        if (Server.System<SectorServiceSystem>().GetServiceEntity().IsValid())
            return null;

        return SEntMan.SpawnEntity("WFTestSectorHost", MapCoordinates.Nullspace);
    }

    private EntityUid SpawnBountyEncounter(string id, Vector2 at)
    {
        var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(id);
        Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(at, MapData.MapId), out var encounter), Is.True);
        return encounter;
    }

    /// <summary>A small grid in open space with the test player aboard; the caller deletes it.</summary>
    private EntityUid PutPlayerOnGrid(Vector2 at)
    {
        var grid = Server.System<SharedMapSystem>().CreateGridEntity(MapData.MapId);
        Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
        var transform = Server.System<SharedTransformSystem>();
        transform.SetCoordinates(grid.Owner, new EntityCoordinates(MapData.MapUid, at));
        transform.SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(grid.Owner, new Vector2(0.5f)));
        return grid.Owner;
    }

    private void SendPlayerHome(EntityUid? grid)
    {
        Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
        if (grid is { } uid)
            SEntMan.DeleteEntity(uid);
    }

    /// <summary>The warrant goes up on the board when the encounter starts, under the wanted man's name, and comes down when it is gone.</summary>
    [Test]
    public async Task WarrantIsPostedOnTheBoardAndComesDownWhenTheEncounterEndsUnpaid()
    {
        EntityUid? board = null;
        EntityUid encounter = default;
        uint id = 0;
        await Server.WaitAssertion(() =>
        {
            board = EnsureBoard();
            var contracts = Server.System<BountyContractSystem>();
            encounter = SpawnBountyEncounter("WFTestBountyCaptain", new Vector2(-30000, 30000));
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            var bounty = SEntMan.GetComponent<WFEncounterBountyComponent>(encounter);
            Assert.That(bounty.Contract, Is.Not.Null, "The board took the warrant.");
            id = bounty.Contract!.Value;

            Assert.That(contracts.TryGetContract(id, out var contract), Is.True);
            Assert.That(contracts.CollectionOf(id), Is.EqualTo(BountyContractSystem.WarrantCollection));
            Assert.That(contract!.Reward, Is.EqualTo(20000));
            Assert.That(contract.Category, Is.EqualTo(BountyContractCategory.Criminal));
            Assert.That(bounty.Fugitive, Is.Not.Null);
            Assert.That(contract.Name, Is.EqualTo(SEntMan.GetComponent<MetaDataComponent>(bounty.Fugitive!.Value).EntityName),
                "A warrant on a captain carries the wanted man's own name.");
            Assert.That(contract.Vessel, Is.EqualTo(SEntMan.GetComponent<MetaDataComponent>(comp.Ships["runner"].Grid).EntityName));
            Assert.That(SEntMan.HasComponent<WFEncounterBountyTargetComponent>(bounty.Fugitive.Value), Is.True, "He is flagged as the target.");
            Assert.That(SEntMan.GetComponent<WFCrewComponent>(bounty.Fugitive.Value).Role, Is.EqualTo(WFCrewRoles.Captain));
            Assert.That(contract.Description, Is.Not.Null.And.Not.Empty);
        });

        // An admin ending the encounter takes the warrant down and pays nobody.
        await Server.WaitAssertion(() =>
        {
            var player = SEntMan.GetEntity(Player);
            SEntMan.EnsureComponent<BankAccountComponent>(player);
            Server.System<WFEncounterSystem>().End(encounter);
            Assert.That(Server.System<BountyContractSystem>().TryGetContract(id, out _), Is.False, "Ended unpaid, the warrant is gone.");
        });
        await RunTicks(40);
        await Server.WaitAssertion(() =>
        {
            if (board is { } host)
                SEntMan.DeleteEntity(host);
        });
        await RunTicks(5);
    }

    /// <summary>An encounter that runs out takes its warrant down too, and a crew warrant is named for the ship.</summary>
    [Test]
    public async Task ExpiredEncounterWithdrawsItsCrewWarrant()
    {
        EntityUid? board = null;
        EntityUid encounter = default;
        uint id = 0;
        await Server.WaitAssertion(() =>
        {
            board = EnsureBoard();
            encounter = SpawnBountyEncounter("WFTestBountyCrew", new Vector2(-30000, 36000));
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            var bounty = SEntMan.GetComponent<WFEncounterBountyComponent>(encounter);
            id = bounty.Contract!.Value;
            Assert.That(Server.System<BountyContractSystem>().TryGetContract(id, out var contract), Is.True);
            Assert.That(contract!.Name, Is.EqualTo(SEntMan.GetComponent<MetaDataComponent>(comp.Ships["raider"].Grid).EntityName),
                "A crew warrant carries the ship's name.");
            Assert.That(bounty.Crew.Count, Is.GreaterThan(1), "Every hand is a target.");

            Server.System<WFEncounterSystem>().Resolve(encounter, WFEncounterResolution.Expired);
            Assert.That(Server.System<BountyContractSystem>().TryGetContract(id, out _), Is.False, "Expired, the warrant is gone.");
        });
        await Server.WaitAssertion(() =>
        {
            Server.System<WFEncounterSystem>().End(encounter);
            if (board is { } host)
                SEntMan.DeleteEntity(host);
        });
        await RunTicks(10);
    }

    /// <summary>Spesos added to a live warrant raise the same board entry's reward in place.</summary>
    [Test]
    public async Task AddingToTheWarrantUpdatesTheEntryInPlace()
    {
        EntityUid? board = null;
        EntityUid encounter = default;
        await Server.WaitAssertion(() =>
        {
            board = EnsureBoard();
            var contracts = Server.System<BountyContractSystem>();
            var bounties = Server.System<WFEncounterBountySystem>();
            encounter = SpawnBountyEncounter("WFTestBountyCaptain", new Vector2(-30000, 42000));
            var id = SEntMan.GetComponent<WFEncounterBountyComponent>(encounter).Contract!.Value;

            Assert.That(bounties.AddToBounty(encounter, 5000), Is.True);
            Assert.That(bounties.AddToBounty(encounter, 2500), Is.True);
            Assert.That(bounties.AddToBounty(encounter, 0), Is.False, "Nothing to add.");
            Assert.That(bounties.Pool(encounter), Is.EqualTo(27500));
            Assert.That(SEntMan.GetComponent<WFEncounterBountyComponent>(encounter).Contract, Is.EqualTo(id), "Same entry.");
            Assert.That(contracts.TryGetContract(id, out var contract), Is.True);
            Assert.That(contract!.Reward, Is.EqualTo(27500));

            Server.System<WFEncounterSystem>().End(encounter);
            Assert.That(bounties.AddToBounty(encounter, 100), Is.False, "A warrant that has come down takes nothing more.");
            if (board is { } host)
                SEntMan.DeleteEntity(host);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// A captain who is down but not dead is not taken. Once he is dead, the players who hit the ship with their ship's
    /// guns are paid the whole prize, the warrant comes off the board and the encounter is over.
    /// </summary>
    [Test]
    public async Task KillingTheFugitivePaysTheHelperAndClearsTheBoard()
    {
        EntityUid? board = null, grid = null;
        EntityUid encounter = default, fugitive = default;
        uint id = 0;
        var before = 0;
        await Server.WaitAssertion(() =>
        {
            board = EnsureBoard();
            encounter = SpawnBountyEncounter("WFTestBountyCaptain", new Vector2(-30000, 48000));
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            var bounty = SEntMan.GetComponent<WFEncounterBountyComponent>(encounter);
            id = bounty.Contract!.Value;
            fugitive = bounty.Fugitive!.Value;
            Server.System<WFEncounterBountySystem>().AddToBounty(encounter, 130000);

            // The test player's ship lands enough hits with its guns, far off.
            grid = PutPlayerOnGrid(new Vector2(-24000, 48000));
            var rewards = Server.System<WFEncounterRewardSystem>();
            for (var i = 0; i < WFEncounterRewardSystem.MinimumHits; i++)
            {
                rewards.RecordHit(comp.Ships["runner"].Grid, grid.Value);
            }

            var player = SEntMan.GetEntity(Player);
            before = SEntMan.EnsureComponent<BankAccountComponent>(player).Balance;
            Server.System<MobStateSystem>().ChangeMobState(fugitive, MobState.Critical);
        });
        await RunTicks(100);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<BountyContractSystem>().TryGetContract(id, out _), Is.True, "A man who is down is not dead.");
            Assert.That(SEntMan.GetComponent<WFEncounterBountyComponent>(encounter).State, Is.EqualTo(WFBountyState.Open));
            Server.System<MobStateSystem>().ChangeMobState(fugitive, MobState.Dead);
        });
        await RunTicks(100);
        await Server.WaitAssertion(() =>
        {
            var player = SEntMan.GetEntity(Player);
            Assert.That(SEntMan.GetComponent<BankAccountComponent>(player).Balance - before, Is.EqualTo(150000),
                "The player's ship landed the hits: the whole pool, past the 120000 hourly cap on rewards.");
            Assert.That(Server.System<BountyContractSystem>().TryGetContract(id, out _), Is.False, "Paid, the warrant is gone.");
            Assert.That(SEntMan.GetComponent<WFEncounterBountyComponent>(encounter).State, Is.EqualTo(WFBountyState.Claimed));
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(encounter).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));

            Server.System<WFEncounterSystem>().End(encounter);
            SendPlayerHome(grid);
            if (board is { } host)
                SEntMan.DeleteEntity(host);
        });
        await RunTicks(10);
    }

    /// <summary>A bystander near the fall is paid; a player whose ship never hit it and who is far off is not.</summary>
    [Test]
    public async Task OnlyThoseBesideTheFallOrInTheFightArePaid()
    {
        EntityUid? board = null, grid = null;
        EntityUid encounter = default, fugitive = default;
        var before = 0;
        await Server.WaitAssertion(() =>
        {
            board = EnsureBoard();
            encounter = SpawnBountyEncounter("WFTestBountyCaptain", new Vector2(-30000, 54000));
            var bounty = SEntMan.GetComponent<WFEncounterBountyComponent>(encounter);
            fugitive = bounty.Fugitive!.Value;

            // Far from the ship and never in the fight: nothing.
            grid = PutPlayerOnGrid(new Vector2(-20000, 54000));
            var player = SEntMan.GetEntity(Player);
            before = SEntMan.EnsureComponent<BankAccountComponent>(player).Balance;
            Server.System<MobStateSystem>().ChangeMobState(fugitive, MobState.Dead);
        });
        await RunTicks(100);
        await Server.WaitAssertion(() =>
        {
            var player = SEntMan.GetEntity(Player);
            Assert.That(SEntMan.GetComponent<BankAccountComponent>(player).Balance, Is.EqualTo(before), "A bystander far off is not paid.");
            Assert.That(SEntMan.GetComponent<WFEncounterBountyComponent>(encounter).State, Is.EqualTo(WFBountyState.Claimed),
                "The target is dead all the same: the warrant is closed with nobody to pay.");
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(encounter).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));
            Server.System<WFEncounterSystem>().End(encounter);
            SendPlayerHome(grid);
        });
        await RunTicks(10);

        // Standing beside the ship when he falls is enough.
        await Server.WaitAssertion(() =>
        {
            encounter = SpawnBountyEncounter("WFTestBountyCaptain", new Vector2(-30000, 60000));
            var bounty = SEntMan.GetComponent<WFEncounterBountyComponent>(encounter);
            fugitive = bounty.Fugitive!.Value;
            var shipGrid = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["runner"].Grid;
            var at = SEntMan.GetComponent<TransformComponent>(shipGrid).LocalPosition;
            grid = PutPlayerOnGrid(at + new Vector2(60f, 0f));
            before = SEntMan.GetComponent<BankAccountComponent>(SEntMan.GetEntity(Player)).Balance;
            Server.System<MobStateSystem>().ChangeMobState(fugitive, MobState.Dead);
        });
        await RunTicks(100);
        await Server.WaitAssertion(() =>
        {
            var player = SEntMan.GetEntity(Player);
            Assert.That(SEntMan.GetComponent<BankAccountComponent>(player).Balance - before, Is.EqualTo(20000), "Beside the ship when he fell.");
            Server.System<WFEncounterSystem>().End(encounter);
            SendPlayerHome(grid);
            if (board is { } host)
                SEntMan.DeleteEntity(host);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// An encounter's time does not run out while a player is beside the ship its warrant is on. Once they have gone
    /// it expires and the warrant comes down.
    /// </summary>
    [Test]
    public async Task WarrantDoesNotExpireUnderThePlayersOnIt()
    {
        EntityUid? board = null, grid = null;
        EntityUid encounter = default;
        uint id = 0;
        await Server.WaitAssertion(() =>
        {
            board = EnsureBoard();
            encounter = SpawnBountyEncounter("WFTestBountyCrew", new Vector2(-36000, 30000));
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            id = SEntMan.GetComponent<WFEncounterBountyComponent>(encounter).Contract!.Value;
            var at = SEntMan.GetComponent<TransformComponent>(comp.Ships["raider"].Grid).LocalPosition;
            grid = PutPlayerOnGrid(at + new Vector2(60f, 0f));
            comp.Expires = TimeSpan.Zero;
        });
        await RunTicks(400);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(encounter).Resolution, Is.Null, "Its time is up, but a player is on it.");
            Assert.That(Server.System<BountyContractSystem>().TryGetContract(id, out _), Is.True, "The warrant stands.");
            SendPlayerHome(grid);
            grid = null;
        });
        await WaitUntilServer(() => SEntMan.GetComponent<WFEncounterComponent>(encounter).Resolution != null, 2400);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(encounter).Resolution, Is.EqualTo(WFEncounterResolution.Expired));
            Assert.That(Server.System<BountyContractSystem>().TryGetContract(id, out _), Is.False, "Left alone, it expires and the warrant goes.");
            Server.System<WFEncounterSystem>().End(encounter);
            if (board is { } host)
                SEntMan.DeleteEntity(host);
        });
        await RunTicks(10);
    }

    /// <summary>A player who hurt the wanted man and died for it is still paid, far off and dead as he is.</summary>
    [Test]
    public async Task AFighterWhoFellIsStillPaid()
    {
        EntityUid? board = null, grid = null;
        EntityUid encounter = default;
        var before = 0;
        await Server.WaitAssertion(() =>
        {
            board = EnsureBoard();
            encounter = SpawnBountyEncounter("WFTestBountyCaptain", new Vector2(-36000, 36000));
            var bounty = SEntMan.GetComponent<WFEncounterBountyComponent>(encounter);
            grid = PutPlayerOnGrid(new Vector2(-26000, 36000));
            var player = SEntMan.GetEntity(Player);
            SEntMan.EnsureComponent<BankAccountComponent>(player);
            Assert.That(Server.System<BankSystem>().TryGetBalance(ServerSession, out before), Is.True);

            bounty.Fighters.Add(ServerSession.UserId);
            var mobs = Server.System<MobStateSystem>();
            SEntMan.EnsureComponent<MobStateComponent>(player);
            mobs.ChangeMobState(player, MobState.Dead);
            Assert.That(mobs.IsDead(player), Is.True, "The test needs a dead player.");
            mobs.ChangeMobState(bounty.Fugitive!.Value, MobState.Dead);
        });
        await RunTicks(100);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WFEncounterBountyComponent>(encounter).State, Is.EqualTo(WFBountyState.Claimed));
            Assert.That(Server.System<BankSystem>().TryGetBalance(ServerSession, out var after), Is.True);
            Assert.That(after - before, Is.EqualTo(20000), "He earned it before he died.");
            Server.System<WFEncounterSystem>().End(encounter);
            SendPlayerHome(grid);
            if (board is { } host)
                SEntMan.DeleteEntity(host);
        });
        await RunTicks(10);
    }

    /// <summary>A crew warrant is taken once the ship is gone.</summary>
    [Test]
    public async Task CrewWarrantIsTakenWhenTheShipIsGone()
    {
        EntityUid? board = null;
        EntityUid encounter = default;
        uint id = 0;
        await Server.WaitAssertion(() =>
        {
            board = EnsureBoard();
            // Pinned, so the encounter is still there to look at after the poll that finds its only ship gone.
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(WFTestBountyCrewProto);
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-30000, 66000), MapData.MapId),
                out encounter, pinned: true), Is.True);
            id = SEntMan.GetComponent<WFEncounterBountyComponent>(encounter).Contract!.Value;
            SEntMan.DeleteEntity(SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["raider"].Grid);
        });
        await RunTicks(100);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<BountyContractSystem>().TryGetContract(id, out _), Is.False);
            Assert.That(SEntMan.GetComponent<WFEncounterBountyComponent>(encounter).State, Is.EqualTo(WFBountyState.Claimed));
            Server.System<WFEncounterSystem>().End(encounter);
            if (board is { } host)
                SEntMan.DeleteEntity(host);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// A ship that flees flies directly away from a player ship inside its range, and with the player gone goes back
    /// to its wander.
    /// </summary>
    [Test]
    public async Task FleeingShipRunsFromAPlayerShipAndResumesItsWanderOnceClear()
    {
        EntityUid? board = null, grid = null;
        EntityUid encounter = default, ship = default;
        var origin = new Vector2(-30000, 72000);
        await Server.WaitAssertion(() =>
        {
            board = EnsureBoard();
            encounter = SpawnBountyEncounter("WFTestBountyCaptain", origin);
            ship = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["runner"].Grid;
            // The player's ship is 800 m off to the east of the fugitive.
            grid = PutPlayerOnGrid(SEntMan.GetComponent<TransformComponent>(ship).LocalPosition + new Vector2(800f, 0f));
        });
        // Past the encounter's poll.
        await RunTicks(200);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["runner"];
            Assert.That(state.Fleeing, Is.True, "A player ship inside its range sets it running.");
            var net = SEntMan.GetNetEntity(ship);
            var row = Server.System<WFCrewObjectiveSystem>().Snapshot().Single(crew => crew.Grid == net && crew.Group == state.Group);
            Assert.That(row.Objectives, Is.Not.Empty);
            Assert.That(row.Objectives[0].Kind, Is.EqualTo(WFCrewObjectiveKind.GoTo));
            var here = SEntMan.GetComponent<TransformComponent>(ship).LocalPosition;
            var away = row.Objectives[0].Position - here;
            Assert.That(away.X, Is.LessThan(-1000f), "Its run takes it west, away from the ship in the east.");
            Assert.That(MathF.Abs(away.Y), Is.LessThan(MathF.Abs(away.X) * 0.3f), "Directly away.");
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(encounter).Resolution, Is.Null, "It started no fight.");

            // The player leaves.
            SendPlayerHome(grid);
            grid = null;
        });
        await RunTicks(200);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["runner"];
            Assert.That(state.Fleeing, Is.False, "With nobody near it goes back to wandering.");
            var net = SEntMan.GetNetEntity(ship);
            var row = Server.System<WFCrewObjectiveSystem>().Snapshot().Single(crew => crew.Grid == net && crew.Group == state.Group);
            Assert.That(row.Objectives, Is.Not.Empty);
            Server.System<WFEncounterSystem>().End(encounter);
            if (board is { } host)
                SEntMan.DeleteEntity(host);
        });
        await RunTicks(10);
    }

    /// <summary>Every tier posts a warrant for a named captain, climbing in prize, crew and skill.</summary>
    [Test]
    public async Task EveryFugitiveTierPostsAWarrantAndClimbs()
    {
        EntityUid? board = null;
        var crews = new List<int>();
        var prizes = new List<int>();
        await Server.WaitAssertion(() =>
        {
            board = EnsureBoard();
            var y = 78000f;
            foreach (var id in FugitiveTiers)
            {
                var encounter = SpawnBountyEncounter(id, new Vector2(-30000, y));
                y += 6000f;
                var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
                var bounty = SEntMan.GetComponent<WFEncounterBountyComponent>(encounter);
                Assert.That(bounty.Contract, Is.Not.Null, id);
                Assert.That(bounty.Fugitive, Is.Not.Null, $"{id} has a wanted man.");
                var ship = comp.Ships["fugitive"];
                Assert.That(ship.Flees, Is.GreaterThan(0f), id);
                Assert.That(ship.Hunt, Is.False, id);
                var members = 0;
                var query = SEntMan.EntityQueryEnumerator<WFCrewComponent>();
                while (query.MoveNext(out _, out var member))
                {
                    if (member.Group == ship.Group)
                        members++;
                }

                crews.Add(members);
                prizes.Add(bounty.Pool);
                Server.System<WFEncounterSystem>().End(encounter);
            }

            if (board is { } host)
                SEntMan.DeleteEntity(host);
        });
        await RunTicks(10);
        Assert.That(prizes, Is.Ordered, "The prize climbs with the tier.");
        Assert.That(crews[^1], Is.GreaterThan(crews[0]), $"The crew grows: {string.Join(", ", crews)}.");
        Assert.That(crews[0], Is.GreaterThanOrEqualTo(2), "The petty fugitive has a pilot as well.");
        TestContext.Out.WriteLine($"Crew sizes by tier: {string.Join(", ", crews)}");
    }
}
