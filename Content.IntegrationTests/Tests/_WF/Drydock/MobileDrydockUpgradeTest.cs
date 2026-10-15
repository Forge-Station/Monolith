using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Drydock;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.Power.Components;
using Content.Shared._WF.Drydock;
using Content.Shared.Construction.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Drydock;

/// <summary>Tests machine part upgrades on the mobile drydock crane: speed, reach and the power they cost.</summary>
[TestFixture]
public sealed class MobileDrydockUpgradeTest
{
    private static readonly Dictionary<string, string> MkII = new()
    {
        ["Manipulator"] = "NanoManipulatorStockPart",
        ["MatterBin"] = "AdvancedMatterBinStockPart",
        ["Capacitor"] = "AdvancedCapacitorStockPart",
    };

    private static readonly Dictionary<string, string> MkIV = new()
    {
        ["Manipulator"] = "FemtoManipulatorStockPart",
        ["MatterBin"] = "BluespaceMatterBinStockPart",
        ["Capacitor"] = "QuadraticCapacitorStockPart",
    };

    /// <summary>Swaps a machine's parts of the given types for the given prototypes, then refreshes its upgrades.</summary>
    private static void SetParts(IEntityManager em, EntityUid uid, Dictionary<string, string> parts)
    {
        var machine = em.GetComponent<MachineComponent>(uid);
        var containers = em.System<SharedContainerSystem>();
        var coords = em.GetComponent<TransformComponent>(uid).Coordinates;
        foreach (var part in machine.PartContainer.ContainedEntities.ToList())
        {
            if (!em.TryGetComponent<MachinePartComponent>(part, out var comp) || !parts.TryGetValue(comp.PartType.Id, out var replacement))
                continue;

            containers.Remove(part, machine.PartContainer);
            em.DeleteEntity(part);
            containers.Insert(em.SpawnEntity(replacement, coords), machine.PartContainer);
        }

        em.System<ConstructionSystem>().RefreshParts(uid, machine);
    }

    /// <summary>A stock crane keeps its prototype stats, and builds with the matter bins that set its reach.</summary>
    [Test]
    public async Task StockCraneKeepsBaseStats()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var (_, crane, _) = MobileDrydockTest.SpawnMobileYard(pair, map.MapId);
            var comp = em.GetComponent<WFDrydockCraneComponent>(crane);
            var bins = em.GetComponent<MachineComponent>(crane).PartContainer.ContainedEntities
                .Count(p => em.TryGetComponent<MachinePartComponent>(p, out var part) && part.PartType.Id == "MatterBin");
            Assert.Multiple(() =>
            {
                Assert.That(bins, Is.EqualTo(2), "The crane should build with two matter bins.");
                Assert.That(comp.SpeedMultiplier, Is.EqualTo(1f));
                Assert.That(comp.MaxArm, Is.EqualTo(29f));
                Assert.That(comp.MaxExtension, Is.EqualTo(13f));
                Assert.That(comp.WorkingLoad, Is.EqualTo(6000f));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Full part sets raise speed, reach and draw by tier; Mk IV reaches points a stock crane can't.</summary>
    [Test]
    public async Task UpgradedCraneIsFasterReachesFurtherAndDrawsMore()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var drydock = server.System<WFDrydockSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var (host, crane, console) = MobileDrydockTest.SpawnMobileYard(pair, map.MapId);
            var comp = em.GetComponent<WFDrydockCraneComponent>(crane);
            var consoleComp = em.GetComponent<WFDrydockConsoleComponent>(console);

            // The arm root sits at grid y 4 and runs toward -Y, so this point is 50 tiles down the arm and 20 across.
            var far = new Vector2(20.5f, -46f);
            Assert.That(drydock.CanReach(consoleComp, host, crane, far), Is.False, "A stock crane shouldn't reach that far.");

            SetParts(em, crane, MkII);
            Assert.Multiple(() =>
            {
                Assert.That(comp.SpeedMultiplier, Is.EqualTo(1.5f));
                Assert.That(comp.MaxArm, Is.EqualTo(37f));
                Assert.That(comp.MaxExtension, Is.EqualTo(17f));
                Assert.That(comp.WorkingLoad, Is.EqualTo(6000f * 1.9f * 1.9f * 0.9f).Within(1f));
            });

            SetParts(em, crane, MkIV);
            Assert.Multiple(() =>
            {
                Assert.That(comp.SpeedMultiplier, Is.EqualTo(3f));
                Assert.That(comp.MaxArm, Is.EqualTo(63f));
                Assert.That(comp.MaxExtension, Is.EqualTo(26f));
                Assert.That(comp.WorkingLoad, Is.EqualTo(120_000f).Within(1f));
                Assert.That(drydock.CanReach(consoleComp, host, crane, far), Is.True, "A Mk IV crane should reach it.");
                Assert.That(drydock.TryGetBerth(console, consoleComp, out var berth), Is.True);
                Assert.That(berth.Area.Height, Is.EqualTo(60f).Within(0.01f), "The berth should run the upgraded arm.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Upgrading one part type costs that axis's share of the draw, and a working crane draws the new load at once.</summary>
    [Test]
    public async Task SingleAxisUpgradeAndWorkingDraw()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var (_, crane, _) = MobileDrydockTest.SpawnMobileYard(pair, map.MapId);
            var comp = em.GetComponent<WFDrydockCraneComponent>(crane);
            var consumer = em.GetComponent<PowerConsumerComponent>(crane);
            comp.Active = true;

            SetParts(em, crane, new Dictionary<string, string> { ["Manipulator"] = MkIV["Manipulator"] });
            Assert.Multiple(() =>
            {
                Assert.That(comp.SpeedMultiplier, Is.EqualTo(3f));
                Assert.That(comp.MaxArm, Is.EqualTo(29f), "Manipulators shouldn't change the reach.");
                Assert.That(comp.WorkingLoad, Is.EqualTo(30_000f).Within(1f));
                Assert.That(consumer.DrawRate, Is.EqualTo(comp.WorkingLoad), "A working crane should draw its new load.");
            });

            // Without its matching capacitor, a Mk IV crane draws all a substation passes.
            SetParts(em, crane, new Dictionary<string, string> { ["MatterBin"] = MkIV["MatterBin"] });
            Assert.That(comp.WorkingLoad, Is.EqualTo(150_000f).Within(1f));
        });

        await pair.CleanReturnAsync();
    }
}
