#pragma warning disable RA0002
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Numerics;
using Stopwatch = System.Diagnostics.Stopwatch;
using Content.Server._Forge.KIAS;
using Content.Server._Forge.KIAS.Controllers;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared._Forge.KIAS;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasControllerStressTests
{
    private static KiasControllerProgram Dense()
    {
        var program = new KiasControllerProgram { Name = "100 nodes / 200 wires" };
        program.Nodes.Add(new() { Id = 1, Kind = KiasNodeKind.Any, Profile = "Automation" });
        program.Nodes.Add(new() { Id = 2, Kind = KiasNodeKind.BoolConstant, Config = new() { Bool = true } });
        program.Nodes.Add(new() { Id = 3, Kind = KiasNodeKind.Clock, Config = new() { Bool = true, Seconds = .5 } });
        program.Nodes.Add(new() { Id = 4, Kind = KiasNodeKind.OnStart });
        void W(int from, string output, int to, string input) => program.Wires.Add(new()
            { FromNode = from, FromPort = output, ToNode = to, ToPort = input });
        for (var i = 0; i < 32; i++)
        {
            var gate = 5 + i; var latch = 37 + i; var edge = 69 + i;
            program.Nodes.Add(new() { Id = gate, Kind = KiasNodeKind.If });
            program.Nodes.Add(new() { Id = latch, Kind = KiasNodeKind.Latch });
            program.Nodes.Add(new() { Id = edge, Kind = KiasNodeKind.Edge });
            W(2, "Value", gate, "Condition"); W(1, "Manual", gate, "Trigger");
            W(gate, "True", latch, "Set"); W(gate, "False", latch, "Reset");
            W(latch, "Value", edge, "Value"); W(edge, "Rising", gate, "Trigger");
            if (i < 6) W(edge, "Falling", latch, "Reset");
        }
        W(3, "Tick", 5, "Trigger"); W(4, "Started", 6, "Trigger");
        return program;
    }

    [Test]
    public async Task DenseRackAndFiftyTwoHundredGridWarmColdTopologyAndOffStress()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap(); var em = pair.Server.ResolveDependency<IEntityManager>();
        var kias = em.System<KiasSystem>(); var runtime = em.System<KiasControllerRuntimeSystem>();
        var fleet = new List<(EntityUid Grid, EntityUid Core, EntityUid Cable, EntityUid Rack)>();
        var cards = new List<EntityUid>();
        void AddGrid(int count)
        {
            var maps = em.System<SharedMapSystem>();
            while (fleet.Count < count)
            {
                var index = fleet.Count;
                var grid = maps.CreateGridEntity(map.MapId);
                maps.SetTile(grid, grid.Comp, Vector2i.Zero, map.Tile.Tile);
                em.System<SharedTransformSystem>().SetLocalPosition(grid, new Vector2(index % 20 * 100, index / 20 * 100));
                EntityUid Spawn(string prototype)
                {
                    var uid = em.SpawnEntity(prototype, new EntityCoordinates(grid, .5f, .5f));
                    em.RemoveComponent<ApcPowerReceiverComponent>(uid); return uid;
                }
                var core = Spawn("KiasCore"); var cable = Spawn("KiasDataCable"); var rack = Spawn("KiasControllerRack");
                var program = Dense();
                Assert.That(program.Nodes, Has.Count.EqualTo(100)); Assert.That(program.Wires, Has.Count.EqualTo(200));
                Assert.That(KiasGraphCompiler.Compile(program, em.System<KiasControllerIoSystem>().Schema).Errors, Is.Empty);
                kias.Rebuild(grid);
                for (var i = 0; i < (index == 0 ? 8 : 1); i++)
                {
                    var card = Spawn("KiasProgrammableController"); cards.Add(card);
                    em.GetComponent<KiasControllerCardComponent>(card).Program = program.Copy();
                    Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(i), card, null), Is.True);
                }
                fleet.Add((grid, core, cable, rack));
            }
        }
        async Task Ticks(string label, int ticks = 60)
        {
            var samples = new List<double>();
            var bytes = new List<long>();
            var ownSamples = new List<double>();
            var ownBytes = new List<long>();
            long start = 0;
            long startBytes = 0;
            object? loop = null;
            EventHandler<FrameEventArgs> before = (_, _) =>
            {
                start = Stopwatch.GetTimestamp(); startBytes = GC.GetAllocatedBytesForCurrentThread();
                kias.MeasuredUpdateMilliseconds = 0; kias.MeasuredUpdateBytes = 0;
            };
            EventHandler<FrameEventArgs> after = (_, _) =>
            {
                samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                bytes.Add(GC.GetAllocatedBytesForCurrentThread() - startBytes);
                ownSamples.Add(kias.MeasuredUpdateMilliseconds);
                ownBytes.Add(kias.MeasuredUpdateBytes);
            };
            await pair.Server.WaitAssertion(() =>
            {
                for (var type = pair.Server.GetType(); type != null; type = type.BaseType)
                {
                    if (type.GetField("GameLoop", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is not { } field) continue;
                    loop = field.GetValue(pair.Server);
                    break;
                }
                Assert.That(loop, Is.Not.Null);
                kias.MeasureUpdates = true;
                loop!.GetType().GetEvent("Input")!.AddEventHandler(loop, before);
                loop.GetType().GetEvent("Update")!.AddEventHandler(loop, after);
            });
            var allocated = GC.GetTotalAllocatedBytes();
            var watch = Stopwatch.StartNew();
            try { await pair.RunTicksSync(ticks); }
            finally
            {
                await pair.Server.WaitAssertion(() =>
                {
                    loop!.GetType().GetEvent("Input")!.RemoveEventHandler(loop, before);
                    loop.GetType().GetEvent("Update")!.RemoveEventHandler(loop, after);
                    kias.MeasureUpdates = false;
                });
            }
            TestContext.Out.WriteLine($"KIAS {label}, {ticks} complete engine ticks: {watch.Elapsed.TotalMilliseconds:F2} ms, {GC.GetTotalAllocatedBytes() - allocated} total bytes (server + client + harness)");
            samples.Sort();
            TestContext.Out.WriteLine($"KIAS {label}, server ticks: max {samples.Max():F3} ms, p95 {samples[(int) (samples.Count * 0.95)]:F3} ms, max {bytes.Max()} bytes, sum {bytes.Sum()} bytes");
            ownSamples.Sort();
            TestContext.Out.WriteLine($"KIAS {label}, periodic KIAS updates: max {ownSamples.Max():F3} ms, p95 {ownSamples[(int) (ownSamples.Count * 0.95)]:F3} ms, max {ownBytes.Max()} bytes, sum {ownBytes.Sum()} bytes");
        }
        await pair.Server.WaitAssertion(() => AddGrid(1));
        await Ticks("one rack / 8 cards / 800 nodes / 1600 wires cold boot", 40);
        await pair.Server.WaitAssertion(() => Assert.That(runtime.RunningCount(fleet[0].Rack), Is.EqualTo(8)));
        await Ticks("one dense rack warm timers", 120);
        await pair.Server.WaitAssertion(() => AddGrid(50));
        await Ticks("50 grids / 57 dense cards cold boot", 60);
        await Ticks("50 grids / 57 dense cards warm timers", 120);
        await pair.Server.WaitAssertion(() => AddGrid(200));
        await Ticks("200 grids / 207 dense cards cold boot", 60);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(cards.All(runtime.Running), Is.True);
            foreach (var ship in fleet) em.System<KiasProtocolSystem>().Trigger(ship.Grid, KiasTrigger.Manual);
        });
        await Ticks("200 grids sensor fanout / 207 cards / 20700 nodes / 41400 wires", 120);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(cards.All(runtime.Running), Is.True);
            foreach (var ship in fleet) kias.Rebuild(ship.Grid);
        });
        await Ticks("200 dense-card topology rebuilds", 60);
        await pair.Server.WaitAssertion(() => { foreach (var ship in fleet) kias.SetEnabled(ship.Core, false); });
        await Ticks("200 dense-card grids hard OFF", 60);
        await pair.Server.WaitAssertion(() => Assert.That(cards.Any(runtime.Running), Is.False));
        await pair.Server.WaitAssertion(() => { foreach (var ship in fleet) kias.SetEnabled(ship.Core, true); });
        await Ticks("200 dense-card grids cold restart", 60);
        await pair.Server.WaitAssertion(() => Assert.That(cards.All(runtime.Running), Is.True));
        await pair.CleanReturnAsync();
    }
}
