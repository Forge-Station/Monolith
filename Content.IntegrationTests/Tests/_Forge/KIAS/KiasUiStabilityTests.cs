using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._Forge.KIAS.Controllers;
using Content.Server._Forge.KIAS;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Containers.ItemSlots;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Localization;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasUiStabilityTests
{
    [Test]
    public async Task UnanchoredAtmosDeviceNearConnectorDoesNotKeepGridDirty()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid rack = default, alarm = default;
        uint revision = 0;
        await pair.Server.WaitAssertion(() =>
        {
            for (var x = 0; x < 5; x++)
            for (var y = 0; y < 3; y++)
                em.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            EntityUid Spawn(string id, float x = .5f)
            {
                var uid = em.SpawnEntity(id, new EntityCoordinates(map.Grid, x, .5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                return uid;
            }
            Spawn("KiasDataCable"); Spawn("KiasCore");
            foreach (var id in new[] { "KiasAtmosServer", "KiasPowerServer", "KiasCrewServer", "KiasDefenceServer", "KiasNavigationServer", "KiasLightController", "KiasFlipFlop" }) Spawn(id);
            rack = Spawn("KiasControllerRack");
            var scanner = Spawn("KiasRoomScanner");
            Assert.That(em.System<ItemSlotsSystem>().TryEject(scanner, "kias-module-3", null, out var previousModule), Is.True);
            em.DeleteEntity(previousModule!.Value);
            var connector = Spawn("KiasConnectorModule");
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(scanner, "kias-module-3", connector, null), Is.True);
            alarm = Spawn("AirAlarm", 1.5f);
            em.System<SharedTransformSystem>().Unanchor(alarm);
        });
        await pair.RunTicksSync(60);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<KiasSystem>().IsOnline(rack), Is.True);
            revision = em.GetComponent<KiasGridComponent>(map.Grid).Revision;
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<KiasSystem>().IsOnline(rack), Is.True);
            Assert.That(em.GetComponent<KiasGridComponent>(map.Grid).Revision, Is.EqualTo(revision));
            Assert.That(em.HasComponent<KiasIntegratedComponent>(alarm), Is.False);
            em.System<SharedTransformSystem>().AnchorEntity(alarm);
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<KiasIntegrationSystem>().CanControl(alarm), Is.True);
            Assert.That(em.System<KiasSystem>().IsOnline(rack), Is.True);
            revision = em.GetComponent<KiasGridComponent>(map.Grid).Revision;
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() => Assert.That(em.GetComponent<KiasGridComponent>(map.Grid).Revision, Is.EqualTo(revision)));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RackUpdatesKeepTheHoveredButtonsAndUseCurrentSlotState()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Client.WaitAssertion(() =>
        {
            using var rack = new KiasControllerRackWindow();
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            ui.WindowRoot.AddChild(rack);
            KiasControllerRackState State(bool enabled) => new() { Slots = new() { new() { Inserted = true, Enabled = enabled, Name = "Card" } } };
            rack.UpdateState(State(true));
            rack.Measure(new Vector2(650, 420));
            rack.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(650, 420)));
            var buttons = Descendants(rack).OfType<Button>().ToArray();
            buttons[0].MuteSounds = true;
            ui.SetHovered(buttons[0]);
            KiasControllerRackMessage? submitted = null;
            rack.Changed += message => submitted = message;
            void Click(string method, BoundKeyState state) => typeof(BaseButton).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(buttons[0],
                new object[] { new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state, new(buttons[0].GlobalPixelPosition + Vector2.One, default), true, Vector2.One, Vector2.One) });
            Click("KeyBindDown", BoundKeyState.Down);
            for (var i = 0; i < 30; i++) rack.UpdateState(State(i % 2 == 0));
            Assert.That(Descendants(rack).OfType<Button>().ToArray(), Is.EqualTo(buttons));
            Assert.That(buttons[0].Text, Is.EqualTo(Loc.GetString("kias-controller-enable")));
            Assert.That(ui.ControlFocused, Is.SameAs(buttons[0]));
            Click("KeyBindUp", BoundKeyState.Up);
            Assert.That(submitted, Is.Not.Null);
            Assert.That(submitted!.Enabled, Is.True);
            ui.SetHovered(null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LeavingAPortInsideTheCanvasImmediatelyHidesItsNativeTooltip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            using var canvas = new KiasGraphCanvas();
            ui.WindowRoot.AddChild(canvas);
            canvas.SetState(new() { Nodes = new() { new() { Id = 1, Kind = KiasNodeKind.OnStart, Ports = KiasGraphCatalog.InternalPorts(KiasNodeKind.OnStart) } } });
            ui.SetHovered(canvas);
            void Move(Vector2 position) => typeof(KiasGraphCanvas).GetMethod("MouseMove", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas,
                new object[] { new GUIMouseMoveEventArgs(Vector2.Zero, canvas, position, default, position, position) });
            Move(new Vector2(300, 96) * canvas.UIScale);
            Assert.That(canvas.ToolTip, Is.Not.Null);
            ui.GetType().GetMethod("_showTooltip", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ui, null);
            Assert.That(canvas.SuppliedTooltip, Is.Not.Null);
            Move(new Vector2(500, 200) * canvas.UIScale);
            Assert.That(canvas.ToolTip, Is.Null);
            Assert.That(canvas.SuppliedTooltip, Is.Null);
            ui.SetHovered(null);
        });
        await pair.CleanReturnAsync();
    }

    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
