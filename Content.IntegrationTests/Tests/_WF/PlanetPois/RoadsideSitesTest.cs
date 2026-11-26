#nullable enable
using System.Linq;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Traders;
using Content.Shared.Light.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>
/// The mechanic's garage and the roadside inn are roadside sites on the paved worlds, always on Verda, and each loads as
/// one roofed grid with its trader behind his counter.
/// </summary>
[TestFixture]
public sealed class RoadsideSitesTest
{
    private static readonly (string Path, string Trader)[] Sites =
    {
        ("/Maps/_WF/PlanetPois/mechanic_shop.yml", "WFPoiMechanic"),
        ("/Maps/_WF/PlanetPois/roadside_inn.yml", "WFPoiInnkeeper"),
    };

    [Test]
    public async Task SitesLoadWholeWithTheirTraders()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            foreach (var (path, trader) in Sites)
            {
                var verda = server.ProtoMan.Index<WFPlanetPoiTablePrototype>("WFPoisAsclepiu").Entries.Where(entry => entry.MapPath == path).ToList();
                var merak = server.ProtoMan.Index<WFPlanetPoiTablePrototype>("WFPoisMerak").Entries.Where(entry => entry.MapPath == path).ToList();
                Assert.That(verda, Has.Count.EqualTo(1), $"Verda has no entry for {path}.");
                Assert.That(merak, Has.Count.EqualTo(1), $"Merak has no entry for {path}.");
                Assert.That(verda[0].MinCount, Is.EqualTo(1), $"Verda doesn't always place {path}.");
                Assert.That(merak[0].MaxCount, Is.EqualTo(1), $"Merak may place {path} more than once.");
                Assert.That(verda[0].Connection, Is.EqualTo(WFPlanetPoiConnection.Through), $"{path} should sit on its own stretch of road.");

                var maps = server.System<SharedMapSystem>();
                maps.CreateMap(out var mapId);
                Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(mapId, new ResPath(path), out var grid), Is.True, path);

                var grids = 0;
                var query = entMan.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
                while (query.MoveNext(out _, out _, out var xform))
                {
                    if (xform.MapID == mapId)
                        grids++;
                }

                Assert.That(grids, Is.EqualTo(1), $"{path} loaded in pieces.");
                Assert.That(entMan.HasComponent<RoofComponent>(grid!.Value.Owner), Is.True, $"{path} has no roof.");

                var traders = entMan.EntityQuery<TraderComponent, MetaDataComponent>()
                    .Where(found => found.Item2.EntityPrototype?.ID == trader
                                    && entMan.GetComponent<TransformComponent>(found.Item2.Owner).GridUid == grid.Value.Owner)
                    .ToList();
                Assert.That(traders, Has.Count.EqualTo(1), $"{path} should hold one {trader}.");
                Assert.That(traders[0].Item1.Table, Is.Not.Null, $"The {trader} in {path} has no counter to trade over.");
                maps.DeleteMap(mapId);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HospitalLoadsWholeOnVerda()
    {
        const string path = "/Maps/_WF/PlanetPois/planetary_hospital.yml";

        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var entry = server.ProtoMan.Index<WFPlanetPoiTablePrototype>("WFPoisAsclepiu").Entries.Single(found => found.MapPath == path);
            Assert.That(entry.MinCount, Is.EqualTo(1), "Verda doesn't always place the hospital.");
            Assert.That(entry.Connection, Is.EqualTo(WFPlanetPoiConnection.Through), "The hospital should sit on its own stretch of road.");

            var maps = server.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(mapId, new ResPath(path), out var grid), Is.True);

            var grids = 0;
            var query = entMan.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var xform))
            {
                if (xform.MapID == mapId)
                    grids++;
            }

            Assert.That(grids, Is.EqualTo(1), "The hospital loaded in pieces.");
            Assert.That(entMan.HasComponent<RoofComponent>(grid!.Value.Owner), Is.True, "The hospital has no roof.");
            maps.DeleteMap(mapId);
        });

        await pair.CleanReturnAsync();
    }
}
