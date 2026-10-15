using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Content.Shared._Forge.KIAS.Controllers;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using SixLabors.ImageSharp;

namespace Content.Client._Forge.KIAS.Controllers;

public sealed class KiasLiveUiCommand : IConsoleCommand
{
    public string Command => "kias_lab_ui_client";
    public string Description => "Exercise the real network BUI in the desktop renderer.";
    public string Help => Command;
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var output = Environment.GetEnvironmentVariable("KIAS_LAB_LIVE_OUTPUT") ?? throw new InvalidOperationException("KIAS_LAB_LIVE_OUTPUT required.");
        Directory.CreateDirectory(output);
        var runner = new LiveRunner(output);
        IoCManager.Resolve<IUserInterfaceManager>().RootControl.AddChild(runner);
        IoCManager.Resolve<IClyde>().MainWindow.IsVisible = true;
    }
    private sealed class LiveRunner(string output) : Control
    {
        private KiasControllerWindow? _window;
        private readonly Queue<(string Name, Action Action)> _steps = new();
        private readonly List<string> _executed = new();
        private float _delay = .5f, _elapsed;
        private bool _done;
        private string _step = "wait for actual server BUI";
        private static T Field<T>(object owner, string name) => (T) owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
        private KiasControllerEditorState State => Field<KiasControllerEditorState>(_window!, "_state");
        private KiasGraphCanvas Canvas => Field<KiasGraphCanvas>(_window!, "_canvas");
        private static IEnumerable<Control> Children(Control owner)
        {
            foreach (var child in owner.Children) { yield return child; foreach (var nested in Children(child)) yield return nested; }
        }
        private void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private void Click(string key, Button? actual = null)
        {
            var button = actual ?? Children(_window!).OfType<Button>().Single(item => item.Text == Loc.GetString(key));
            Require(!button.Disabled && button.PixelSize.X > 1 && button.PixelSize.Y > 1, "Native button unavailable: " + key);
            button.MuteSounds = true;
            var ui = IoCManager.Resolve<IUserInterfaceManager>();
            ui.SetHovered(button);
            foreach (var (method, state) in new[] { ("KeyBindDown", BoundKeyState.Down), ("KeyBindUp", BoundKeyState.Up) })
                typeof(BaseButton).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button,
                    new object[] { new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state, new(button.GlobalPixelPosition + button.PixelSize / 2, default), true, button.PixelSize / 2, button.PixelSize / 2) });
            ui.SetHovered(null);
            Require(Field<bool>(_window!, "_pending"), "The real BUI did not send " + key);
        }
        private Vector2 Screen(Vector2 point) => (Vector2) typeof(KiasGraphCanvas).GetMethod("Screen", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(Canvas, new object[] { point })!;
        private Vector2 Port(KiasGraphNodeView node, string id) => (Vector2) typeof(KiasGraphCanvas).GetMethod("PortPosition", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(Canvas, new object[] { node, node.Ports.Single(port => port.Id == id) })!;
        private void Key(string method, Vector2 point, BoundKeyState state, bool right = false) => typeof(KiasGraphCanvas).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(Canvas,
            new object[] { new GUIBoundKeyEventArgs(right ? EngineKeyFunctions.UIRightClick : EngineKeyFunctions.UIClick, state, new(Canvas.GlobalPixelPosition + point, default), true, point, point) });
        private void Move(Vector2 point, Vector2 relative) => typeof(KiasGraphCanvas).GetMethod("MouseMove", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(Canvas,
            new object[] { new GUIMouseMoveEventArgs(relative, Canvas, point, new(Canvas.GlobalPixelPosition + point, default), point, point) });
        private void PanTo(Vector2 graphPoint)
        {
            var candidates = new[] { new Vector2(10, 10), new Vector2(Canvas.PixelSize.X - 10, 10), new Vector2(10, Canvas.PixelSize.Y - 10), Canvas.PixelSize - new Vector2(10, 10) };
            var hit = typeof(KiasGraphCanvas).GetMethod("PortAt", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var start = candidates.First(point => hit.Invoke(Canvas, new object[] { Canvas.GraphPosition(point) }) == null);
            var end = start + Canvas.PixelSize / 2 - Screen(graphPoint);
            Key("KeyBindDown", start, BoundKeyState.Down, right: true);
            Require(Field<bool>(Canvas, "_panning"), "Native pan must start on the canvas background, away from ports.");
            Move(end, end - start);
            Key("KeyBindUp", end, BoundKeyState.Up, right: true);
        }
        private void Select(Func<KiasGraphNodeView, bool> predicate)
        {
            var node = State.Nodes.First(predicate);
            var graph = new Vector2(node.X + 40, node.Y + 15);
            PanTo(graph);
            var point = Screen(graph);
            Key("KeyBindDown", point, BoundKeyState.Down);
            Key("KeyBindUp", point, BoundKeyState.Up);
        }
        private void AddSteps()
        {
            foreach (var size in new[] { new Vector2i(850, 500), new Vector2i(1200, 720), new Vector2i(1600, 900) })
            {
                var name = $"Живой GUI {size.X}×{size.Y}: длинное русское имя";
                void Step(string title, Action action) => _steps.Enqueue(($"{size.X}x{size.Y}: {title}", action));
                Step("resize real desktop window", () => { IoCManager.Resolve<IClyde>().MainWindow.Size = size; _window!.SetSize = new Vector2(size.X, size.Y); _window.OpenCentered(); });
                Step("load real preset", () => { Field<OptionButton>(_window!, "_presets").SelectId(State.Presets.IndexOf("battle-flash")); Click("kias-controller-load-preset"); });
                Step("native node drag", () =>
                {
                    Require(State.Name == "battle-flash", "Native preset acknowledgement missing.");
                    var node = State.Nodes.First(item => item.Profile == "Automation");
                    PanTo(new Vector2(node.X + 40, node.Y + 15));
                    var start = Screen(new Vector2(node.X + 40, node.Y + 15));
                    var delta = new Vector2(20, 20) * Field<float>(Canvas, "_zoom") * Canvas.UIScale;
                    Key("KeyBindDown", start, BoundKeyState.Down); Move(start + delta, delta); Key("KeyBindUp", start + delta, BoundKeyState.Up);
                });
                Step("select number", () => Select(node => node.Kind == KiasNodeKind.NumberConstant));
                Step("save number", () => { Children(_window!).OfType<LineEdit>().Single(item => item.Name == "kias-controller-number").Text = "0.25"; Click("kias-save"); });
                Step("select inferred enum", () => Select(node => node.Kind == KiasNodeKind.EnumConstant && node.Ports.Any(port => port.EnumDomain == KiasEnumDomain.AudioChannel)));
                Step("save inferred enum", () =>
                {
                    Require(Children(_window!).OfType<OptionButton>().Single(item => item.Name == "kias-controller-enum-domain").SelectedId == (int) KiasEnumDomain.AudioChannel, "Wrong inferred enum.");
                    Children(_window!).OfType<OptionButton>().Single(item => item.Name == "kias-controller-enum").SelectId(2); Click("kias-save");
                });
                Step("select ALL", () => Select(node => node.Kind == KiasNodeKind.All && node.Profile == "Speaker"));
                Step("save room/group", () => { Children(_window!).OfType<LineEdit>().Single(item => item.Name == "kias-controller-filter-room").Text = "Тестовая комната"; Children(_window!).OfType<LineEdit>().Single(item => item.Name == "kias-controller-filter-group").Text = "Тестовая группа"; Click("kias-save"); });
                Step("zoom and search real adapter", () =>
                {
                    typeof(KiasGraphCanvas).GetMethod("MouseWheel", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(Canvas, new object[] { new GUIMouseWheelEventArgs(new Vector2(0, -10), Canvas, Canvas.PixelSize / 2, default, Canvas.PixelSize / 2, Canvas.PixelSize / 2) });
                    var core = State.Nodes.First(node => node.Profile == "Automation"); PanTo(Port(core, "Manual"));
                    var em = IoCManager.Resolve<IEntityManager>();
                    var device = State.Devices.First(item => item.Profile == "DeviceAdapter" && em.GetComponent<MetaDataComponent>(em.GetEntity(item.Entity)).EntityPrototype?.ID == "KiasDeviceAdapter");
                    Field<LineEdit>(_window!, "_search").SetText(device.Identifier, invokeEvent: true);
                });
                Step("bind actual SPECIFIC adapter", () =>
                {
                    var em = IoCManager.Resolve<IEntityManager>();
                    var device = State.Devices.First(item => item.Profile == "DeviceAdapter" && em.GetComponent<MetaDataComponent>(em.GetEntity(item.Entity)).EntityPrototype?.ID == "KiasDeviceAdapter");
                    var profile = State.Profiles.Single(item => item.Id == device.Profile);
                    var tooltip = $"{device.Name} · #{device.Identifier}\n{KiasControllerLabels.Profile(device.Profile, profile.Ports)}";
                    Click("SPECIFIC adapter", Children(Field<BoxContainer>(_window!, "_palette")).OfType<Button>().Single(item => item.ToolTip == tooltip));
                });
                Step("move SPECIFIC", () =>
                {
                    Field<LineEdit>(_window!, "_search").SetText("", invokeEvent: true);
                    var node = State.Nodes.Single(item => item.Kind == KiasNodeKind.Specific && item.Profile == "DeviceAdapter");
                    var start = Screen(new Vector2(node.X + 40, node.Y + 15));
                    var delta = new Vector2(208, 0) * Field<float>(Canvas, "_zoom") * Canvas.UIScale;
                    Key("KeyBindDown", start, BoundKeyState.Down); Move(start + delta, delta); Key("KeyBindUp", start + delta, BoundKeyState.Up);
                });
                Step("native zoomed connection", () =>
                {
                    var source = State.Nodes.First(node => node.Profile == "Automation");
                    var adapter = State.Nodes.Single(node => node.Kind == KiasNodeKind.Specific && node.Profile == "DeviceAdapter");
                    var first = Port(source, "Manual"); var last = Port(adapter, "On"); PanTo((first + last) / 2);
                    var start = Screen(first); var end = Screen(last);
                    Require(start.X >= 0 && start.Y >= 0 && start.X <= Canvas.PixelSize.X && start.Y <= Canvas.PixelSize.Y && end.X >= 0 && end.Y >= 0 && end.X <= Canvas.PixelSize.X && end.Y <= Canvas.PixelSize.Y, "Connection endpoints must be visible in the live canvas.");
                    Key("KeyBindDown", start, BoundKeyState.Down); Key("KeyBindUp", end, BoundKeyState.Up);
                });
                Step("add actual Bool constant", () => Click("kias-controller-node-boolconstant"));
                Step("reject Bool to Signal", () =>
                {
                    var source = State.Nodes.Single(node => node.Kind == KiasNodeKind.BoolConstant);
                    var adapter = State.Nodes.Single(node => node.Kind == KiasNodeKind.Specific && node.Profile == "DeviceAdapter");
                    var first = Port(source, source.Ports.Single(port => port.Direction == KiasPortDirection.Output).Id);
                    var last = Port(adapter, "On"); PanTo((first + last) / 2);
                    var before = State.Wires.Count;
                    Key("KeyBindDown", Screen(first), BoundKeyState.Down); Key("KeyBindUp", Screen(last), BoundKeyState.Up);
                    Require(!Field<bool>(_window!, "_pending") && State.Wires.Count == before, "Native canvas accepted incompatible Bool to Signal.");
                });
                Step("rename actual draft", () => { Require(State.Wires.Any(wire => wire.FromPort == "Manual" && wire.ToPort == "On"), "Real server did not accept the connection."); Field<LineEdit>(_window!, "_name").Text = name; Click("kias-controller-rename"); });
                Step("WRITE actual card", () => { Require(Field<Button>(_window!, "_eject").Disabled, "Dirty editor allowed Eject."); Click("kias-controller-write"); });
                Step("capture actual written state", () =>
                {
                    Require(!State.Dirty && State.Name == name, "Real WRITE acknowledgement missing.");
                    IoCManager.Resolve<IClyde>().Screenshot(ScreenshotType.Final, shot => { shot.SaveAsPng(Path.Combine(output, $"live-{size.X}x{size.Y}.png")); shot.Dispose(); });
                    _delay = 1;
                });
                Step("rename unsaved draft", () => { Field<LineEdit>(_window!, "_name").Text = "Несохранённое изменение"; Click("kias-controller-rename"); });
                Step("native discard", () => Click("kias-controller-discard"));
                Step("native eject", () => { Require(State.Name == name && !State.Dirty, "Discard failed."); Click("kias-controller-eject"); });
                Step("observe actual ejection", () => { Require(!State.HasCard, "Actual card was not ejected."); _delay = 3; });
                Step("observe native reinsert", () => Require(State.HasCard && State.Name == name, "Native server reinsertion did not preserve the physical card."));
            }
        }
        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);
            if (_done) return;
            _elapsed += args.DeltaSeconds;
            try
            {
                Require(_elapsed < 240, "Live GUI timed out at " + _step);
                if ((_delay -= args.DeltaSeconds) > 0) return;
                _delay = .5f;
                if (_window == null)
                {
                    var query = IoCManager.Resolve<IEntityManager>().AllEntityQueryEnumerator<UserInterfaceComponent>();
                    while (query.MoveNext(out _, out var ui))
                        if (ui.ClientOpenInterfaces.TryGetValue(KiasControllerUiKey.Programmer, out var bui)) _window = Field<KiasControllerWindow>(bui, "_editor");
                    if (_window == null) return;
                    AddSteps();
                }
                if (Field<bool>(_window, "_pending")) return;
                if (_steps.Count == 0)
                {
                    File.WriteAllText(Path.Combine(output, "client-completed.json"), JsonSerializer.Serialize(new { status = "PASS", realDesktopRenderer = true, actualServerBui = true, steps = _executed }, new JsonSerializerOptions { WriteIndented = true }));
                    _done = true; return;
                }
                var step = _steps.Dequeue(); _step = step.Name; step.Action(); _executed.Add(_step);
            }
            catch (Exception error)
            {
                IoCManager.Resolve<IClyde>().Screenshot(ScreenshotType.Final, shot => { shot.SaveAsPng(Path.Combine(output, "failed-state.png")); shot.Dispose(); });
                File.WriteAllText(Path.Combine(output, "client-failed.json"), JsonSerializer.Serialize(new { step = _step, error = error.ToString(), executed = _executed }, new JsonSerializerOptions { WriteIndented = true }));
                _done = true;
            }
        }
    }
}
