using System.Numerics;
using Content.Client._WF.CombatConsole;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._WF.Cockpit;

/// <summary>Shows the ship's world position in a fixed-width box that sits between the motion strips.</summary>
public sealed class WFCockpitCoordinates : Control
{
    /// <summary>Characters reserved for each coordinate, which covers positions up to 9999.9 either side of the origin.</summary>
    public const int FieldWidth = 7;

    private readonly Func<Vector2?> _read;
    private readonly Font _small;
    private readonly Font _digits;
    private readonly string _caption = Loc.GetString("wf-cockpit-coordinates");
    private readonly string _x = Loc.GetString("wf-cockpit-coordinates-x");
    private readonly string _y = Loc.GetString("wf-cockpit-coordinates-y");
    private readonly string _offline = Loc.GetString("wf-gauge-offline");
    private Vector2? _position;
    private string _xText = string.Empty;
    private string _yText = string.Empty;
    private float _sampleElapsed;

    /// <summary>Exposes the live position independently of drawing.</summary>
    public Vector2? Reading => _read();

    /// <summary>Creates a passive readout bound to a world position source.</summary>
    public WFCockpitCoordinates(Func<Vector2?> read)
    {
        _read = read;
        MinWidth = 88;
        SetHeight = 44;
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Pass;
        ToolTip = Loc.GetString("wf-cockpit-coordinates-help");
        var cache = IoCManager.Resolve<IResourceCache>();
        _small = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Regular.ttf"), 9);
        _digits = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf"), WFInstrumentText.StripDigitSize);
        Sample();
    }

    /// <summary>Formats both coordinates to one decimal and the same width, so the digits hold still as the ship moves.</summary>
    public static (string X, string Y) Format(Vector2 position)
    {
        var x = WFGaugeScale.Fixed(position.X, 1);
        var y = WFGaugeScale.Fixed(position.Y, 1);
        var width = Math.Max(FieldWidth, Math.Max(x.Length, y.Length));
        return (x.PadLeft(width), y.PadLeft(width));
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _sampleElapsed += args.DeltaSeconds;
        if (_sampleElapsed < 0.1f)
            return;
        _sampleElapsed %= 0.1f;
        Sample();
    }

    /// <summary>Reads and formats the position at ten hertz.</summary>
    private void Sample()
    {
        _position = _read() is { } position && float.IsFinite(position.X) && float.IsFinite(position.Y) ? position : null;
        (_xText, _yText) = _position is { } known ? Format(known) : (string.Empty, string.Empty);
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
            handle.DrawRect(bounds, skin.Glass);

        var inset = 4 * UIScale;
        var width = MathF.Max(0, PixelWidth - 2 * inset);
        var y = WFGaugeScale.StripOffset(PixelHeight, 44 * UIScale);
        WFInstrumentText.DrawFitted(handle, _small, _caption,
            UIBox2.FromDimensions(new Vector2(inset, y + 2 * UIScale), new Vector2(width, 12 * UIScale)),
            UIScale, digital ? skin.TextMuted : skin.Text);

        var top = y + 15 * UIScale;
        var height = 27 * UIScale;
        var edge = 4 * UIScale;
        if (_position == null)
            WFInstrumentText.DrawFitted(handle, _digits, _offline,
                UIBox2.FromDimensions(new Vector2(inset + edge, top), new Vector2(MathF.Max(0, width - 2 * edge), height)),
                UIScale, skin.TextMuted);
        else
        {
            var half = MathF.Max(0, (width - 2 * edge) / 2);
            var color = skin.Accent;
            WFInstrumentText.DrawReading(handle, _digits, _xText, _x,
                UIBox2.FromDimensions(new Vector2(inset + edge, top), new Vector2(half - UIScale, height)),
                UIScale, color, skin.TextMuted, true);
            WFInstrumentText.DrawReading(handle, _digits, _yText, _y,
                UIBox2.FromDimensions(new Vector2(PixelWidth / 2f + UIScale, top), new Vector2(half - UIScale, height)),
                UIScale, color, skin.TextMuted, true);
            handle.DrawLine(new Vector2(PixelWidth / 2f, top + 3 * UIScale),
                new Vector2(PixelWidth / 2f, top + height - 3 * UIScale), skin.EdgeSoft);
        }
        if (!digital)
            WFInstrumentGlass.Window(handle, bounds, UIScale);
    }
}
