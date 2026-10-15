using System.Linq;
using Content.Server._Forge.KIAS;
using Content.Server._Forge.KIAS.Controllers;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Interaction;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasOutputMonitorTests
{
    [Test]
    public async Task ToolOpensOutputsAndRefreshesValuesWithoutWritingOrShowingOfflineData()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid tool = default, scanner = default, actor = default, cable = default, crew = default;
        await pair.Server.WaitAssertion(() =>
        {
            for (var x = -2; x < 5; x++)
            for (var y = -2; y < 5; y++)
                em.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            var coordinates = new EntityCoordinates(map.Grid, .5f, .5f);
            EntityUid Spawn(string id)
            {
                var uid = em.SpawnEntity(id, coordinates);
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                return uid;
            }
            Spawn("KiasCore"); cable = Spawn("KiasDataCable"); crew = Spawn("KiasCrewServer");
            scanner = Spawn("KiasAdvancedRoomScanner"); actor = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 1.5f, 1.5f)); tool = Spawn("KiasServiceTool");
            em.GetComponent<KiasServiceToolComponent>(tool).Mode = KiasServiceMode.Diagnose;
            em.System<KiasSystem>().Rebuild(map.Grid);
        });
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            var used = new AfterInteractEvent(actor, tool, scanner, TransformCoordinates(scanner), true);
            em.EventBus.RaiseLocalEvent(tool, used);
            Assert.That(used.Handled, Is.True);
            Assert.That(em.System<UserInterfaceSystem>().IsUiOpen(tool, KiasUiKey.Service, actor), Is.True);
            em.System<KiasCrewSystem>().RefreshCounts(map.Grid);
            em.System<KiasDisplaySystem>().Refresh(tool);
            var initial = (KiasServiceState) em.System<KiasDisplaySystem>().BuildLocalState(tool);
            Assert.That(initial.Mode, Is.EqualTo(KiasServiceMode.Diagnose));
            Assert.That(initial.Target, Is.EqualTo(em.GetNetEntity(scanner)));
            Assert.That(initial.Details, Does.Contain(Loc.GetString("kias-monitor-title")));
            Assert.That(initial.Details, Does.Contain(Loc.GetString("kias-monitor-unset")));
            Assert.That(initial.Details, Does.Contain(NumberLine(0)));
            em.EnsureComponent<KiasClaimComponent>(map.Grid).Owner = new Robust.Shared.Network.NetUserId(Guid.NewGuid());
            var denied = new AfterInteractEvent(actor, tool, crew, TransformCoordinates(crew), true);
            em.EventBus.RaiseLocalEvent(tool, denied);
            Assert.That(em.GetComponent<KiasServiceToolComponent>(tool).Target, Is.EqualTo(scanner), "An unauthorized click must not select a target for the monitoring stream.");
            em.RemoveComponent<KiasClaimComponent>(map.Grid);
            em.EnsureComponent<KiasTrackedEntityComponent>(actor);
            em.System<KiasCrewSystem>().RefreshCounts(map.Grid);
            Assert.That(em.System<KiasControllerIoSystem>().TryOutput(scanner, "RoomScanner", "Occupied", out var value, out _), Is.True);
            Assert.That(value, Is.EqualTo(KiasGraphValue.Boolean(true)));
        });
        await pair.RunTicksSync(80);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<UserInterfaceSystem>().TryGetUiState<KiasServiceState>(tool, KiasUiKey.Service, out var live), Is.True);
            Assert.That(live!.Details, Does.Contain(NumberLine(1)));
            Assert.That(Enumerable.Range(1, 4).Any(seconds => live.Details.Contains(Loc.GetString("kias-monitor-pulse", ("seconds", seconds)))), Is.True, live.Details);
            em.DeleteEntity(cable);
            em.System<KiasSystem>().Rebuild(map.Grid);
        });
        await pair.RunTicksSync(80);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<UserInterfaceSystem>().TryGetUiState<KiasServiceState>(tool, KiasUiKey.Service, out var offline), Is.True);
            Assert.That(offline!.Details, Does.Contain(Loc.GetString("kias-monitor-offline")));
            Assert.That(offline.Details, Does.Not.Contain(NumberLine(1)));
            Assert.That(em.System<KiasControllerIoSystem>().TryOutput(scanner, "RoomScanner", "Entities", out _, out _), Is.False);
            em.DeleteEntity(scanner);
        });
        await pair.RunTicksSync(80);
        await pair.Server.WaitAssertion(() =>
        {
            var deleted = (KiasServiceState) em.System<KiasDisplaySystem>().BuildLocalState(tool);
            Assert.That(deleted.Target, Is.Null);
            Assert.That(deleted.Details, Is.Empty);
        });
        await pair.Client.WaitAssertion(() =>
        {
            var rsi = pair.Client.ResolveDependency<Robust.Client.ResourceManagement.IResourceCache>()
                .GetResource<Robust.Client.ResourceManagement.RSIResource>("/Textures/_Forge/KIAS/KiasCore.rsi", useFallback: false).RSI;
            Assert.That(rsi.Size, Is.EqualTo(new Vector2i(32, 64)));
            Assert.That(rsi.TryGetState("core", out _), Is.True);
        });
        await pair.CleanReturnAsync();

        EntityCoordinates TransformCoordinates(EntityUid uid) => em.GetComponent<TransformComponent>(uid).Coordinates;
        string NumberLine(int value) => $"{Loc.GetString("kias-controller-port-entities")} ({Loc.GetString("kias-controller-type-number")}): {value}";
    }
}
