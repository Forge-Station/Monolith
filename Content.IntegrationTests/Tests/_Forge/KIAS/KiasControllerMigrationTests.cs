#pragma warning disable RA0002
using System.Linq;
using Content.Server._Forge.KIAS;
using Content.Server._Forge.KIAS.Controllers;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasControllerMigrationTests
{
    [Test]
    public async Task AllDefaultPresetsCompileAndLegacyRecordsRequireAnInsertedCard()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        var io = em.System<KiasControllerIoSystem>();
        var engine = em.System<KiasProtocolSystem>();
        EntityUid rack = default, card = default, recorder = default;
        await pair.Server.WaitAssertion(() =>
        {
            var presets = prototypes.EnumeratePrototypes<KiasControllerProgramPrototype>().ToArray();
            Assert.That(presets, Has.Length.EqualTo(27));
            foreach (var preset in presets)
            {
                var compiled = KiasGraphCompiler.Compile(preset.Program, io.Schema, io.SnapshotSchema);
                Assert.That(compiled.Errors, Is.Empty, preset.ID);
                Assert.That(preset.Program.Wires, Is.Not.Empty, preset.ID);
                Assert.That(preset.Program.Copy().Nodes.Count, Is.EqualTo(preset.Program.Nodes.Count));
            }
            Assert.That(presets.Single(p => p.ID == "quiet").Enabled, Is.False);
            EntityUid Spawn(string prototype)
            {
                var uid = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, .5f, .5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid); return uid;
            }
            var core = Spawn("KiasCore"); Spawn("KiasDataCable");
            rack = Spawn("KiasControllerRack"); recorder = Spawn("KiasRecorder");
            card = Spawn("KiasProgrammableController");
            var record = new KiasProtocolRecord { Trigger = KiasTrigger.Manual, Cooldown = 10 };
            record.Actions.Add(new() { Kind = KiasActionKind.Record, Message = "card-only-policy" });
            em.GetComponent<KiasProtocolComponent>(core).Protocols.Add(record);
            em.System<KiasSystem>().Rebuild(map.Grid);
            engine.Trigger(map.Grid, KiasTrigger.Manual);
            var imported = KiasLegacyGraphTranslator.Import(record, io.Supports, io.NativeSinkProfile);
            Assert.That(imported.Errors, Is.Empty);
            Assert.That(imported.Program, Is.Not.Null);
            em.GetComponent<KiasControllerCardComponent>(card).Program = imported.Program!;
        });
        await pair.RunTicksSync(40);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasRecorderComponent>(recorder).Entries, Has.None.Contains("card-only-policy"));
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);
        });
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<KiasControllerRuntimeSystem>().Running(card), Is.True);
            engine.Trigger(map.Grid, KiasTrigger.Manual, eventKey: "fore");
            engine.Trigger(map.Grid, KiasTrigger.Manual, eventKey: "aft");
            engine.Trigger(map.Grid, KiasTrigger.Manual, eventKey: "fore");
        });
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() =>
        {
            var entries = em.GetComponent<KiasRecorderComponent>(recorder).Entries;
            Assert.That(entries.Count(e => e.Contains("card-only-policy")), Is.EqualTo(2));
            Assert.That(em.System<ItemSlotsSystem>().TryEject(rack, KiasControllerRackComponent.SlotId(0), null, out _), Is.True);
            engine.Trigger(map.Grid, KiasTrigger.Manual, eventKey: "third");
        });
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() => Assert.That(em.GetComponent<KiasRecorderComponent>(recorder).Entries.Count(e => e.Contains("card-only-policy")), Is.EqualTo(2)));
        await pair.CleanReturnAsync();
    }

    [Test]
    public void ImportReportsUnsupportedPoliciesInsteadOfSilentlyChangingThem()
    {
        var record = new KiasProtocolRecord { Trigger = KiasTrigger.Manual, RequireCrewUnavailable = true };
        record.Actions.Add(new() { Kind = KiasActionKind.Record });
        var result = KiasLegacyGraphTranslator.Import(record);
        Assert.That(result.Program, Is.Null);
        Assert.That(result.Errors, Does.Contain("legacy-crew-condition"));
        record.RequireCrewUnavailable = false;
        record.Actions[0].Kind = KiasActionKind.DevicePort;
        result = KiasLegacyGraphTranslator.Import(record);
        Assert.That(result.Program, Is.Null);
        Assert.That(result.Errors, Is.Not.Empty);
    }
}
