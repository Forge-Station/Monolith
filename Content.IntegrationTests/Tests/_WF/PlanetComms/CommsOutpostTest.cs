#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.PlanetComms;
using Content.Server._WF.PlanetPois;
using Content.Server.Construction;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Shared._WF.PlanetPois;
using Content.Shared.NodeContainer;
using Robust.Server.Physics;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.PlanetComms;

/// <summary>Every world's site table places one comms outpost, and the outpost holds a mass transceiver.</summary>
[TestFixture]
public sealed class CommsOutpostTest
{
    private const string OutpostPath = "/Maps/_WF/PlanetComms/comms_outpost.yml";

    private static readonly string[] Tables =
    {
        "WFPoisAsclepiu", "WFPoisMerak", "WFPoisFervidus", "WFPoisThrascias", "WFPoisAerumna", "WFPoisCarcinoma",
    };

    [Test]
    public async Task EveryWorldGetsOneOutpostWithATransceiver()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            foreach (var id in Tables)
            {
                var outposts = prototypes.Index<WFPlanetPoiTablePrototype>(id).Entries.Where(entry => entry.MapPath == OutpostPath).ToList();
                Assert.That(outposts, Has.Count.EqualTo(1), $"{id} has no comms outpost entry.");
                Assert.That(outposts[0].MinCount, Is.EqualTo(1), $"{id} doesn't always place its comms outpost.");
            }

            var maps = server.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(mapId, new ResPath(OutpostPath), out var grid), Is.True);
            var transceivers = server.EntMan.EntityQuery<MetaDataComponent>()
                .Count(meta => meta.EntityPrototype?.ID.StartsWith("WFMassTransceiver") == true
                               && server.EntMan.GetComponent<TransformComponent>(meta.Owner).GridUid == grid!.Value.Owner);
            Assert.That(transceivers, Is.EqualTo(1), "The outpost should hold one mass transceiver.");
            maps.DeleteMap(mapId);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The outpost powers itself at any turn: RTGs feed the substation, and a repaired transceiver and the rest of the site run.</summary>
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task TheRepairedOutpostRunsOffItsOwnRtgs(int turns)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var target = EntityUid.Invalid;
        var sourceMap = MapId.Nullspace;

        await server.WaitPost(() =>
        {
            maps.CreateMap(out sourceMap, runMapInit: false);
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(sourceMap, new ResPath(OutpostPath), out var loaded), Is.True);
            var source = loaded!.Value;

            target = maps.CreateMap(out var targetMap, runMapInit: false);
            var grid = entMan.AddComponent<MapGridComponent>(target);
            server.System<SharedTransformSystem>().SetCoordinates(source.Owner, new EntityCoordinates(target, Vector2.Zero));
            server.System<GridFixtureSystem>().Merge(target, source.Owner, Vector2i.Zero, Angle.FromDegrees(90 * turns), grid, source.Comp);
            var merged = new List<EntityUid>();
            var children = entMan.GetComponent<TransformComponent>(target).ChildEnumerator;
            while (children.MoveNext(out var child))
                merged.Add(child);
            server.System<WFPlanetPoiSystem>().UnturnFixedRotation(merged);
            maps.InitializeMap(targetMap);
        });
        await server.WaitRunTicks(5);

        await server.WaitPost(() =>
        {
            var wreck = OnSite<WFMassTransceiverComponent>(entMan, target).Single();
            Assert.That(server.System<ConstructionSystem>().ChangeNode(wreck, null, "working"), Is.True);
        });
        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            var nodes = server.System<NodeContainerSystem>();
            var substation = OnSite<PowerNetworkBatteryComponent>(entMan, target)
                .Single(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "SubstationWallBasic");
            Assert.That(nodes.TryGetNode<Node>(entMan.GetComponent<NodeContainerComponent>(substation), "input", out var input), Is.True);
            var rtgs = OnSite<PowerSupplierComponent>(entMan, target)
                .Where(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "GeneratorRTG").ToList();
            Assert.That(rtgs, Has.Count.EqualTo(2));
            foreach (var rtg in rtgs)
            {
                Assert.That(nodes.TryGetNode<Node>(entMan.GetComponent<NodeContainerComponent>(rtg), "output", out var output), Is.True);
                Assert.That(output!.NodeGroup, Is.Not.Null.And.SameAs(input!.NodeGroup), $"{entMan.ToPrettyString(rtg)} doesn't reach the substation.");
            }

            var transceiver = OnSite<WFMassTransceiverComponent>(entMan, target).Single();
            Assert.That(server.System<WFPlanetCommsSystem>().Carries((transceiver, entMan.GetComponent<WFMassTransceiverComponent>(transceiver))), Is.True,
                "The repaired transceiver isn't powered.");

            var dark = OnSite<ApcPowerReceiverComponent>(entMan, target)
                .Where(uid => entMan.GetComponent<ApcPowerReceiverComponent>(uid) is { NeedsPower: true, Powered: false })
                .Select(uid => entMan.ToPrettyString(uid).ToString()).ToList();
            Assert.That(dark, Is.Empty, "These have no power.");

            entMan.DeleteEntity(target);
            maps.DeleteMap(sourceMap);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every entity with this component anchored directly on the site.</summary>
    private static List<EntityUid> OnSite<T>(IEntityManager entMan, EntityUid site) where T : IComponent
    {
        var found = new List<EntityUid>();
        var query = entMan.EntityQueryEnumerator<T, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.ParentUid == site)
                found.Add(uid);
        }

        return found;
    }
}
