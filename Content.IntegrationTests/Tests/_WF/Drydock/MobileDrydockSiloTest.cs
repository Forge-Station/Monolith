using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Drydock;
using Content.Server._WF.Lathe;
using Content.Server.Power.Components;
using Content.Shared._WF.Drydock;
using Content.Shared._WF.Lathe;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Materials.OreSilo;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Drydock;

/// <summary>Tests mobile cranes on fabrication silos: refit parts from a parts silo, and a chemical silo's stock.</summary>
[TestFixture]
public sealed class MobileDrydockSiloTest
{
    private const string ThrusterRefit = "WFRefitThrusters";

    /// <summary>The part a stock thruster's capacitor becomes at Thrusters Mk II.</summary>
    private const string MkIICapacitor = "AdvancedCapacitorStockPart";

    /// <summary>A trade good that is also a rating-3 capacitor.</summary>
    private const string EconomyCapacitor = "CapacitorEconomy2";

    /// <summary>A stock capacitor, at a rating no refit tier installs.</summary>
    private const string MkICapacitor = "CapacitorStockPart";

    /// <summary>Spawns an always-powered fabrication silo on a yard, off the arm column, and links a crane to it.</summary>
    internal static EntityUid SpawnLinkedSilo(TestPair pair, EntityUid host, string proto, Vector2i tile, EntityUid crane)
    {
        var em = pair.Server.EntMan;
        var silo = DrydockTest.SpawnAnchored(pair, host, proto, tile);
        pair.Server.System<SharedPowerReceiverSystem>().SetNeedsPower(silo, false);
        em.GetComponent<ApcPowerReceiverComponent>(silo).Powered = true;
        em.EventBus.RaiseLocalEvent(silo, new ToggleOreSiloClientMessage(em.GetNetEntity(crane)));
        Assert.That(pair.Server.System<FabricationSiloSystem>().GetLinkedSilo(crane, em.GetComponent<FabricationSiloComponent>(silo).Kind)?.Owner,
            Is.EqualTo(silo), $"The crane should link to {em.ToPrettyString(silo)}.");
        return silo;
    }

    /// <summary>The console's current state, pushed fresh.</summary>
    private static WFDrydockConsoleState State(TestPair pair, EntityUid console)
    {
        pair.Server.System<WFDrydockSystem>().UpdateUi(console, force: true);
        Assert.That(pair.Server.System<SharedUserInterfaceSystem>().TryGetUiState<WFDrydockConsoleState>(console,
            WFDrydockConsoleUiKey.Key, out var state), Is.True, "The console should have a state.");
        return state!;
    }

    /// <summary>
    /// A refit unit takes a part of its rating from the crane's parts silo in place of its materials, so a crane with no
    /// materials still refits; an aborted job puts the part back.
    /// </summary>
    [Test]
    public async Task RefitPaysWithSiloParts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var silos = server.System<FabricationSiloSystem>();

        (EntityUid Grid, EntityUid Crane, EntityUid Console) yard = default;
        EntityUid ship = default, payer = default;

        await server.WaitAssertion(() =>
        {
            yard = MobileDrydockTest.SpawnMobileYard(pair, map.MapId);
            var comp = em.GetComponent<WFDrydockConsoleComponent>(yard.Console);
            Assert.That(drydock.TryGetBerth(yard.Console, comp, out var berth), "The crane should make a berth.");
            ship = DrydockTest.CreateDamagedShip(pair, map.MapId);
            DrydockTest.PlaceOver(pair, ship, yard.Grid, berth.Area.Center);
            payer = DrydockTest.CreatePayer(pair, map.MapId);
            SpawnLinkedSilo(pair, yard.Grid, "WFMachinePartsSilo", new Vector2i(5, 2), yard.Crane);
        });

        // Machines fill their stock parts on map init.
        await server.WaitRunTicks(2);
        await MobileDrydockTest.WaitForCranePower(pair, yard.Crane);

        await server.WaitAssertion(() =>
        {
            // Neither a trade good of the installed type and rating nor a Mk I part is a part the refit installs.
            foreach (var proto in new[] { EconomyCapacitor, MkICapacitor })
            {
                var other = em.SpawnEntity(proto, em.GetComponent<TransformComponent>(yard.Crane).Coordinates);
                Assert.That(silos.ReturnPart(yard.Crane, other), Is.True, $"{proto} should go into the crane's silo.");
            }
            var before = State(pair, yard.Console);
            var quoted = before.Upgrades.Single(u => u.Id == ThrusterRefit);
            Assert.Multiple(() =>
            {
                Assert.That(before.StoredParts.Select(p => p.Prototype), Is.EquivalentTo(new[] { EconomyCapacitor, MkICapacitor }),
                    "The console should list everything in the silo, refit parts or not.");
                Assert.That(quoted.NextParts, Is.Empty, "The refit should take neither part.");
                Assert.That(quoted.NextMaterials, Is.Not.Empty, "Without its part, the refit should bill materials.");
            });

            var part = em.SpawnEntity(MkIICapacitor, em.GetComponent<TransformComponent>(yard.Crane).Coordinates);
            Assert.That(silos.IsAcceptedPart(part), Is.True, "A parts silo should take machine parts for the cranes.");
            Assert.That(silos.ReturnPart(yard.Crane, part), Is.True, "The part should go into the crane's silo.");

            var state = State(pair, yard.Console);
            var entry = state.Upgrades.Single(u => u.Id == ThrusterRefit);
            Assert.Multiple(() =>
            {
                Assert.That(state.StoredParts.Single(p => p.Prototype == MkIICapacitor).Count, Is.EqualTo(1),
                    "The console should list the silo's part.");
                Assert.That(entry.NextParts.GetValueOrDefault(MkIICapacitor), Is.EqualTo(1), "The refit should take the part.");
                Assert.That(entry.NextMaterials, Is.Empty, "The part should pay for the thruster's only unit.");
            });

            // The crane holds no materials, so only the part can pay.
            DrydockTest.RaiseUiMessage(pair, yard.Console, WFDrydockConsoleUiKey.Key, new WFDrydockUpgradeMessage(ThrusterRefit), payer);
            Assert.Multiple(() =>
            {
                Assert.That(em.TryGetComponent(yard.Console, out WFDrydockJobComponent job) && job.PaidInMaterials, Is.True,
                    "The refit should start, paid from the silo.");
                Assert.That(silos.GetPartAmount(yard.Crane, MkIICapacitor), Is.EqualTo(0), "The silo's part should be taken.");
            });

            // Leaving the berth aborts the job before the cranes reach the thruster.
            server.System<SharedTransformSystem>().SetWorldPosition(ship, new Vector2(2000f, 2000f));
        });

        await DrydockTest.RunUntil(pair, 100, 10, () => !em.HasComponent<WFDrydockJobComponent>(yard.Console));

        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WFDrydockJobComponent>(yard.Console), Is.False, "The job should abort.");
            Assert.That(silos.GetPartAmount(yard.Crane, MkIICapacitor), Is.EqualTo(1), "The unfinished refit's part should go back.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The console lists every reagent in a chemical silo linked to its cranes.</summary>
    [Test]
    public async Task ConsoleListsChemicalSiloStock()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var yard = MobileDrydockTest.SpawnMobileYard(pair, map.MapId);
            var silo = SpawnLinkedSilo(pair, yard.Grid, "WFMachineChemicalSilo", new Vector2i(5, 3), yard.Crane);
            Assert.That(server.System<FabricationSiloSystem>().TryGetSolution((silo, em.GetComponent<FabricationSiloComponent>(silo)),
                out var soln, out _), Is.True, "The chemical silo should have a solution.");

            // Added directly, so reagents no lathe recipe uses are stored too.
            var solution = new Solution();
            solution.AddReagent("Water", FixedPoint2.New(40));
            solution.AddReagent("WeldingFuel", FixedPoint2.New(25));
            Assert.That(server.System<SharedSolutionContainerSystem>().TryAddSolution(soln!.Value, solution), Is.True);

            var stored = State(pair, yard.Console).StoredChemicals;
            Assert.Multiple(() =>
            {
                Assert.That(stored.GetValueOrDefault("Water"), Is.EqualTo(FixedPoint2.New(40)));
                Assert.That(stored.GetValueOrDefault("WeldingFuel"), Is.EqualTo(FixedPoint2.New(25)));
            });
        });

        await pair.CleanReturnAsync();
    }
}
