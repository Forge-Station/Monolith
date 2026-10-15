using System.Linq;
using Content.IntegrationTests.Tests._WF.Drydock;
using Content.Server._NF.Atmos.Components;
using Content.Shared._NF.Atmos.BUI;
using Content.Shared._NF.Atmos.Events;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Atmos;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Mining;

/// <summary>Tests the Hokkaido gas sale console: it appraises in MIC, refuses a sale worth nothing and hides its rate.</summary>
[TestFixture]
public sealed class GasSaleConsoleTest
{
    private const string Console = "GasSaleConsoleHigh";
    private const string SalePoint = "GasSalePoint";
    private const string Mic = "MMCIndustrialCredit";

    /// <summary>MIC stacks on the map, in total.</summary>
    private static int MicSpawned(IEntityManager em)
    {
        var total = 0;
        var query = em.EntityQueryEnumerator<StackComponent>();
        while (query.MoveNext(out _, out var stack))
        {
            if (stack.StackTypeId == Mic)
                total += stack.Count;
        }

        return total;
    }

    /// <summary>The gas a sale point holds.</summary>
    private static GasMixture Storage(IEntityManager em, EntityUid point)
    {
        var storage = em.GetComponent<GasSalePointComponent>(point).GasStorage;
        return storage;
    }

    [Test]
    public async Task AppraisesInMicAndRefusesWorthlessSales()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid console = default, point = default, actor = default;
        await server.WaitAssertion(() =>
        {
            var grid = DrydockTest.CreateShip(pair, map.MapId);
            DrydockTest.SetFloor(pair, grid, 5);
            console = DrydockTest.SpawnAnchored(pair, grid, Console, new Vector2i(0, 0));
            point = DrydockTest.SpawnAnchored(pair, grid, SalePoint, new Vector2i(2, 0));
            actor = em.SpawnEntity("MobHuman", new EntityCoordinates(grid, 1.5f, 1.5f));

            Assert.That(em.GetComponent<MarketModifierComponent>(console).HideExamine, Is.True,
                "The console states its rate in its description, so examine should leave it out.");

            Storage(em, point).AdjustMoles(Gas.Plasma, 1f);
            DrydockTest.RaiseUiMessage(pair, console, GasSaleConsoleUiKey.Key, new GasSaleSellMessage(), actor);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Storage(em, point).GetMoles(Gas.Plasma), Is.EqualTo(1f),
                    "A sale worth nothing should leave the gas in the sale point.");
                Assert.That(MicSpawned(em), Is.Zero, "A sale worth nothing should pay nothing.");
            });

            Storage(em, point).AdjustMoles(Gas.Plasma, 100_000f);
            DrydockTest.RaiseUiMessage(pair, console, GasSaleConsoleUiKey.Key, new GasSaleRefreshMessage(), actor);
            Assert.That(server.System<SharedUserInterfaceSystem>().TryGetUiState<GasSaleConsoleBoundUserInterfaceState>(console,
                GasSaleConsoleUiKey.Key, out var state), Is.True, "The console should have a state.");
            Assert.Multiple(() =>
            {
                Assert.That(state!.Currency, Is.EqualTo("MIC"), "The appraisal should be in MIC.");
                Assert.That(state.Appraisal, Is.GreaterThan(0), "Enough gas should be worth some MIC.");
            });

            DrydockTest.RaiseUiMessage(pair, console, GasSaleConsoleUiKey.Key, new GasSaleSellMessage(), actor);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Storage(em, point).TotalMoles, Is.Zero, "The sale should take the gas.");
                Assert.That(MicSpawned(em), Is.GreaterThan(0), "The sale should pay MIC.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
