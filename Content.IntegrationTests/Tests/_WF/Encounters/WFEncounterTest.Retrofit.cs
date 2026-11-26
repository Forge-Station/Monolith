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
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
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
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFReaverPatrol");
            var encounters = Server.System<WFEncounterSystem>();
            Assert.That(encounters.TrySpawn(prototype, new MapCoordinates(new Vector2(-45000, -45000), MapData.MapId), out low, pinned: true, tier: 0), Is.True);
            Assert.That(encounters.TrySpawn(prototype, new MapCoordinates(new Vector2(-48000, -48000), MapData.MapId), out high, pinned: true, tier: 4), Is.True);
        });
        // Power comes up and the fire control server takes the guns.
        await RunTicks(60);
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFReaverPatrol");
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
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFReaverStronghold");
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
