#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._Mono.FireControl;
using Content.Server._Mono.ShipRepair;
using Content.Server._WF.ShipRepair;
using Content.Server._WF.Shipyard;
using Content.Server.Atmos;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Decals;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._WF.ShipRepair;
using Content.Shared.Atmos;
using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Power.EntitySystems;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.ShipRepair;

/// <summary>One repair snapshot per broken hull, the SRD's rebuild guard, and reattaching sections.</summary>
[TestFixture]
[TestOf(typeof(WFHullSectionSystem))]
public sealed class HullSectionTest
{
    /// <summary>Where the test hull sits on the test map, clear of the map's own one-tile grid.</summary>
    private static readonly Vector2 HullOrigin = new(20f, 20f);

    /// <summary>The column cut out of the 15x15 test hull; the four columns past it become the section.</summary>
    private const int SeamColumn = 10;

    /// <summary>Speeds the SRD's do-afters up so a reattach takes about a second at most.</summary>
    private const float QuickTool = 0.03f;

    [Test]
    public void ReattachTimeGrowsWithTheSectionAndIsCapped()
    {
        Assert.That(WFHullSectionSystem.ReattachTime(0), Is.EqualTo(5f));
        Assert.That(WFHullSectionSystem.ReattachTime(60), Is.EqualTo(11f).Within(0.001f));
        Assert.That(WFHullSectionSystem.ReattachTime(10000), Is.EqualTo(30f));
    }

    /// <summary>The biggest piece keeps the snapshot; a section, and a section of a section, only link back to it.</summary>
    [Test]
    public async Task SplitKeepsOneSnapshotAndLinksEverySection()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var (hull, section) = await BuildSplitHull(pair, map.MapId);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<ShipRepairDataComponent>(hull).Chunks, Is.Not.Empty);
            Assert.That(em.HasComponent<ShipRepairDataComponent>(section), Is.False, "The section carries its own copy of the blueprint.");

            Cut(pair, section, Enumerable.Range(SeamColumn + 1, 4).Select(x => new Vector2i(x, 7)));
            var sections = SectionsOf(em, hull);
            Assert.That(sections, Has.Count.EqualTo(2), "A piece of the section did not link back to the hull.");
            Assert.That(sections.Any(uid => em.HasComponent<ShipRepairDataComponent>(uid)), Is.False,
                "A piece of the section carries its own copy of the blueprint.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A deleted hull hands its snapshot to its largest section on the same map; scrap below a quarter of the blueprint is wreckage.</summary>
    [Test]
    public async Task DeletingTheHullPromotesItsLargestSection()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var (hull, large) = await BuildSplitHull(pair, map.MapId);

        await server.WaitAssertion(() =>
        {
            var chunks = em.GetComponent<ShipRepairDataComponent>(hull).Chunks.Count;
            // The top four rows of the hull's side make a second, smaller section.
            Cut(pair, hull, Enumerable.Range(0, SeamColumn).Select(x => new Vector2i(x, 10)));
            var small = SectionsOf(em, hull).Single(uid => uid != large);

            em.DeleteEntity(hull);

            Assert.That(em.TryGetComponent<ShipRepairDataComponent>(large, out var inherited), Is.True,
                "The largest section did not take over the blueprint.");
            Assert.That(inherited!.Chunks, Has.Count.EqualTo(chunks));
            Assert.That(em.HasComponent<WFHullSectionComponent>(large), Is.False, "The new hull still links to the old one.");
            Assert.That(em.GetComponent<WFHullSectionComponent>(small).Hull, Is.EqualTo(large), "The other section was not repointed.");

            // With its new hull gone, the last piece holds under a quarter of the blueprint: plain wreckage.
            em.DeleteEntity(large);

            Assert.That(em.HasComponent<WFHullSectionComponent>(small), Is.False, "A section with no hull left is still linked.");
            Assert.That(em.HasComponent<ShipRepairDataComponent>(small), Is.False, "A scrap section took over the blueprint.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Selling the hull, or deleting it with its section on another map, leaves the section as wreckage.</summary>
    [Test]
    public async Task ASoldOrDistantHullLeavesItsSectionAsWreckage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var (hull, section) = await BuildSplitHull(pair, map.MapId);

        await server.WaitAssertion(() =>
        {
            var sold = new ShipSoldEvent(hull, EntityUid.Invalid, EntityUid.Invalid, 0);
            em.EventBus.RaiseEvent(EventSource.Local, ref sold);
            em.DeleteEntity(hull);

            Assert.That(em.HasComponent<ShipRepairDataComponent>(section), Is.False, "A piece left behind took over a sold ship's blueprint.");
            Assert.That(em.HasComponent<WFHullSectionComponent>(section), Is.False, "A piece of a sold ship is still linked.");
        });

        var elsewhere = EntityUid.Invalid;
        var elsewhereId = MapId.Nullspace;
        await server.WaitPost(() => elsewhere = server.System<SharedMapSystem>().CreateMap(out elsewhereId));
        var (farHull, farSection) = await BuildSplitHull(pair, elsewhereId);

        await server.WaitAssertion(() =>
        {
            server.System<SharedTransformSystem>().SetCoordinates(farSection, new EntityCoordinates(map.MapUid, new Vector2(200f, 200f)));
            em.DeleteEntity(farHull);

            Assert.That(em.HasComponent<ShipRepairDataComponent>(farSection), Is.False, "A section on another map took over the blueprint.");
            Assert.That(em.HasComponent<WFHullSectionComponent>(farSection), Is.False, "A section with no hull left is still linked.");
            em.DeleteEntity(elsewhere);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The SRD rebuilds the hull except where a nearby section belongs or another grid has moved in.</summary>
    [Test]
    public async Task RebuildLeavesRoomForSectionsAndOtherGrids()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();
        var (hull, section) = await BuildSplitHull(pair, map.MapId);
        var debris = await BuildDebris(pair, map.MapId, 3, new Vector2(-40f, -40f));
        var seam = new Vector2i(SeamColumn, 5);
        var reserved = new Vector2i(SeamColumn + 1, 5);
        var free = new Vector2i(3, 3);
        var other = new Vector2i(4, 3);

        await server.WaitPost(() =>
        {
            Hold(pair, hull, section, debris);
            Cut(pair, hull, new[] { free, other });
            xforms.SetWorldPosition(section, xforms.GetWorldPosition(section) + new Vector2(5f, 0f));
        });
        var (user, tool) = await Worker(pair, () => new EntityCoordinates(hull, new Vector2(5.5f, 9.5f)));

        // The seam is nobody's place, so it rebuilds.
        await server.WaitPost(() => Click(em, user, tool, TileCentre(hull, seam)));
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitAssertion(() => Assert.That(HasTile(pair, hull, seam), Is.True, "The SRD no longer rebuilds a clear tile."));

        // The section sits five tiles off its place: its tiles are reserved.
        await server.WaitPost(() => Click(em, user, tool, TileCentre(hull, reserved)));
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitAssertion(() => Assert.That(HasTile(pair, hull, reserved), Is.False,
            "The SRD rebuilt the hull where a detached section belongs."));

        // Out of reattach range the section reserves nothing.
        await server.WaitPost(() =>
        {
            xforms.SetWorldPosition(section, xforms.GetWorldPosition(section) + new Vector2(100f, 0f));
            Click(em, user, tool, TileCentre(hull, reserved));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitAssertion(() => Assert.That(HasTile(pair, hull, reserved), Is.True,
            "A section out of range still reserved its place."));

        // A grid that moves over the tile while the repair runs stops it.
        await server.WaitPost(() =>
        {
            Click(em, user, tool, TileCentre(hull, free));
            var spot = Vector2.Transform(maps.TileCenterToVector((hull, em.GetComponent<MapGridComponent>(hull)), free), xforms.GetWorldMatrix(hull));
            xforms.SetWorldPosition(debris, spot - new Vector2(1.5f, 1.5f));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitAssertion(() => Assert.That(HasTile(pair, hull, free), Is.False,
            "The SRD rebuilt a tile under another grid."));

        // Clear of other grids and sections, both tiles rebuild.
        await server.WaitPost(() =>
        {
            xforms.SetWorldPosition(debris, new Vector2(-40f, -40f));
            Click(em, user, tool, TileCentre(hull, free));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitPost(() => Click(em, user, tool, TileCentre(hull, other)));
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitAssertion(() =>
        {
            Assert.That(HasTile(pair, hull, free), Is.True, "The SRD no longer rebuilds a clear tile.");
            Assert.That(HasTile(pair, hull, other), Is.True, "The SRD no longer rebuilds a clear tile.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A snapshot fixture carried to another grid still exists, so the SRD does not make a second one; on a section
    /// broken off the hull it is debris, which the SRD rebuilds on the hull and takes away, so there is still only one.
    /// </summary>
    [Test]
    public async Task TheSrdDoesNotCopyWhatWasCarriedOff()
    {
        const string Fixture = "Table";

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();
        var spot = new Vector2i(3, 3);
        var table = EntityUid.Invalid;
        var (hull, section) = await BuildSplitHull(pair, map.MapId, built => table = em.SpawnEntity(Fixture, TileCentre(built, spot)));
        var debris = await BuildDebris(pair, map.MapId, 3, new Vector2(-40f, -40f));

        await server.WaitPost(() => Hold(pair, hull, section, debris));
        var (user, tool) = await Worker(pair, () => new EntityCoordinates(hull, new Vector2(5.5f, 5.5f)));

        int FixturesAt(EntityUid grid, Vector2i index)
        {
            return maps.GetAnchoredEntities(grid, em.GetComponent<MapGridComponent>(grid), index)
                .Count(uid => em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == Fixture);
        }

        void Carry(EntityUid grid, Vector2i index)
        {
            xforms.Unanchor(table);
            xforms.SetCoordinates(table, TileCentre(grid, index));
            xforms.AnchorEntity(table);
        }

        await server.WaitAssertion(() =>
            Assert.That(FixturesAt(hull, spot), Is.EqualTo(1), "Precondition: the fixture is not on the hull."));

        // Unbolted and bolted down again on another ship.
        await server.WaitPost(() =>
        {
            Carry(debris, new Vector2i(1, 1));
            Click(em, user, tool, TileCentre(hull, spot));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<TransformComponent>(table).GridUid, Is.EqualTo(debris), "Precondition: the fixture is not on the other grid.");
            Assert.That(FixturesAt(hull, spot), Is.Zero, "The SRD built a second fixture while the first stands on another grid.");
        });

        // On a section broken off this hull it is debris.
        await server.WaitPost(() =>
        {
            var index = maps.GetAllTiles(section, em.GetComponent<MapGridComponent>(section)).First().GridIndices;
            Carry(section, index);
            xforms.SetWorldPosition(section, xforms.GetWorldPosition(section) + new Vector2(100f, 0f));
            Click(em, user, tool, TileCentre(hull, spot));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            Assert.That(FixturesAt(hull, spot), Is.EqualTo(1), "The SRD no longer rebuilds what was left on a broken-off section.");
            Assert.That(em.Deleted(table), Is.True, "The SRD rebuilt a fixture and left the original usable on the section.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A fixture torn off onto planet ground is rebuilt on the hull once: the one on the ground goes.</summary>
    [Test]
    public async Task AFixtureLeftOnPlanetGroundIsRebuiltOnce()
    {
        const string Fixture = "Table";

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var xforms = server.System<SharedTransformSystem>();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-32, -32), new Vector2i(64, 64));
        var hull = await BuildHull(pair, em.GetComponent<MapComponent>(ground).MapId);
        await MapInitHull(pair, hull);
        var spot = new Vector2i(3, 3);
        var table = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            table = em.SpawnEntity(Fixture, TileCentre(hull, spot));
            server.System<ShipRepairSystem>().GenerateRepairData(hull);
        });
        var (user, tool) = await Worker(pair, () => new EntityCoordinates(hull, new Vector2(5.5f, 5.5f)));

        await server.WaitPost(() =>
        {
            // Off the hull and onto the ground beside it, as a tile bitten out from under it leaves it.
            var beside = Vector2.Transform(new Vector2(-3.5f, 3.5f), xforms.GetWorldMatrix(hull));
            xforms.Unanchor(table);
            xforms.SetCoordinates(table, new EntityCoordinates(ground, beside));
            Assert.That(em.GetComponent<TransformComponent>(table).GridUid, Is.EqualTo(ground), "Precondition: the fixture is not on planet ground.");
            Click(em, user, tool, TileCentre(hull, spot));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var count = maps.GetAnchoredEntities(hull, em.GetComponent<MapGridComponent>(hull), spot)
                .Count(uid => em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == Fixture);
            Assert.That(count, Is.EqualTo(1), "The SRD did not rebuild a fixture whose original lies on planet ground.");
            Assert.That(em.Deleted(table), Is.True, "The SRD rebuilt a fixture and left the original on the ground.");
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>With grid splitting on, the SRD won't lay a tile that no hull tile joins, so a hole fills from its edge.</summary>
    [Test]
    public async Task RebuildStartsFromTheHullsEdge()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var (hull, _) = await BuildSplitHull(pair, map.MapId);
        var middle = new Vector2i(4, 4);
        var edge = new Vector2i(3, 4);

        await server.WaitPost(() =>
        {
            Hold(pair, hull);
            Cut(pair, hull, Enumerable.Range(3, 3).SelectMany(x => Enumerable.Range(3, 3).Select(y => new Vector2i(x, y))));
        });
        var (user, tool) = await Worker(pair, () => new EntityCoordinates(hull, new Vector2(5.5f, 9.5f)));

        // The middle of the hole first: it would join nothing, so no repair starts.
        await server.WaitAssertion(() =>
        {
            server.System<SharedChargesSystem>().AddCharges(tool, 10000);
            Click(em, user, tool, TileCentre(hull, middle));
            Assert.That(em.GetComponent<ShipRepairToolComponent>(tool).DoAfters, Is.Empty, "The SRD started a repair in the middle of a hole.");
        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitAssertion(() =>
        {
            Assert.That(HasTile(pair, hull, middle), Is.False, "The SRD laid a tile in the middle of a hole.");
            Assert.That(SectionsOf(em, hull), Has.Count.EqualTo(1), "A rebuilt tile broke off as a section.");
        });

        // From the edge it rebuilds.
        await server.WaitPost(() => Click(em, user, tool, TileCentre(hull, edge)));
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitAssertion(() => Assert.That(HasTile(pair, hull, edge), Is.True, "The SRD no longer rebuilds from the edge."));

        // The tile beside it going while the repair runs stops it.
        await server.WaitPost(() =>
        {
            Click(em, user, tool, TileCentre(hull, middle));
            Cut(pair, hull, new[] { edge });
        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitAssertion(() =>
        {
            Assert.That(HasTile(pair, hull, middle), Is.False, "The SRD finished a tile whose neighbour went during the repair.");
            Assert.That(SectionsOf(em, hull), Has.Count.EqualTo(1), "A rebuilt tile broke off as a section.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A reattach needs a full SRD, the section in range and its place clear; the charge is only spent on success.</summary>
    [Test]
    public async Task ReattachNeedsAFullChargeRangeAndAClearPlace()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var charges = server.System<SharedChargesSystem>();
        var map = await pair.CreateTestMap();
        var (hull, section) = await BuildSplitHull(pair, map.MapId);
        var debris = await BuildDebris(pair, map.MapId, 3, new Vector2(-40f, -40f));
        var home = new Vector2i(SeamColumn + 1, 5);
        var start = Vector2.Zero;

        await server.WaitPost(() =>
        {
            // Every step but one lets the section back, so each refusal has a single cause.
            Hold(pair, hull, section, debris);
            FillSeam(pair, hull);
            start = xforms.GetWorldPosition(section);
            xforms.SetWorldPosition(section, start + new Vector2(3f, 0f));
        });
        var (user, tool) = await Worker(pair, () => new EntityCoordinates(hull, new Vector2(5.5f, 9.5f)));

        // Below a full charge.
        var before = 0;
        await server.WaitPost(() =>
        {
            before = em.GetComponent<LimitedChargesComponent>(tool).Charges;
            Assert.That(before, Is.LessThan(em.GetComponent<LimitedChargesComponent>(tool).MaxCharges));
            Click(em, user, tool, TileCentre(section, home));
        });
        await AssertStillDetached(pair, section, tool, before, "below a full charge");

        // Out of range.
        await server.WaitPost(() =>
        {
            charges.AddCharges(tool, 10000);
            before = em.GetComponent<LimitedChargesComponent>(tool).Charges;
            xforms.SetWorldPosition(section, start + new Vector2(100f, 0f));
            Click(em, user, tool, TileCentre(section, home));
        });
        await AssertStillDetached(pair, section, tool, before, "out of range");

        // With the seam gone, nothing on the hull would join it.
        await server.WaitPost(() =>
        {
            xforms.SetWorldPosition(section, start + new Vector2(3f, 0f));
            Cut(pair, hull, Enumerable.Range(0, 15).Select(y => new Vector2i(SeamColumn, y)));
            Click(em, user, tool, TileCentre(section, home));
        });
        await AssertStillDetached(pair, section, tool, before, "apart from the hull");

        // A hull tile rebuilt in its place.
        await server.WaitPost(() =>
        {
            FillSeam(pair, hull);
            var grid = em.GetComponent<MapGridComponent>(hull);
            maps.SetTile(hull, grid, home, maps.GetTileRef(hull, grid, new Vector2i(5, 5)).Tile);
            Click(em, user, tool, TileCentre(section, home));
        });
        await AssertStillDetached(pair, section, tool, before, "onto a hull tile");

        // Another grid in its place.
        await server.WaitPost(() =>
        {
            Cut(pair, hull, new[] { home });
            var spot = Vector2.Transform(new Vector2(12.5f, 8.5f), xforms.GetWorldMatrix(hull));
            xforms.SetWorldPosition(debris, spot - new Vector2(1.5f, 1.5f));
            Click(em, user, tool, TileCentre(section, home));
        });
        await AssertStillDetached(pair, section, tool, before, "onto another grid");

        // All clear: the section goes back and the SRD is emptied.
        await server.WaitPost(() =>
        {
            xforms.SetWorldPosition(debris, new Vector2(-40f, -40f));
            Click(em, user, tool, TileCentre(section, home));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1.5f));
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(section), Is.False, "The section was not reattached.");
            Assert.That(em.GetComponent<LimitedChargesComponent>(tool).Charges, Is.Zero, "A reattach did not use the whole charge.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A reattach puts the section's tiles back at their indices, with its machines, loose items, crew and decals.</summary>
    [Test]
    public async Task ReattachPutsTheSectionAndEverythingAboardBack()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var xforms = server.System<SharedTransformSystem>();
        var decals = server.System<DecalSystem>();
        var map = await pair.CreateTestMap();
        var decalSpot = new Vector2(12.5f, 6.5f);
        var aboard = new Dictionary<EntityUid, Vector2>();
        var tiles = new Dictionary<Vector2i, Tile>();
        var drive = EntityUid.Invalid;
        var engine = EntityUid.Invalid;
        var stray = EntityUid.Invalid;
        var strayWorld = Vector2.Zero;

        var (hull, section) = await BuildSplitHull(pair, map.MapId, grid =>
        {
            var comp = em.GetComponent<MapGridComponent>(grid);
            for (var x = SeamColumn + 1; x < 15; x++)
            for (var y = 0; y < 15; y++)
                tiles[new Vector2i(x, y)] = server.System<SharedMapSystem>().GetTileRef(grid, comp, new Vector2i(x, y)).Tile;

            foreach (var uid in Children(em, grid))
            {
                var proto = em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                var local = em.GetComponent<TransformComponent>(uid).LocalPosition;
                if (proto == "MachineFTLDrive")
                    drive = uid;
                else if (proto == "DebugThruster" && local == new Vector2(13.5f, 1.5f))
                    engine = uid;
            }

            aboard[em.SpawnEntity("Crowbar", new EntityCoordinates(grid, new Vector2(12.3f, 4.7f)))] = new Vector2(12.3f, 4.7f);
            aboard[em.SpawnEntity("MobHuman", new EntityCoordinates(grid, new Vector2(13.5f, 10.5f)))] = new Vector2(13.5f, 10.5f);
            aboard[drive] = new Vector2(11.5f, 2.5f);
            aboard[engine] = new Vector2(13.5f, 1.5f);
            Assert.That(decals.TryAddDecal("Arrows", new EntityCoordinates(grid, decalSpot), out _), Is.True);
        });

        await server.WaitPost(() =>
        {
            Hold(pair, hull, section);
            FillSeam(pair, hull);
            Assert.That(aboard.Keys.All(uid => em.GetComponent<TransformComponent>(uid).ParentUid == section),
                "Not everything aboard went with the section.");
            Assert.That(DecalsAt(decals, section, decalSpot), Is.EqualTo(1), "The decal did not go with the section.");
            // Off its place and turned, clear of the hull.
            xforms.SetWorldPosition(section, xforms.GetWorldPosition(section) + new Vector2(10f, -6f));
            xforms.SetWorldRotation(section, Angle.FromDegrees(30));

            // Something held to the section off its tiles, as a sound playing at its centre is.
            stray = em.SpawnEntity(null, MapCoordinates.Nullspace);
            em.GetComponent<TransformComponent>(stray).GridTraversal = false;
            xforms.SetCoordinates(stray, new EntityCoordinates(section, new Vector2(16.5f, 7.5f)));
        });
        var (user, tool) = await Worker(pair, () => new EntityCoordinates(hull, new Vector2(5.5f, 9.5f)));

        await server.WaitPost(() =>
        {
            Assert.That(em.GetComponent<TransformComponent>(stray).ParentUid, Is.EqualTo(section));
            strayWorld = xforms.GetWorldPosition(stray);
            server.System<SharedChargesSystem>().AddCharges(tool, 10000);
            Click(em, user, tool, TileCentre(section, new Vector2i(12, 5)));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1.5f));

        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(section), Is.False, "The section was not reattached.");
            var maps = server.System<SharedMapSystem>();
            var grid = em.GetComponent<MapGridComponent>(hull);
            foreach (var (index, tile) in tiles)
            {
                Assert.That(maps.GetTileRef(hull, grid, index).Tile.TypeId, Is.EqualTo(tile.TypeId), $"Tile {index} did not come back.");
            }

            foreach (var (uid, local) in aboard)
            {
                var xform = em.GetComponent<TransformComponent>(uid);
                Assert.That(xform.ParentUid, Is.EqualTo(hull), $"{em.ToPrettyString(uid)} did not come back aboard the hull.");
                Assert.That(Vector2.Distance(xform.LocalPosition, local), Is.LessThan(0.05f), $"{em.ToPrettyString(uid)} is not back in its place.");
            }

            Assert.That(em.GetComponent<TransformComponent>(drive).Anchored, Is.True, "The FTL drive came back unanchored.");
            var shuttle = em.GetComponent<ShuttleComponent>(hull);
            Assert.That(shuttle.LinearThrusters.Sum(bank => bank.Count(uid => uid == engine)), Is.EqualTo(1),
                "The section's engine is not in the hull's thrust banks exactly once.");
            Assert.That(DecalsAt(decals, hull, decalSpot), Is.EqualTo(1), "The section's decal was lost.");
            Assert.That(em.EntityExists(stray), Is.True, "Something held to the section off its tiles was deleted with it.");
            Assert.That(Vector2.Distance(xforms.GetWorldPosition(stray), strayWorld), Is.LessThan(0.05f),
                "Something off the section's tiles was moved.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Crew and items aboard a drifting section come back at rest on the hull, not flung across it.</summary>
    [Test]
    public async Task ReattachLeavesThingsAboardAtRest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var physics = server.System<SharedPhysicsSystem>();
        var map = await pair.CreateTestMap();
        var (hull, section) = await BuildSplitHull(pair, map.MapId);

        await server.WaitAssertion(() =>
        {
            Hold(pair, hull);
            FillSeam(pair, hull);
            var aboard = new[]
            {
                em.SpawnEntity("MobHuman", new EntityCoordinates(section, new Vector2(13.5f, 10.5f))),
                em.SpawnEntity("Crowbar", new EntityCoordinates(section, new Vector2(12.3f, 4.7f))),
            };

            physics.SetBodyType(section, BodyType.Dynamic);
            physics.SetLinearVelocity(section, new Vector2(1.5f, 0f));
            physics.SetAngularVelocity(section, 0.2f);
            server.System<WFHullSectionServerSystem>().Reattach(hull, section);

            foreach (var uid in aboard)
            {
                Assert.That(em.GetComponent<TransformComponent>(uid).ParentUid, Is.EqualTo(hull), $"{em.ToPrettyString(uid)} did not come back aboard.");
                Assert.That(em.GetComponent<PhysicsComponent>(uid).LinearVelocity.Length(), Is.LessThan(0.05f),
                    $"{em.ToPrettyString(uid)} kept the section's drift aboard the hull.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A gun on a section answers to the hull's gunnery server again once the section is back.</summary>
    [Test]
    public async Task ReattachSignsTheSectionsGunsBackOn()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.GridSplitting, true));
        var hull = await BuildHull(pair, map.MapId, HullOrigin);
        var gunnery = EntityUid.Invalid;
        var gun = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            gunnery = em.SpawnEntity("GunneryServerLow", new EntityCoordinates(hull, new Vector2(4.5f, 11.5f)));
            gun = em.SpawnEntity("ShuttleGunKinetic", new EntityCoordinates(hull, new Vector2(12.5f, 11.5f)));
            foreach (var uid in new[] { gunnery, gun })
            {
                Assert.That(em.GetComponent<TransformComponent>(uid).Anchored, Is.True, $"{em.ToPrettyString(uid)} spawned loose.");
                server.System<SharedPowerReceiverSystem>().SetNeedsPower(uid, false);
            }
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var controlled = em.GetComponent<FireControlServerComponent>(gunnery).Controlled;
            Assert.That(controlled, Does.Contain(gun), "The gunnery server never took the gun.");

            server.System<ShipRepairSystem>().GenerateRepairData(hull);
            Cut(pair, hull, Enumerable.Range(0, 15).Select(y => new Vector2i(SeamColumn, y)));
            var section = SectionsOf(em, hull).Single();
            Assert.That(controlled, Does.Not.Contain(gun), "The gun stayed on the gunnery server when its section broke off.");

            FillSeam(pair, hull);
            server.System<WFHullSectionServerSystem>().Reattach(hull, section);
            Assert.That(em.GetComponent<TransformComponent>(gun).ParentUid, Is.EqualTo(hull));
            Assert.That(controlled, Does.Contain(gun), "The gun is not on the gunnery server after the reattach.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Rooms on a section keep their air when it goes back on the hull.</summary>
    [Test]
    public async Task ReattachCarriesTheSectionAir()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var (hull, section) = await BuildSplitHull(pair, map.MapId);
        var index = new Vector2i(12, 5);

        // Atmos gives the section's tiles their mixtures.
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        await server.WaitAssertion(() =>
        {
            var atmos = server.System<AtmosphereSystem>();
            var mixture = atmos.GetTileMixture(section, null, index, true);
            Assert.That(mixture is { Immutable: false }, "The section has no air of its own on the test tile.");
            mixture!.AdjustMoles(Gas.Nitrogen, 100f);
            var moles = mixture.TotalMoles;

            FillSeam(pair, hull);
            server.System<WFHullSectionServerSystem>().Reattach(hull, section);

            Assert.That(atmos.GetTileMixture(hull, null, index)?.TotalMoles ?? 0f, Is.EqualTo(moles).Within(0.01f),
                "The section's air did not come back with it.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Tiles trimmed off a split hull's atmosphere leave no adjacency behind, before or after a reattach.</summary>
    [Test]
    public async Task SplitAndReattachLeaveNoStaleAtmosAdjacency()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var index = new Vector2i(12, 5);

        // Planet-like map air: equalization walks through map tiles instead of stopping at space.
        await server.WaitPost(() =>
        {
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            mixture.AdjustMoles(Gas.Nitrogen, Atmospherics.MolesCellStandard);
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, mixture);
        });

        var (hull, section) = await BuildSplitHull(pair, map.MapId);

        // The hull revalidates and trims the section's tiles within a few atmos cycles.
        var trimmed = false;
        for (var i = 0; i < 40 && !trimmed; i++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(0.25f));
            await server.WaitPost(() => trimmed = !HullAtmosTiles(pair, hull).ContainsKey(index));
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(trimmed, "The hull never trimmed the section's tiles.");
            AssertAdjacencyIsLive(HullAtmosTiles(pair, hull), "after the split");

            FillSeam(pair, hull);
            server.System<WFHullSectionServerSystem>().Reattach(hull, section);
            Assert.That(HasTile(pair, hull, index), "The section was not reattached.");
            AssertAdjacencyIsLive(HullAtmosTiles(pair, hull), "straight after the reattach");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(2f));
        await server.WaitAssertion(() => AssertAdjacencyIsLive(HullAtmosTiles(pair, hull), "once the reattach settled"));

        await pair.CleanReturnAsync();
    }

    /// <summary>Standing on planet ground, the SRD works on the hull beside the click rather than on the ground map.</summary>
    [Test]
    public async Task SrdUsedFromPlanetGroundReachesTheHull()
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
        var edge = new Vector2i(0, 5);

        await server.WaitPost(() =>
        {
            server.System<ShipRepairSystem>().GenerateRepairData(hull);
            Cut(pair, hull, new[] { edge });
        });
        var (user, tool) = await Worker(pair, () =>
        {
            var world = Vector2.Transform(new Vector2(-1.5f, 5.5f), server.System<SharedTransformSystem>().GetWorldMatrix(hull));
            return new EntityCoordinates(ground, world);
        }, 1f);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<TransformComponent>(user).GridUid, Is.EqualTo(ground), "The crew member is not standing on planet ground.");
            Click(em, user, tool, TileCentre(hull, edge));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(1.5f));
        await server.WaitAssertion(() => Assert.That(HasTile(pair, hull, edge), Is.True,
            "The SRD used from planet ground did not rebuild the hull."));

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Standing on a section, a click into the gap beside the hull rebuilds the hull instead of reattaching.</summary>
    [Test]
    public async Task SrdUsedFromASectionReachesTheHullBesideTheClick()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var (hull, section) = await BuildSplitHull(pair, map.MapId);
        var seam = new Vector2i(SeamColumn, 5);

        await server.WaitPost(() => Hold(pair, hull, section));
        var (user, tool) = await Worker(pair, () => new EntityCoordinates(section, new Vector2(12.5f, 9.5f)));

        await server.WaitPost(() =>
        {
            Assert.That(em.GetComponent<TransformComponent>(user).GridUid, Is.EqualTo(section), "The crew member is not standing on the section.");
            Click(em, user, tool, TileCentre(hull, seam));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitAssertion(() => Assert.That(HasTile(pair, hull, seam), Is.True,
            "A click beside the hull from a section did not rebuild the hull."));

        await pair.CleanReturnAsync();
    }

    /// <summary>Builds the test hull, snapshots it and cuts the seam column; returns the hull and its one section.</summary>
    private static async Task<(EntityUid Hull, EntityUid Section)> BuildSplitHull(TestPair pair, MapId map, Action<EntityUid>? beforeCut = null)
    {
        var server = pair.Server;
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.GridSplitting, true));
        var hull = await BuildHull(pair, map, HullOrigin);
        var section = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            beforeCut?.Invoke(hull);
            server.System<ShipRepairSystem>().GenerateRepairData(hull);
            Cut(pair, hull, Enumerable.Range(0, 15).Select(y => new Vector2i(SeamColumn, y)));
            var sections = SectionsOf(server.EntMan, hull);
            Assert.That(sections, Has.Count.EqualTo(1), "Cutting the seam did not leave one section linked to the hull.");
            section = sections[0];
        });

        return (hull, section);
    }

    /// <summary>Removes tiles; a cut across the grid splits it.</summary>
    private static void Cut(TestPair pair, EntityUid grid, IEnumerable<Vector2i> tiles)
    {
        var comp = pair.Server.EntMan.GetComponent<MapGridComponent>(grid);
        pair.Server.System<SharedMapSystem>().SetTiles(grid, comp, tiles.Select(tile => (tile, Tile.Empty)).ToList());
    }

    /// <summary>Rebuilds the hull's side of the seam, so the section's place joins the hull again.</summary>
    private static void FillSeam(TestPair pair, EntityUid hull)
    {
        var maps = pair.Server.System<SharedMapSystem>();
        var grid = pair.Server.EntMan.GetComponent<MapGridComponent>(hull);
        var floor = maps.GetTileRef(hull, grid, new Vector2i(SeamColumn - 1, 0)).Tile;
        maps.SetTiles(hull, grid, Enumerable.Range(0, 15).Select(y => (new Vector2i(SeamColumn, y), floor)).ToList());
    }

    /// <summary>Makes grids static, so nothing pushes them apart while a test lines them up.</summary>
    private static void Hold(TestPair pair, params EntityUid[] grids)
    {
        foreach (var grid in grids)
        {
            pair.Server.System<SharedPhysicsSystem>().SetBodyType(grid, BodyType.Static);
        }
    }

    /// <summary>Every grid linked to a hull.</summary>
    private static List<EntityUid> SectionsOf(IEntityManager entMan, EntityUid hull)
    {
        var found = new List<EntityUid>();
        var query = entMan.EntityQueryEnumerator<WFHullSectionComponent>();
        while (query.MoveNext(out var uid, out var section))
        {
            if (section.Hull == hull)
                found.Add(uid);
        }

        return found;
    }

    /// <summary>Spawns a crew member holding an SRD, its do-afters scaled by the multiplier.</summary>
    private static async Task<(EntityUid User, EntityUid Tool)> Worker(TestPair pair, Func<EntityCoordinates> at, float multiplier = QuickTool)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var user = EntityUid.Invalid;
        var tool = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var coords = at();
            user = entMan.SpawnEntity("MobHuman", coords);
            tool = entMan.SpawnEntity("ShipRepairDevice", coords);
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(user, tool), Is.True);
            entMan.GetComponent<ShipRepairToolComponent>(tool).RepairTimeMultiplier = multiplier;
        });

        await server.WaitRunTicks(1);
        return (user, tool);
    }

    /// <summary>Uses the SRD at a spot, as a click within reach does.</summary>
    private static void Click(IEntityManager entMan, EntityUid user, EntityUid tool, EntityCoordinates at)
    {
        entMan.EventBus.RaiseLocalEvent(tool, new AfterInteractEvent(user, tool, null, at, true));
    }

    /// <summary>The centre of a grid tile, in that grid's coordinates.</summary>
    private static EntityCoordinates TileCentre(EntityUid grid, Vector2i index)
    {
        return new EntityCoordinates(grid, new Vector2(index.X + 0.5f, index.Y + 0.5f));
    }

    private static Dictionary<Vector2i, TileAtmosphere> HullAtmosTiles(TestPair pair, EntityUid hull)
    {
        return pair.Server.EntMan.GetComponent<GridAtmosphereComponent>(hull).Tiles;
    }

    /// <summary>Fails if a tile links to one that left the grid atmosphere, or the two disagree about the link.</summary>
    private static void AssertAdjacencyIsLive(Dictionary<Vector2i, TileAtmosphere> tiles, string when)
    {
        foreach (var (index, tile) in tiles)
        {
            var adjacentTiles = tile.AdjacentTiles;
            for (var i = 0; i < Atmospherics.Directions; i++)
            {
                if (adjacentTiles[i] is not { } adjacent)
                    continue;

                var direction = (AtmosDirection) (1 << i);
                Assert.That(tiles.GetValueOrDefault(index.Offset(direction)), Is.SameAs(adjacent),
                    $"Tile {index} links {direction} to a tile no longer in the grid atmosphere, {when}.");
                Assert.That(adjacent.AdjacentBits.IsFlagSet(direction.GetOpposite()),
                    $"Tile {index} links {direction} but its neighbour does not link back, {when}.");
            }
        }
    }

    private static bool HasTile(TestPair pair, EntityUid grid, Vector2i index)
    {
        var comp = pair.Server.EntMan.GetComponent<MapGridComponent>(grid);
        return !pair.Server.System<SharedMapSystem>().GetTileRef(grid, comp, index).Tile.IsEmpty;
    }

    private static int DecalsAt(DecalSystem decals, EntityUid grid, Vector2 spot)
    {
        return decals.GetDecalsIntersecting(grid, Box2.CenteredAround(spot, new Vector2(0.5f, 0.5f))).Count;
    }

    /// <summary>Waits out a refused reattach and checks the section and the charge are untouched.</summary>
    private static async Task AssertStillDetached(TestPair pair, EntityUid section, EntityUid tool, int charges, string why)
    {
        var server = pair.Server;
        await server.WaitRunTicks(pair.SecondsToTicks(1.5f));
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.EntityExists(section), Is.True, $"The section was reattached {why}.");
            Assert.That(server.EntMan.GetComponent<LimitedChargesComponent>(tool).Charges, Is.EqualTo(charges), $"A refused reattach {why} used charges.");
        });
    }
}
