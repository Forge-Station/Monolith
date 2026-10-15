#pragma warning disable RA0002
using System.Linq;
using Content.Server._Forge.KIAS;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasControllerShutdownTests
{
    [Test]
    public async Task ExplicitShutdownGraphRunsBeforeOffAndQueuedEventsAndTimersAreCancelled()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap(); var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid core = default, recorder = default;
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string prototype)
            {
                var uid = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, .5f, .5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid); return uid;
            }
            core = Spawn("KiasCore"); Spawn("KiasDataCable");
            var rack = Spawn("KiasControllerRack"); recorder = Spawn("KiasRecorder");
            var card = Spawn("KiasProgrammableController");
            var program = new KiasControllerProgram();
            program.Nodes.Add(new() { Id = 1, Kind = KiasNodeKind.Any, Profile = "Automation" });
            program.Nodes.Add(new() { Id = 2, Kind = KiasNodeKind.All, Profile = "Recorder" });
            program.Nodes.Add(new() { Id = 3, Kind = KiasNodeKind.Timer, Config = new() { Seconds = 10 } });
            program.Nodes.Add(new() { Id = 4, Kind = KiasNodeKind.OnStart });
            program.Nodes.Add(new() { Id = 5, Kind = KiasNodeKind.StringConstant, Config = new() { Text = "must-cancel-timer" } });
            program.Nodes.Add(new() { Id = 6, Kind = KiasNodeKind.All, Profile = "Recorder" });
            void W(int from, string output, int to, string input) => program.Wires.Add(new()
                { FromNode = from, FromPort = output, ToNode = to, ToPort = input });
            W(1, "Message", 2, "Message"); W(1, "Shutdown", 2, "Record"); W(1, "Manual", 2, "Record");
            W(4, "Started", 3, "Trigger"); W(3, "Elapsed", 6, "Record"); W(5, "Value", 6, "Message");
            em.GetComponent<KiasControllerCardComponent>(card).Program = program;
            em.System<KiasSystem>().Rebuild(map.Grid);
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);
        });
        await pair.RunTicksSync(40);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasRecorderComponent>(recorder).Entries.Any(e => e.Contains("must-cancel")), Is.False);
            em.System<KiasProtocolSystem>().Trigger(map.Grid, KiasTrigger.Manual, message: "must-cancel-event");
            em.System<KiasSystem>().SetEnabled(core, false);
            Assert.That(em.GetComponent<KiasRecorderComponent>(recorder).Entries, Has.Some.Contains(Loc.GetString("kias-shutdown")));
        });
        await pair.RunTicksSync(1000);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasRecorderComponent>(recorder).Entries.Any(e => e.Contains("must-cancel")), Is.False);
            Assert.That(em.GetComponent<KiasCoreComponent>(core).Enabled, Is.False);
        });
        await pair.CleanReturnAsync();
    }
}
