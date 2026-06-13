using Content.IntegrationTests;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Maps;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests;

/// <summary>
/// Verifies entity-level gas mixtures survive grid save/load (hangar persistence baseline).
/// </summary>
[TestFixture]
public sealed class ShipyardHangarTest
{
    [Test]
    public async Task CanisterPlasmaSurvivesGridSaveLoad()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapLoader = entManager.System<MapLoaderSystem>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var userData = server.ResolveDependency<IResourceManager>().UserData;
        var savePath = new ResPath("/persisted_shuttles/hangar_test.yml");

        await server.WaitPost(() =>
        {
            mapSystem.CreateMap(out var mapId0);
            var grid0 = mapManager.CreateGridEntity(mapId0);
            entManager.RunMapInit(grid0.Owner, entManager.GetComponent<MetaDataComponent>(grid0));
            var canister = entManager.SpawnEntity("GasCanister", new EntityCoordinates(grid0.Owner, 0.5f, 0.5f));
            var canisterComp = entManager.GetComponent<GasCanisterComponent>(canister);
            canisterComp.Air ??= new GasMixture();
            canisterComp.Air.SetMoles(Gas.Plasma, 100f);
            entManager.Dirty(canister, canisterComp);

            Assert.That(mapLoader.TrySaveGrid(grid0.Owner, savePath));

            mapSystem.CreateMap(out var mapId1);
            Assert.That(mapLoader.TryLoadGrid(mapId1, savePath, out var grid1));
            var loadedGrid = grid1!.Value.Owner;

            var canisters = entManager.AllEntityQueryEnumerator<GasCanisterComponent, TransformComponent>();
            GasCanisterComponent? loaded = null;
            while (canisters.MoveNext(out _, out var comp, out var xform))
            {
                if (xform.GridUid != loadedGrid)
                    continue;

                loaded = comp;
                break;
            }

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Air!.GetMoles(Gas.Plasma), Is.EqualTo(100f).Within(0.1f));
        });

        if (userData.Exists(savePath))
            userData.Delete(savePath);

        await pair.CleanReturnAsync();
    }
}
