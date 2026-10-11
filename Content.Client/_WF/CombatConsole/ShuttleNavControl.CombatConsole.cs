using System.Numerics;
using Content.Client._WF.CombatConsole;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using ConsoleTheme = Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private bool _wfInstrumentRadar;

    /// <summary>Retains the radar's original mode controls above the instrument glass.</summary>
    public void WfAttachRadarControls(LayoutContainer parent)
    {
        _wfInstrumentRadar = true;
        EnsureRadarModeButtons();
        if (_radarModeButtons == null)
            return;

        _radarModeButtons.Name = "WfRadarModes";
        _radarModeButtons.SeparationOverride = 4;
        _radarModeButtons.MinSize = _radarModeButtons.MaxSize = new Vector2(140, 32);
        WfStyleMode(_radarAzimuthButton!, "WfRadarAzimuth", () => _azimuthMode != RadarAzimuthMode.Off);
        WfStyleMode(_radarRotationButton!, "WfRadarRotation", () => _angleFollow);
        WfStyleMode(_radarAnchorButton!, "WfRadarAnchor", () => _relativePanning);
        WfStyleMode(_radarResetButton!, "WfRadarReset", () => false);
        Move(_radarModeButtons, LayoutContainer.LayoutPreset.BottomLeft);
        if (_wfTerrainButton != null)
        {
            _wfTerrainButton.Name = "WfRadarTerrain";
            _wfTerrainButton.AddStyleClass("WfCompact");
            _wfTerrainButton.MinSize = _wfTerrainButton.MaxSize = new Vector2(120, 32);
            ConsoleTheme.Switch(_wfTerrainButton);
            Move(_wfTerrainButton, LayoutContainer.LayoutPreset.TopRight);
        }

        void Move(Control control, LayoutContainer.LayoutPreset preset)
        {
            if (control.Parent != parent)
                parent.AddChild(ConsoleTheme.Detach(control));
            control.SetPositionLast();
            LayoutContainer.SetAnchorAndMarginPreset(control, preset, margin: 8);
        }
    }

    /// <summary>Moves overlay controls with a radar borrowed by the cockpit.</summary>
    protected override void Parented(Control newParent)
    {
        base.Parented(newParent);
        if (_wfInstrumentRadar && newParent is LayoutContainer layers)
            WfAttachRadarControls(layers);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _wfInstrumentRadar)
        {
            _radarModeButtons?.Dispose();
            _wfTerrainButton?.Dispose();
        }
        base.Dispose(disposing);
    }

    private static void WfStyleMode(RadarModeButton button, string name, Func<bool> active)
    {
        button.Name = name;
        button.WfInstrument = true;
        button.WfActive = active;
        button.MinSize = button.MaxSize = new Vector2(32);
        button.ModulateSelfOverride = Color.White;
        button.MuteSounds = true;
        button.OnPressed -= WFConsoleAudio.Press;
        button.OnPressed += WFConsoleAudio.Press;
    }

    public sealed partial class RadarModeButton
    {
        internal bool WfInstrument;
        internal Func<bool>? WfActive;
        private readonly WFConsoleStyleBox _wfUp = new("button_up", false, false, compact: true);
        private readonly WFConsoleStyleBox _wfHover = new("button_hover", false, false, compact: true);
        private readonly WFConsoleStyleBox _wfDown = new("button_down", false, true, compact: true);
        private DrawVertexUV2DColor[] _wfVertices = Array.Empty<DrawVertexUV2DColor>();

        /// <summary>Draws the original radar functions as legible controls in either instrument palette.</summary>
        private bool WfDrawInstrument(DrawingHandleScreen handle)
        {
            if (!WfInstrument)
                return false;
            var face = DrawMode == DrawModeEnum.Pressed ? _wfDown : DrawMode == DrawModeEnum.Hover ? _wfHover : _wfUp;
            face.Draw(handle, UIBox2.FromDimensions(Vector2.Zero, PixelSize), UIScale);
            var active = WfActive?.Invoke() == true;
            var ink = Disabled ? ConsoleTheme.Muted : active ? ConsoleTheme.Accent : ConsoleTheme.Cream;
            var center = new Vector2(PixelWidth / 2f, PixelHeight / 2f);
            var radius = 8 * UIScale;
            var stroke = Math.Max(1, UIScale);
            void Line(float x1, float y1, float x2, float y2) =>
                handle.DrawLine(center + new Vector2(x1, y1) * radius, center + new Vector2(x2, y2) * radius, ink);
            void Arc(Vector2 origin, float size, float start, float end) =>
                WFConsoleDigital.Arc(handle, origin, size, stroke, start, end, ink, ref _wfVertices);
            switch (_icon)
            {
                case RadarModeButtonIcon.Azimuth:
                    Arc(center, radius * 0.8f, 0, MathF.Tau);
                    Line(-1, 0, 1, 0);
                    Line(0, -1, 0, 1);
                    break;
                case RadarModeButtonIcon.Rotation:
                    Arc(center, radius, -MathF.PI * 0.8f, MathF.PI * 0.8f);
                    Line(-0.8f, 0.6f, -0.3f, 0.7f);
                    Line(-0.8f, 0.6f, -0.7f, 0.1f);
                    break;
                case RadarModeButtonIcon.Anchor:
                    Arc(center + new Vector2(0, -radius * 0.6f), radius * 0.25f, 0, MathF.Tau);
                    Line(0, -0.35f, 0, 0.9f);
                    Line(-0.45f, -0.1f, 0.45f, -0.1f);
                    Line(0, 0.9f, -0.8f, 0.3f);
                    Line(0, 0.9f, 0.8f, 0.3f);
                    Line(-0.8f, 0.3f, -0.8f, 0.65f);
                    Line(0.8f, 0.3f, 0.8f, 0.65f);
                    break;
                case RadarModeButtonIcon.Reset:
                    Arc(center, radius * 0.9f, -MathF.PI * 0.15f, MathF.PI * 1.45f);
                    Line(-0.15f, -0.9f, -0.55f, -0.5f);
                    Line(-0.15f, -0.9f, 0.25f, -0.5f);
                    WFConsoleDigital.Dot(handle, center, 1.5f * UIScale, ink, ref _wfVertices);
                    break;
            }
            if (active)
                handle.DrawLine(new Vector2(7, 28) * UIScale, new Vector2(25, 28) * UIScale, ink);
            return true;
        }
    }
}
