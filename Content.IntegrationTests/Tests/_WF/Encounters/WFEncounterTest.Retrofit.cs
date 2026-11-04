#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Mono.FireControl;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._Mono.ShipGuns;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Decals;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    private static readonly ProtoId<WFEncounterPrototype> WFReaverPatrolProto = "WFReaverPatrol";
    private static readonly ProtoId<WFEncounterPrototype> WFReaverStrongholdProto = "WFReaverStronghold";
    private static readonly ProtoId<VesselPrototype> VictoriaProto = "Victoria";
    private static readonly ProtoId<WFPirateRetrofitPrototype> WFPirateRetrofitLightProto = "WFPirateRetrofitLight";

    private static readonly string[] WornTiles = { "FloorSteelDirty", "FloorSteelDamaged", "PlatingDamaged", "FloorBrokenWood" };

    /// <summary>
    /// A tier 0 Reaver patrol flies retrofitted civilian hulls: a Green crew, only light guns on its hardpoints and taken
    /// by its fire control, and worn decks. At tier 4 it flies the black-market line, ranked on the map by its tier.
    /// </summary>
    [Test]
    public async Task LowTierReaversFlyRetrofittedCivilianHulls()
    {
        EntityUid low = default, high = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(WFReaverPatrolProto);
            var encounters = Server.System<WFEncounterSystem>();
            Assert.That(encounters.TrySpawn(prototype, new MapCoordinates(new Vector2(-45000, -45000), MapData.MapId), out low, pinned: true, tier: 0), Is.True);
            Assert.That(encounters.TrySpawn(prototype, new MapCoordinates(new Vector2(-48000, -48000), MapData.MapId), out high, pinned: true, tier: 4), Is.True);
        });
        // Power comes up and the fire control server takes the guns; a slow runner takes longer than a second.
        var controlled = false;
        for (var i = 0; i < 20 && !controlled; i++)
        {
            await RunTicks(60);
            await Server.WaitPost(() =>
            {
                var lead = SEntMan.GetComponent<WFEncounterComponent>(low).Ships["lead"];
                controlled = Guns(lead.Grid).All(gun => SEntMan.GetComponent<FireControllableComponent>(gun).ControllingServer != null);
            });
        }
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(WFReaverPatrolProto);
            var lowComp = SEntMan.GetComponent<WFEncounterComponent>(low);
            Assert.That(lowComp.Tier, Is.EqualTo(0));
            Assert.That(lowComp.Threat, Is.EqualTo(0), "Its rank on the map is its tier.");

            var lead = lowComp.Ships["lead"];
            var band = WFEncounterSystem.BandFor(prototype.Ships.First(ship => ship.Key == "lead"), 0)!;
            Assert.That(VesselOf(lead.Grid, band.Vessels), Is.Not.Null, "The lead flies a civilian hull of the tier 0 band.");
            Assert.That(lead.Retrofitted, Is.True);

            var guns = Guns(lead.Grid);
            Assert.That(guns.Count, Is.InRange(2, 3), "Two or three guns go on.");
            foreach (var gun in guns)
            {
                Assert.That(SEntMan.GetComponent<HardpointAnchorableOnlyComponent>(gun).CompatibleSizes, Is.LessThanOrEqualTo(weaponSizes.Light),
                    $"{SEntMan.ToPrettyString(gun)} is no bigger than light.");
                Assert.That(SEntMan.GetComponent<HardpointAnchorableOnlyComponent>(gun).anchoredTo, Is.Not.Null, "Each sits on a hardpoint.");
                Assert.That(SEntMan.GetComponent<FireControllableComponent>(gun).ControllingServer, Is.Not.Null, "And fire control takes it.");
            }

            Assert.That(WornTileCount(lead.Grid), Is.GreaterThan(0), "Its decks are worn.");
            Assert.That(Crew(lead.Group), Is.Not.Empty, "It is crewed.");
            foreach (var crewman in Crew(lead.Group))
            {
                Assert.That(SEntMan.GetComponent<WFCrewComponent>(crewman).Skill, Is.EqualTo(WFCrewSkill.Green), "Its crew are green.");
            }

            var highComp = SEntMan.GetComponent<WFEncounterComponent>(high);
            Assert.That(highComp.Threat, Is.EqualTo(4));
            var highLead = highComp.Ships["lead"];
            var original = prototype.Ships.First(ship => ship.Key == "lead").Vessels;
            Assert.That(VesselOf(highLead.Grid, original), Is.Not.Null, "At tier 4 the lead is a black-market gunship.");
            Assert.That(highLead.Retrofitted, Is.False, "Its guns are its own.");
            Assert.That(WornTileCount(highLead.Grid), Is.GreaterThan(0), "But its decks are worn as well.");

            Server.System<WFEncounterSystem>().End(low);
            Server.System<WFEncounterSystem>().End(high);
        });
        await RunTicks(10);
    }

    /// <summary>A tier 0 stronghold hub is a hull of the hub pool with exactly four light guns.</summary>
    [Test]
    public async Task LowTierStrongholdHubsCarryFourLightGuns()
    {
        EntityUid hub = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(WFReaverStrongholdProto);
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-51000, -51000), MapData.MapId), out hub, pinned: true, tier: 0), Is.True);
            var lead = SEntMan.GetComponent<WFEncounterComponent>(hub).Ships["lead"];
            var band = WFEncounterSystem.BandFor(prototype.Ships[0], 0)!;
            Assert.That(VesselOf(lead.Grid, band.Vessels), Is.Not.Null, "The hub is a hull of the pool.");
            var guns = Guns(lead.Grid);
            Assert.That(guns.Count, Is.EqualTo(4), "Every hub carries the same four guns.");
            Assert.That(guns.All(gun => SEntMan.GetComponent<HardpointAnchorableOnlyComponent>(gun).CompatibleSizes <= weaponSizes.Light), Is.True);
            Server.System<WFEncounterSystem>().End(hub);
        });
        await RunTicks(10);
    }

    [TestPrototypes]
    private const string UsspHubPrototypes = @"
- type: wfEncounter
  id: WFTestUsspHubLedokol
  name: wf-reaver-name-stronghold
  start: Manual
  lifetime: Persistent
  ships:
  - key: lead
    vessel: Ledokol
    retrofit: WFPirateRetrofitHubLight
    deckhands: 0
    captain: false
    roles: [ WFCrewPilot ]

- type: wfEncounter
  id: WFTestUsspHubBuran
  name: wf-reaver-name-stronghold
  start: Manual
  lifetime: Persistent
  ships:
  - key: lead
    vessel: Buran
    retrofit: WFPirateRetrofitHubHeavy
    deckhands: 0
    captain: false
    roles: [ WFCrewPilot ]

- type: wfEncounter
  id: WFTestUsspHubLedokolHeavy
  name: wf-reaver-name-stronghold
  start: Manual
  lifetime: Persistent
  ships:
  - key: lead
    vessel: Ledokol
    retrofit: WFPirateRetrofitHubHeavy
    deckhands: 0
    captain: false
    roles: [ WFCrewPilot ]

- type: wfEncounter
  id: WFTestUsspHubLedokolSiege
  name: wf-reaver-name-stronghold
  start: Manual
  lifetime: Persistent
  ships:
  - key: lead
    vessel: Ledokol
    retrofit: WFPirateRetrofitHubSiege
    deckhands: 0
    captain: false
    roles: [ WFCrewPilot ]

- type: wfEncounter
  id: WFTestUsspHubBuranSiege
  name: wf-reaver-name-stronghold
  start: Manual
  lifetime: Persistent
  ships:
  - key: lead
    vessel: Buran
    retrofit: WFPirateRetrofitHubSiege
    deckhands: 0
    captain: false
    roles: [ WFCrewPilot ]
";

    /// <summary>The USSP hub hulls take the hub refit's fixed gun count and carry docking ports for reinforcements.</summary>
    [TestCase("WFTestUsspHubLedokol", 4)]
    [TestCase("WFTestUsspHubBuran", 6)]
    [TestCase("WFTestUsspHubLedokolHeavy", 6)]
    [TestCase("WFTestUsspHubLedokolSiege", 6)]
    [TestCase("WFTestUsspHubBuranSiege", 6)]
    public async Task UsspHubsTakeTheHubRefit(string id, int guns)
    {
        EntityUid hub = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(id);
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-54000, -54000), MapData.MapId), out hub, pinned: true), Is.True);
            var grid = SEntMan.GetComponent<WFEncounterComponent>(hub).Ships["lead"].Grid;
            Assert.That(Guns(grid), Has.Count.EqualTo(guns), "The refit sets the gun count, whatever the hull was mapped with.");

            var ports = 0;
            var query = SEntMan.EntityQueryEnumerator<Content.Server.Shuttles.Components.DockingComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var xform))
            {
                if (xform.GridUid == grid)
                    ports++;
            }

            Assert.That(ports, Is.GreaterThanOrEqualTo(4), "Room for reinforcements to dock.");
            Server.System<WFEncounterSystem>().End(hub);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// A hull saved with an empty fire control server still has its retrofitted guns taken at once, rather than once the
    /// server's battery has charged off the ship's power.
    /// </summary>
    [Test]
    public async Task RetrofitTakesGunsOnAHullSavedWithAnEmptyServer()
    {
        var prototypes = Server.ResolveDependency<IPrototypeManager>();
        EntityUid grid = default;
        await Server.WaitAssertion(() =>
        {
            var patrol = prototypes.Index<WFEncounterPrototype>(WFReaverPatrolProto);
            var band = WFEncounterSystem.BandFor(patrol.Ships.First(ship => ship.Key == "lead"), 0)!;
            Assert.That(band.Retrofit, Is.Not.Null);
            var path = prototypes.Index<VesselPrototype>(VictoriaProto).ShuttlePath;
            Assert.That(SEntMan.System<MapLoaderSystem>().TryLoadGrid(MapData.MapId, path, out var loaded,
                offset: new Vector2(-60000, -60000)), Is.True);
            grid = loaded!.Value.Owner;
            Server.System<WFPirateRetrofitSystem>().Apply(grid, prototypes.Index(band.Retrofit!.Value), 1);
        });
        // The power net updates receivers every half second, and a slow runner can take a few intervals.
        var controlled = false;
        for (var i = 0; i < 20 && !controlled; i++)
        {
            await RunTicks(20);
            await Server.WaitPost(() => controlled = Guns(grid).Count > 0
                && Guns(grid).All(gun => SEntMan.GetComponent<FireControllableComponent>(gun).ControllingServer != null));
        }
        await Server.WaitAssertion(() =>
        {
            var guns = Guns(grid);
            Assert.That(guns, Is.Not.Empty);
            foreach (var gun in guns)
            {
                Assert.That(SEntMan.GetComponent<FireControllableComponent>(gun).ControllingServer, Is.Not.Null,
                    $"{SEntMan.ToPrettyString(gun)} was left to an uncharged server.");
            }

            SEntMan.DeleteEntity(grid);
        });
        await RunTicks(5);
    }

    /// <summary>A retrofit run a step a tick leaves the hull just as one run in one go with the same seed.</summary>
    [Test]
    public async Task SteppedRetrofitMatchesOneGo()
    {
        var prototypes = Server.ResolveDependency<IPrototypeManager>();
        var retrofit = prototypes.Index<WFPirateRetrofitPrototype>(WFPirateRetrofitLightProto);
        var path = prototypes.Index<VesselPrototype>(VictoriaProto).ShuttlePath;
        var system = Server.System<WFPirateRetrofitSystem>();
        EntityUid whole = default, stepped = default;
        WFPirateRetrofitRun run = default!;
        await Server.WaitAssertion(() =>
        {
            var loader = SEntMan.System<MapLoaderSystem>();
            Assert.That(loader.TryLoadGrid(MapData.MapId, path, out var a, offset: new Vector2(-57000, -57000)), Is.True);
            Assert.That(loader.TryLoadGrid(MapData.MapId, path, out var b, offset: new Vector2(-57000, -56000)), Is.True);
            whole = a!.Value.Owner;
            stepped = b!.Value.Owner;
            system.Apply(whole, retrofit, 7);
            run = system.Begin(7);
        });

        var steps = 0;
        var done = false;
        while (!done)
        {
            await Server.WaitPost(() => done = system.Step(stepped, retrofit, run));
            await RunTicks(1);
            steps++;
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(steps, Is.EqualTo(WFPirateRetrofitRun.Steps), "Each part of the retrofit should take its own step.");
            Assert.That(Signature(stepped, retrofit), Is.EqualTo(Signature(whole, retrofit)), "The stepped retrofit came out different.");
            SEntMan.DeleteEntity(whole);
            SEntMan.DeleteEntity(stepped);
        });
        await RunTicks(5);
    }

    /// <summary>
    /// What a hull is made of: its tiles, its decals and the prototypes anchored or standing on it, counted. Loose litter
    /// is left out, since it can be knocked about or eaten by a machine between ticks.
    /// </summary>
    private List<string> Signature(EntityUid grid, WFPirateRetrofitPrototype retrofit)
    {
        var parts = new List<string>();
        var gridComp = SEntMan.GetComponent<MapGridComponent>(grid);
        var tiles = SEntMan.System<SharedMapSystem>().GetAllTiles(grid, gridComp);
        while (tiles.MoveNext(out var tile))
        {
            parts.Add($"tile {tile.Value.GridIndices} {tile.Value.Tile.TypeId}");
        }

        var children = SEntMan.GetComponent<TransformComponent>(grid).ChildEnumerator;
        var protos = new Dictionary<string, int>();
        while (children.MoveNext(out var child))
        {
            var id = SEntMan.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID ?? "?";
            if (retrofit.Litter.Contains(id))
                continue;

            protos[id] = protos.GetValueOrDefault(id) + 1;
        }

        parts.AddRange(protos.Select(p => $"{p.Key} x{p.Value}"));
        var decals = SEntMan.GetComponent<DecalGridComponent>(grid).ChunkCollection.ChunkCollection.Values;
        parts.AddRange(decals.SelectMany(chunk => chunk.Decals.Values).Select(decal => $"decal {decal.Id} {decal.Coordinates}"));
        parts.Sort(string.CompareOrdinal);
        return parts;
    }

    /// <summary>The vessel of a list whose name the grid carries, or none.</summary>
    private string? VesselOf(EntityUid grid, List<ProtoId<VesselPrototype>> vessels)
    {
        var prototypes = Server.ResolveDependency<IPrototypeManager>();
        var name = SEntMan.GetComponent<MetaDataComponent>(grid).EntityName;
        return vessels.Select(id => prototypes.Index(id)).FirstOrDefault(vessel => name.StartsWith(vessel.Name + " "))?.ID;
    }

    /// <summary>The ship guns anchored on a grid.</summary>
    private List<EntityUid> Guns(EntityUid grid)
    {
        var guns = new List<EntityUid>();
        var query = SEntMan.EntityQueryEnumerator<HardpointAnchorableOnlyComponent, ShipGunClassComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored)
                guns.Add(uid);
        }

        return guns;
    }

    private int WornTileCount(EntityUid grid)
    {
        var tileDefs = Server.ResolveDependency<ITileDefinitionManager>();
        var map = Server.System<SharedMapSystem>();
        var comp = SEntMan.GetComponent<MapGridComponent>(grid);
        var count = 0;
        var tiles = map.GetAllTiles(grid, comp);
        while (tiles.MoveNext(out var tile))
        {
            if (WornTiles.Contains(tileDefs[tile.Value.Tile.TypeId].ID))
                count++;
        }

        return count;
    }

    private List<EntityUid> Crew(string group)
    {
        var crew = new List<EntityUid>();
        var query = SEntMan.EntityQueryEnumerator<WFCrewComponent>();
        while (query.MoveNext(out var uid, out var member))
        {
            if (member.Group == group)
                crew.Add(uid);
        }

        return crew;
    }
}
