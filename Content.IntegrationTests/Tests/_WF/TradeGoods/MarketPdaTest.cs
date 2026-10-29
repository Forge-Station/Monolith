#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server.CartridgeLoader;
using Content.Server._WF.TradeGoods.Systems;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>Every PDA comes with the Market app, including those whose prototypes replace the base program list.</summary>
[TestFixture]
[TestOf(typeof(WFMarketCartridgeSystem))]
public sealed class MarketPdaTest
{
    [Test]
    public async Task EveryPdaHasTheMarketApp()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protos = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();
        var loaders = entMan.System<CartridgeLoaderSystem>();

        await server.WaitAssertion(() =>
        {
            var missing = new List<string>();
            var checkedPdas = 0;
            foreach (var proto in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract
                    || !proto.TryComp<PdaComponent>(out _, factory)
                    || !proto.TryComp<CartridgeLoaderComponent>(out var loader, factory)
                    // A PDA whose own programs fill its disk has no room left; only the admin PDA does.
                    || loader.PreinstalledPrograms.Count >= loader.DiskSpace)
                    continue;

                checkedPdas++;
                var pda = entMan.SpawnEntity(proto.ID, map.GridCoords);
                var installed = loaders.GetInstalled(pda)
                    .Select(program => entMan.GetComponent<MetaDataComponent>(program).EntityPrototype?.ID);
                if (!installed.Contains(WFMarketCartridgeSystem.MarketCartridge.Id))
                    missing.Add(proto.ID);

                entMan.DeleteEntity(pda);
            }

            Assert.That(checkedPdas, Is.GreaterThan(10), "There should be many PDAs to check.");
            Assert.That(missing, Is.Empty, "PDAs without the Market app: " + string.Join(", ", missing));
        });

        await pair.CleanReturnAsync();
    }
}
