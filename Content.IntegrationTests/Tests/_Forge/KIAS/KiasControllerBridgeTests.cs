#pragma warning disable RA0002
using System.Linq;
using Content.Server._Forge.KIAS;
using Content.Server._Forge.KIAS.Controllers;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasControllerBridgeTests
{
    [Test]
    public async Task NativeSourceWithoutPhysicalLinksReachesPortableGraph()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap(); var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid scanner = default, card = default;
        var io = em.System<KiasControllerIoSystem>(); var runtime = em.System<KiasControllerRuntimeSystem>();
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string prototype)
            {
                var uid = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, .5f, .5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid); return uid;
            }
            Spawn("KiasCore"); Spawn("KiasDataCable"); var rack = Spawn("KiasControllerRack");
            scanner = Spawn("KiasRoomScanner"); card = Spawn("KiasProgrammableController");
            em.System<KiasSystem>().Rebuild(map.Grid);
            var profile = io.Profiles(scanner).Single(profile => profile.StartsWith("Link."));
            var schema = io.Schema(profile)!;
            var program = new KiasControllerProgram();
            var selector = new KiasControllerNode { Id = 1, Kind = KiasNodeKind.Any, Profile = profile,
                PortSnapshot = schema.Select(port => port.Copy()).ToList() };
            program.Nodes.Add(selector); program.Nodes.Add(new() { Id = 2, Kind = KiasNodeKind.Counter });
            program.Wires.Add(new() { FromNode = 1, FromPort = "out:KiasMotion", ToNode = 2, ToPort = "Increment" });
            Assert.That(io.SnapshotSchema(selector), Is.Not.Null);
            selector.PortSnapshot[0].Type = KiasPortType.Number;
            Assert.That(io.SnapshotSchema(selector), Is.Null, "Stored snapshots cannot manufacture native port types.");
            selector.PortSnapshot = schema.Select(port => port.Copy()).ToList();
            em.GetComponent<KiasControllerCardComponent>(card).Program = program;
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);
            Assert.That(em.GetComponent<DeviceLinkSourceComponent>(scanner).Outputs, Is.Empty);
        });
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(runtime.Running(card), Is.True, runtime.Fault(card));
            em.System<DeviceLinkSystem>().InvokePort(scanner, "KiasMotion");
            em.System<DeviceLinkSystem>().InvokePort(scanner, "KiasMotion");
            em.System<DeviceLinkSystem>().InvokePort(scanner, "NotARealSource");
        });
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() => Assert.That(runtime.LastValue(card, 2, "Value").Number, Is.EqualTo(2)));
        await pair.CleanReturnAsync();
    }
}
