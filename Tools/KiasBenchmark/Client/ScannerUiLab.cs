using System.IO;
using System.Linq;
using System.Reflection;
using System.Numerics;
using System.Text.Json;
using Content.Shared._Forge.KIAS;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Console;
using Robust.Shared.Timing;
using SixLabors.ImageSharp;

namespace Content.Client._Forge.KIAS;

public sealed class KiasScannerUiCommand : IConsoleCommand
{
    public string Command => "kias_lab_scanner_client";
    public string Description => "Capture actual network scanner BUI and world overlay.";
    public string Help => Command;
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var output = Environment.GetEnvironmentVariable("KIAS_LAB_SCANNER_OUTPUT") ?? throw new InvalidOperationException("Scanner output required.");
        var clyde = IoCManager.Resolve<IClyde>();
        clyde.MainWindow.Size = new Vector2i(1600, 900);
        clyde.MainWindow.IsVisible = true;
        IoCManager.Resolve<IUserInterfaceManager>().RootControl.AddChild(new ScannerCapture(output));
    }
    private sealed class ScannerCapture(string output) : Control
    {
        private int _index;
        private float _elapsed, _stable;
        private bool _capturing, _done;
        private static T Field<T>(object value, string name)
        {
            for (var type = value.GetType(); type != null; type = type.BaseType)
                if (type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) is { } field) return (T)field.GetValue(value)!;
            throw new InvalidOperationException("Missing UI field " + name);
        }
        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);
            if (_done || _capturing) return;
            _elapsed += args.DeltaSeconds;
            try
            {
                if (_elapsed > 180) throw new InvalidOperationException("Scanner UI timed out.");
                var em = IoCManager.Resolve<IEntityManager>();
                var query = em.AllEntityQueryEnumerator<UserInterfaceComponent>();
                KiasLocalWindow? window = null;
                while (query.MoveNext(out var uid, out var ui))
                    if (ui.ClientOpenInterfaces.TryGetValue(KiasUiKey.Scanner, out var bui)) window = Field<KiasLocalWindow>(bui, "_window");
                if (window == null || Field<BoundUserInterfaceState?>(window, "_state") is not KiasScannerState state || state.Geometry is not { } geometry) { _stable = 0; return; }
                var coverage = em.System<KiasCoverageSystem>();
                if (coverage.Geometry != geometry) { _stable = 0; return; }
                if (File.Exists(Path.Combine(output, $"client-case-{_index}.json"))) return;
                IoCManager.Resolve<ILightManager>().Enabled = false;
                IoCManager.Resolve<IEyeManager>().CurrentEye.DrawFov = false;
                LayoutContainer.SetPosition(window, new Vector2(1140, 280));
                _stable += args.DeltaSeconds;
                if (_stable < 2) return;
                if (geometry.Shape != KiasCoverageShape.Room || geometry.PreviewTruncated || geometry.Cells.Count != geometry.TileCount || geometry.BoundaryCells.Count != geometry.DoorCount) throw new InvalidOperationException("Actual client coverage differs from BUI geometry.");
                if (Field<LineEdit>(window, "_range").Visible) throw new InvalidOperationException("Scanner exposes editable range.");
                if (_index == 0 && (geometry.RoomStatus != "Ok" || geometry.TileCount != 9 || geometry.DoorCount != 4)) throw new InvalidOperationException("Client Briar (-11,2) geometry mismatch.");
                if (_index >= 2 && geometry.RoomStatus != "ExteriorSector") throw new InvalidOperationException("Client exterior geometry mismatch.");
                var details = Field<string>(window, "_details");
                if (!details.Contains("\u041f\u043e\u043a\u0440\u044b\u0442\u0438\u0435:") || details.Contains("\u0420\u045f")) throw new InvalidOperationException("Unreadable Russian scanner status.");
                var index = _index;
                _capturing = true;
                IoCManager.Resolve<IClyde>().Screenshot(ScreenshotType.Final, shot =>
                {
                    shot.SaveAsPng(Path.Combine(output, $"scanner-{index}.png")); shot.Dispose();
                    File.WriteAllText(Path.Combine(output, $"client-case-{index}.json"), JsonSerializer.Serialize(new { status = "PASS", actualNetworkBui = true, actualWorldOverlay = true, hiddenRangeEditor = true, readableRussianText = true, details, diagnosticFullbright = true, modules = state.Modules.ToString(), geometry }, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
                    window.Close();
                    _index++; _stable = 0; _capturing = false;
                    if (_index == 4) { File.WriteAllText(Path.Combine(output, "client-completed.json"), "{\"status\":\"PASS\",\"cases\":4,\"realDesktopRenderer\":true}"); _done = true; }
                });
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(output, "client-failed.json"), JsonSerializer.Serialize(new { index = _index, error = error.ToString() })); _done = true;
            }
        }
    }
}
