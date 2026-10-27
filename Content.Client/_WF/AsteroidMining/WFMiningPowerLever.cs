using System.Numerics;
using Content.Client._WF.CombatConsole;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._WF.AsteroidMining;

/// <summary>
/// A vertical power lever for the ship's mining lasers. Drag the grip, or turn the mouse wheel over it a percent at a time;
/// each new setting is offered through <see cref="Changed"/>, and the grip follows <see cref="Power"/>.
/// </summary>
public sealed class WFMiningPowerLever : Control
{
    private readonly Font _small;
    private readonly Font _digits;
    private readonly string _caption = Loc.GetString("wf-mining-lever-caption");
    private readonly string _unit = Loc.GetString("wf-mining-dial-unit");
    private bool _dragging;
    private float _grip;
    private string _figure = "0";
    private float _power;

    /// <summary>The setting the grip shows, from zero to one.</summary>
    public float Power
    {
        get => _power;
        set
        {
            _power = Math.Clamp(value, 0f, 1f);
            _figure = ((int) MathF.Round(_power * 100)).ToString();
        }
    }

    /// <summary>A new setting the player picked, rounded to a percent.</summary>
    public event Action<float>? Changed;

    public WFMiningPowerLever()
    {
        MinSize = new Vector2(72, 170);
        SetWidth = 76;
        HorizontalExpand = false;
        MouseFilter = MouseFilterMode.Stop;
        DefaultCursorShape = CursorShape.VResize;
        ToolTip = Loc.GetString("wf-mining-lever-help");
        var cache = IoCManager.Resolve<IResourceCache>();
        _small = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Regular.ttf"), 9);
        _digits = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf"), 18);
    }

    /// <summary>The top and bottom of the grip's travel, in pixels.</summary>
    private (float Top, float Bottom) Track()
    {
        return (24f * UIScale, PixelHeight - 40f * UIScale);
    }

    private float ValueAt(float y)
    {
        var (top, bottom) = Track();
        return bottom > top ? (bottom - y) / (bottom - top) : 0f;
    }

    private void Offer(float value)
    {
        value = MathF.Round(Math.Clamp(value, 0f, 1f), 2);
        if (MathF.Abs(value - _power) < 0.001f)
            return;
        Power = value;
        Changed?.Invoke(value);
        IoCManager.Resolve<IEntityManager>().System<WFConsoleAudio>().TurnBearing();
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;
        _dragging = true;
        Offer(ValueAt(args.RelativePixelPosition.Y));
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function == EngineKeyFunctions.UIClick)
            _dragging = false;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        if (_dragging)
            Offer(ValueAt(args.RelativePixelPosition.Y));
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        if (args.Delta.Y == 0)
            return;
        Offer(_power + MathF.Sign(args.Delta.Y) * 0.01f);
        args.Handle();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _grip += (_power - _grip) * MathF.Min(1f, args.DeltaSeconds * 16f);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var skin = WFInstrumentTheme.Skin;
        var digital = WFInstrumentTheme.Digital;
        var bounds = PixelSizeBox;
        if (digital)
            WFConsoleDigital.Panel(handle, bounds, UIScale, skin.Glass, skin.EdgeSoft);
        else
            WFConsoleMetal.MetalPanel(handle, bounds, UIScale, skin.GlassRaised);

        WFInstrumentText.DrawFitted(handle, _small, _caption,
            UIBox2.FromDimensions(new Vector2(4 * UIScale, 4 * UIScale), new Vector2(PixelWidth - 8 * UIScale, 14 * UIScale)),
            UIScale, skin.Text);

        var (top, bottom) = Track();
        var slot = PixelWidth * 0.62f;
        for (var i = 0; i <= 20; i++)
        {
            var y = bottom - (bottom - top) * i / 20f;
            var length = (i % 10 == 0 ? 9 : i % 2 == 0 ? 6 : 3) * UIScale;
            var right = slot - 10 * UIScale;
            handle.DrawLine(new Vector2(right - length, y), new Vector2(right, y), i % 10 == 0 ? skin.Text : skin.TextMuted);
            if (i % 10 == 0)
                WFInstrumentText.DrawFitted(handle, _small, (i * 5).ToString(),
                    UIBox2.FromDimensions(new Vector2(2 * UIScale, y - 5 * UIScale), new Vector2(right - length - 4 * UIScale, 10 * UIScale)),
                    UIScale, skin.TextMuted);
        }

        // The slot the lever runs in, lit from the bottom up to the setting.
        var slotBox = new UIBox2(slot - 4 * UIScale, top - 4 * UIScale, slot + 4 * UIScale, bottom + 4 * UIScale);
        if (digital)
            handle.DrawRect(slotBox, skin.Ink);
        else
            WFConsoleMetal.Bevel(handle, slotBox, UIScale, skin.Ink, skin.EdgeSoft, inset: true);
        var gripY = bottom - (bottom - top) * _grip;
        if (digital)
        {
            for (var i = 0; i < 20; i++)
            {
                var segmentBottom = bottom - (bottom - top) * i / 20f;
                var lit = (i + 0.5f) / 20f <= _grip;
                handle.DrawRect(new UIBox2(slot - 2 * UIScale, segmentBottom - (bottom - top) / 20f + UIScale, slot + 2 * UIScale,
                    segmentBottom), lit ? skin.Accent : skin.GlassLight);
            }
        }
        else
        {
            handle.DrawRect(new UIBox2(slot - 2 * UIScale, gripY, slot + 2 * UIScale, bottom + 2 * UIScale), skin.Accent.WithAlpha(0.55f));
        }

        var grip = new UIBox2(slot - 20 * UIScale, gripY - 7 * UIScale, slot + 14 * UIScale, gripY + 7 * UIScale);
        if (digital)
        {
            WFConsoleDigital.Panel(handle, grip, UIScale, skin.GlassRaised, _dragging ? skin.Text : skin.Accent, false, 3);
            handle.DrawLine(new Vector2(grip.Left + 3 * UIScale, gripY), new Vector2(grip.Right - 3 * UIScale, gripY), skin.Accent);
        }
        else
        {
            handle.DrawRect(grip.Translated(new Vector2(0, 2 * UIScale)), Color.Black.WithAlpha(0.5f));
            WFConsoleMetal.Bevel(handle, grip, UIScale, _dragging ? skin.EdgeLight : skin.Edge, skin.EdgeLight);
            for (var i = -1; i <= 1; i++)
            {
                var y = gripY + i * 3 * UIScale;
                handle.DrawLine(new Vector2(grip.Left + 3 * UIScale, y), new Vector2(grip.Left + 12 * UIScale, y), skin.Ink);
            }
            handle.DrawLine(new Vector2(slot - 4 * UIScale, gripY), new Vector2(grip.Right - 2 * UIScale, gripY), skin.Accent);
        }

        var readout = new UIBox2(5 * UIScale, PixelHeight - 28 * UIScale, PixelWidth - 5 * UIScale, PixelHeight - 5 * UIScale);
        handle.DrawRect(readout, skin.Ink);
        WFInstrumentText.DrawReading(handle, _digits, _figure, _unit,
            new UIBox2(readout.TopLeft + new Vector2(2 * UIScale), readout.BottomRight - new Vector2(2 * UIScale)), UIScale,
            skin.Accent, skin.TextMuted);
        WFInstrumentGlass.Window(handle, readout, UIScale);
    }
}
