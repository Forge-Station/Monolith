#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.SectorControl.Components;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._WF.SectorControl;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.SectorControl;

/// <summary>Company claim beacons: one per cell, player fire only, and admin or NPC cells stay theirs.</summary>
[TestOf(typeof(WFSectorBeaconSystem))]
public sealed class WFSectorBeaconTest : WFSectorTestBase
{
    private static readonly WFSectorCell Far = new(4, -2);
    private static readonly WFSectorCell Next = new(5, -2);

    private const string CompanyA = "WFTestBeaconA";
    private const string CompanyB = "WFTestBeaconB";
    private const string FactionA = "WFSectorBeaconWFTestBeaconA";
    private const string FactionB = "WFSectorBeaconWFTestBeaconB";

    [TestPrototypes]
    private const string Prototypes = @"
- type: company
  id: WFTestBeaconA
  name: Beacon A
  color: ""#11cc55""
- type: company
  id: WFTestBeaconB
  name: Beacon B
  color: ""#2255ee""
- type: wfSectorFaction
  id: WFSectorBeaconWFTestBeaconA
  name: Beacon A
  color: ""#11cc55""
  company: WFTestBeaconA
  playerBeacons: true
- type: wfSectorFaction
  id: WFSectorBeaconWFTestBeaconB
  name: Beacon B
  color: ""#2255ee""
  company: WFTestBeaconB
  playerBeacons: true
";

    [TearDown]
    public async Task ResetBeacons()
    {
        await Server.WaitAssertion(() =>
        {
            var doomed = new List<EntityUid>();
            var query = SEntMan.AllEntityQueryEnumerator<WFSectorBeaconComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                doomed.Add(uid);
            }

            foreach (var uid in doomed)
            {
                SEntMan.DeleteEntity(uid);
            }

            Server.System<WFSectorTerritorySystem>().Clear(MapData.MapId);
            Disembark();
        });
    }

    /// <summary>Every company a player can belong to has a beacon faction, so the map can paint its colour.</summary>
    [Test]
    public async Task EveryCompanyHasABeaconFaction()
    {
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            var factions = prototypes.EnumeratePrototypes<WFSectorFactionPrototype>().ToList();
            foreach (var company in prototypes.EnumeratePrototypes<CompanyPrototype>())
            {
                if (company.ID == "None")
                    continue;

                Assert.That(factions.Any(faction => faction.PlayerBeacons && faction.Company == company.ID), Is.True, company.ID);
            }
        });
    }

    /// <summary>A player beacon claims its cell in the company's colour, and a second beacon in that cell does not.</summary>
    [Test]
    public async Task OneBeaconClaimsTheCell()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var beacons = Server.System<WFSectorBeaconSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var player = Join(CompanyA);
            var first = Plant(CentreOf(Far));
            var second = Plant(CentreOf(Far) + new Vector2(40f, 0f));

            Assert.That(beacons.TryActivate(first, player, out _), Is.True);
            Assert.That(territory.Owner(MapData.MapId, Far)?.Id, Is.EqualTo(FactionA));
            Assert.That(beacons.TryActivate(second, player, out var occupied), Is.False);
            Assert.That(occupied, Is.EqualTo(WFSectorBeaconSystem.ReasonOccupied));
            Assert.That(territory.Owner(MapData.MapId, Far)?.Id, Is.EqualTo(FactionA));
        });
    }

    /// <summary>A player beacon does not paint a cell an NPC faction already holds, or a cell with an admin beacon.</summary>
    [Test]
    public async Task PlayerBeaconsLeaveNpcAndAdminCells()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var beacons = Server.System<WFSectorBeaconSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var player = Join(CompanyA);
            var map = MapData.MapId;

            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);
            var blocked = Plant(CentreOf(Far));
            Assert.That(beacons.TryActivate(blocked, player, out var held), Is.False);
            Assert.That(held, Is.EqualTo(WFSectorBeaconSystem.ReasonHeld));
            Assert.That(territory.Owner(map, Far)?.Id, Is.EqualTo(Holder));

            var immortal = Plant(CentreOf(Next), "WFSectorBeaconImmortal");
            var comp = SEntMan.GetComponent<WFSectorBeaconComponent>(immortal);
            comp.Company = CompanyB;
            Assert.That(beacons.TryActivate((immortal, comp), player, out var adminOnly), Is.False);
            Assert.That(adminOnly, Is.EqualTo(WFSectorBeaconSystem.ReasonAdminOnly));
            Assert.That(beacons.TryActivate((immortal, comp), null, out _), Is.True, "A mapped beacon claims without a player.");
            Assert.That(territory.Owner(map, Next)?.Id, Is.EqualTo(FactionB));

            var playerBeacon = Plant(CentreOf(Next) + new Vector2(40f, 0f));
            Assert.That(beacons.TryActivate(playerBeacon, player, out var occupied), Is.False);
            Assert.That(occupied, Is.EqualTo(WFSectorBeaconSystem.ReasonOccupied));
            Assert.That(territory.Owner(map, Next)?.Id, Is.EqualTo(FactionB));
        });
    }

    /// <summary>Destroying a player beacon frees the cell. NPC damage and admin beacons do not.</summary>
    [Test]
    public async Task DestroyingTheBeaconClearsTheColour()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var beacons = Server.System<WFSectorBeaconSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var damage = Server.System<DamageableSystem>();
            var player = Join(CompanyA);
            var map = MapData.MapId;
            var beacon = Plant(CentreOf(Far));
            Assert.That(beacons.TryActivate(beacon, player, out _), Is.True);

            var blunt = Server.ResolveDependency<IPrototypeManager>().Index<DamageTypePrototype>("Blunt");
            var spec = new DamageSpecifier(blunt, FixedPoint2.New(40));
            var npc = SEntMan.SpawnEntity(null, new EntityCoordinates(MapData.MapUid, new Vector2(80000f, 80000f)));
            Assert.That(damage.TryChangeDamage(beacon, spec, ignoreResistances: true, origin: npc), Is.Null, "NPC fire does nothing.");
            Assert.That(SEntMan.GetComponent<DamageableComponent>(beacon).TotalDamage, Is.EqualTo(FixedPoint2.Zero));
            Assert.That(damage.TryChangeDamage(beacon, spec, ignoreResistances: true, origin: player), Is.Not.Null, "A player can damage it.");

            SEntMan.DeleteEntity(beacon);
            Assert.That(territory.Owner(map, Far), Is.Null, "The colour leaves with the beacon.");

            var immortal = Plant(CentreOf(Next), "WFSectorBeaconImmortal");
            var comp = SEntMan.GetComponent<WFSectorBeaconComponent>(immortal);
            comp.Company = CompanyA;
            Assert.That(beacons.TryActivate((immortal, comp), null, out _), Is.True);
            Assert.That(damage.TryChangeDamage(immortal, spec, ignoreResistances: true, origin: player), Is.Null);
            Assert.That(SEntMan.GetComponent<DamageableComponent>(immortal).TotalDamage, Is.EqualTo(FixedPoint2.Zero));
            Assert.That(territory.Owner(map, Next)?.Id, Is.EqualTo(FactionA));
        });
    }

    /// <summary>A beacon that leaves its cell drops the claim, and a parked ship does not retake the cell out from under it.</summary>
    [Test]
    public async Task LeavingTheCellDropsItAndAParkedShipDoesNot()
    {
        await StartRound();
        EntityUid grid = default;
        await Server.WaitAssertion(() =>
        {
            var beacons = Server.System<WFSectorBeaconSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var player = Join(CompanyA);
            var map = MapData.MapId;
            grid = SpawnGrid(CentreOf(Far));
            var beacon = PlantOn(grid);
            Assert.That(beacons.TryActivate(beacon, player, out _), Is.True);
            Board(grid);
            territory.CheckRetakes(map);
            Assert.That(territory.RetakeOf(map, Far), Is.Null, "The beacon keeps the cell.");

            SEntMan.DeleteEntity(beacon);
            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);
            territory.CheckRetakes(map);
            Assert.That(territory.RetakeOf(map, Far), Is.Not.Null, "Without the beacon the parked ship starts a retake.");
            territory.Clear(map);

            var again = PlantOn(grid);
            Disembark();
            Assert.That(beacons.TryActivate(again, player, out _), Is.True);
        });

        await Server.WaitAssertion(() => Place(grid, CentreOf(Next)));
        await RunTicks(120);
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            Assert.That(territory.Owner(MapData.MapId, Far), Is.Null, "Flying the beacon out of the hex drops the colour.");
            Assert.That(territory.Owner(MapData.MapId, Next), Is.Null, "It does not claim the hex it drifted into.");
        });
    }

    private EntityUid Join(string company)
    {
        var player = SEntMan.GetEntity(Player);
        var comp = SEntMan.EnsureComponent<CompanyComponent>(player);
        comp.CompanyName = company;
        return player;
    }

    private EntityUid Plant(Vector2 position, string prototype = "WFSectorBeacon")
    {
        return PlantOn(SpawnGrid(position), prototype);
    }

    private EntityUid PlantOn(EntityUid grid, string prototype = "WFSectorBeacon")
    {
        var beacon = SEntMan.SpawnAtPosition(prototype, new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
        var xform = SEntMan.GetComponent<TransformComponent>(beacon);
        if (!xform.Anchored)
            Server.System<SharedTransformSystem>().AnchorEntity(beacon, xform);

        Assert.That(xform.Anchored, Is.True, "The beacon has to be anchored before it can claim.");
        return beacon;
    }
}
