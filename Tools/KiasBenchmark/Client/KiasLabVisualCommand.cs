using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Numerics;
using System.Text.Json;
using Content.Shared._Forge.KIAS.Controllers;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Console;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using SixLabors.ImageSharp;

namespace Content.Client._Forge.KIAS.Controllers;

public sealed class KiasLabVisualCommand : IConsoleCommand
{
    public string Command => "kias_lab_visual";
    public string Description => "Render KIAS programmer visual fixtures in a standalone laboratory client.";
    public string Help => Command;

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var output = Environment.GetEnvironmentVariable("KIAS_LAB_UI_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) { shell.WriteError("KIAS_LAB_UI_OUTPUT is required."); return; }
        Directory.CreateDirectory(output);
        var clyde = IoCManager.Resolve<IClyde>();
        clyde.MainWindow.IsVisible = true;
        shell.WriteLine("Visual capture initialized.");
        var clock = new CaptureClock();
        IoCManager.Resolve<IUserInterfaceManager>().RootControl.AddChild(clock);
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        var profiles = prototypes.EnumeratePrototypes<KiasControllerProfilePrototype>().ToDictionary(p => p.ID);
        var programs = prototypes.EnumeratePrototypes<KiasControllerProgramPrototype>().OrderBy(p => p.ID).ToArray();
        var sizes = new[] { new Vector2i(850, 500), new Vector2i(1200, 720), new Vector2i(1600, 900) };
        var window = new KiasControllerWindow();
        var index = 0;
        var geometry = new List<object>();
        var root = Path.GetFullPath(Path.Combine(output, "../.."));
        var presetHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "Resources/Prototypes/_Forge/KIAS/controller_presets.yml")))).ToLowerInvariant();
        var mapHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "Resources/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml")))).ToLowerInvariant();
        void RenderNext()
        {
            if (index == programs.Length * sizes.Length)
            {
                File.WriteAllText(Path.Combine(output, "completed.json"), JsonSerializer.Serialize(new
                {
                    status = "CAPTURED_REQUIRES_VISUAL_REVIEW", programs = programs.Length, captures = index, presetSha256 = presetHash, mapSha256 = mapHash, geometry, uiScale = 1,
                    scope = "Actual desktop client renderer and programmer window, local visual state fixtures; server write/eject covered separately"
                }));
                return;
            }
            shell.WriteLine($"Render fixture {index}.");
            var size = sizes[index % sizes.Length];
            var program = programs[index / sizes.Length];
            clyde.MainWindow.Size = size;
            var state = new KiasControllerEditorState
            {
                Name = "Проверка программатора: длинное русское название — " + program.ID,
                HasCard = true, Online = true, Editing = true, Enabled = true, Dirty = true,
                Revision = (uint)index + 1, Presets = programs.Select(p => p.ID).ToList(),
                Profiles = profiles.Values.Select(p => new KiasGraphProfileView { Id = p.ID, Ports = p.Ports.Select(port => port.Copy()).ToList() }).ToList(),
                Wires = program.Program.Wires.Select(wire => wire.Copy()).ToList(),
                Nodes = program.Program.Nodes.Select(node => new KiasGraphNodeView
                {
                    Id = node.Id, Kind = node.Kind, X = node.X, Y = node.Y, Profile = node.Profile,
                    Room = node.Room, Group = node.Group, Config = node.Config.Copy(),
                    Ports = profiles.TryGetValue(node.Profile, out var profile) ? KiasGraphCatalog.DevicePorts(profile.Ports) : KiasGraphCatalog.InternalPorts(node.Kind, node.Config.EnumDomain)
                }).ToList()
            };
            var compiled = KiasGraphCompiler.Compile(program.Program, id => profiles.TryGetValue(id, out var profile) ? profile.Ports : null);
            if (!compiled.Success) throw new InvalidOperationException("Visual preset does not compile: " + program.ID);
            foreach (var node in state.Nodes)
                node.Ports = node.Ports.Select(port => compiled.Graph!.Ports.GetValueOrDefault(new(node.Id, port.Id), port)).ToList();
            window.UpdateState(state);
            var selected = state.Nodes.FirstOrDefault(node => node.Kind == (size.X == 1200 ? KiasNodeKind.EnumConstant : KiasNodeKind.NumberConstant)) ?? state.Nodes.FirstOrDefault();
            window.SelectNode(selected);
            window.SetSize = new Vector2(size.X, size.Y);
            window.OpenCentered();
            clock.After(1, () =>
            {
                geometry.Add(new { preset = program.ID, width = size.X, height = size.Y,
                    windowWidth = window.PixelSize.X, windowHeight = window.PixelSize.Y, selectedKind = selected?.Kind.ToString(), selectedEnumDomain = selected?.Ports.FirstOrDefault(port => port.Type == KiasPortType.Enum)?.EnumDomain.ToString() });
                clyde.Screenshot(ScreenshotType.Final, screenshot =>
                {
                    screenshot.SaveAsPng(Path.Combine(output, $"{program.ID}-{size.X}x{size.Y}.png"));
                    screenshot.Dispose();
                });
                index++;
                clock.After(0.5f, RenderNext);
            });
        }
        clock.After(2, RenderNext);
    }
    private sealed class CaptureClock : Control
    {
        private float _remaining;
        private Action? _next;
        public void After(float seconds, Action next) { _remaining = seconds; _next = next; }
        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);
            if (_next == null || (_remaining -= args.DeltaSeconds) > 0) return;
            var next = _next;
            _next = null;
            next();
        }
    }
}
