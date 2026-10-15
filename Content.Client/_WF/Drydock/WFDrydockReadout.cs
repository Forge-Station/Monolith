using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Drydock;

/// <summary>One line of a readout's list: a name, and its amount on the right, tinted when set.</summary>
public readonly record struct WFDrydockReadoutRow(string Name, string Amount, Color? Tint = null);

/// <summary>
/// A recessed glass readout: a large figure over its caption, in a line a caption beside a figure, or a caption over a
/// list of rows.
/// </summary>
public sealed class WFDrydockReadout : Control
{
    /// <summary>Height of a line readout.</summary>
    private const float LineHeight = 30;

    /// <summary>Height of a list row.</summary>
    private const float RowHeight = 17;

    /// <summary>Space taken by the caption above a list.</summary>
    private const float ListTop = 20;

    private readonly string _caption;
    private readonly bool _counter;
    private readonly Font _digits;
    private Font? _small;
    private Font? _row;
    private IReadOnlyList<WFDrydockReadoutRow>? _rows;

    /// <summary>Text shown in a list readout with no rows.</summary>
    public string EmptyText { get; set; } = string.Empty;
    private string? _fontSkin;

    /// <summary>Figure shown in the readout.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Figure color; null draws it in the skin's accent.</summary>
    public Color? Tint { get; set; }

    /// <summary>Whether the figure shows a live value; an unlit readout draws it muted.</summary>
    public bool Lit { get; set; } = true;

    /// <summary>Creates a readout; a counter stacks a large figure over its caption.</summary>
    public WFDrydockReadout(string caption, bool counter = false)
    {
        _caption = Loc.GetString(caption);
        _counter = counter;
        HorizontalExpand = true;
        SetHeight = counter ? 58 : LineHeight;
        MinWidth = 64;
        MouseFilter = MouseFilterMode.Ignore;
        var bold = IoCManager.Resolve<IResourceCache>().GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf");
        _digits = new VectorFont(bold, counter ? 24 : 14);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var skin = WFInstrumentTheme.Skin;
        if (_fontSkin != skin.Id)
        {
            _small = IoCManager.Resolve<IResourceCache>().GetFont(skin.MenuFonts, 9);
            _row = IoCManager.Resolve<IResourceCache>().GetFont(skin.MenuFonts, 11);
            _fontSkin = skin.Id;
        }

        var bounds = PixelSizeBox;
        if (WFInstrumentTheme.Digital)
            WFConsoleDigital.Panel(handle, bounds, UIScale, skin.Glass, skin.EdgeSoft, brackets: false, corner: 4);
        else
            WFConsoleMetal.Bevel(handle, bounds, UIScale, skin.Ink, skin.EdgeLight, inset: true);

        var color = Lit ? Tint ?? skin.Accent : skin.TextMuted;
        var inset = 5 * UIScale;
        if (_rows != null)
        {
            DrawRows(handle, _rows, color, skin.TextMuted);
        }
        else if (_counter)
        {
            var captionHeight = 13 * UIScale;
            var figure = new UIBox2(inset, inset, PixelWidth - inset, PixelHeight - captionHeight - 2 * UIScale);
            if (Lit)
                handle.DrawRect(figure, color.WithAlpha(0.05f));
            WFInstrumentText.DrawFitted(handle, _digits, Value, figure, UIScale, color);
            Fitted(handle, _small!, _caption, new UIBox2(inset, PixelHeight - captionHeight - UIScale,
                PixelWidth - inset, PixelHeight - 2 * UIScale), skin.TextMuted, center: true);
        }
        else
        {
            var captionWidth = MathF.Min(PixelWidth * 0.42f, handle.GetDimensions(_small!, _caption, UIScale).X + 10 * UIScale);
            Fitted(handle, _small!, _caption, new UIBox2(inset + 2 * UIScale, 0, captionWidth, PixelHeight), skin.TextMuted, center: false);
            var figure = new UIBox2(captionWidth, 4 * UIScale, PixelWidth - inset - 2 * UIScale, PixelHeight - 4 * UIScale);
            Right(handle, _digits, Value, figure, color);
        }

        if (!WFInstrumentTheme.Digital)
            WFInstrumentGlass.Window(handle, bounds, UIScale);
    }

    /// <summary>
    /// Lists rows under the caption instead of a figure, at a readable size, growing the readout to fit them; null goes
    /// back to the figure.
    /// </summary>
    public void SetRows(IReadOnlyList<WFDrydockReadoutRow>? rows)
    {
        _rows = rows;
        var height = rows == null ? LineHeight : ListTop + Math.Max(1, rows.Count) * RowHeight + 6;
        if (!MathHelper.CloseTo(SetHeight, height))
            SetHeight = height;
    }

    /// <summary>Draws the caption at the top left and each row beneath it, its name left and its amount right.</summary>
    private void DrawRows(DrawingHandleScreen handle, IReadOnlyList<WFDrydockReadoutRow> rows, Color color, Color muted)
    {
        var inset = 7 * UIScale;
        Fitted(handle, _small!, _caption, new UIBox2(inset, 0, PixelWidth - inset, ListTop * UIScale), muted, center: false);
        if (rows.Count == 0)
        {
            var empty = new UIBox2(inset, ListTop * UIScale, PixelWidth - inset, (ListTop + RowHeight) * UIScale);
            Fitted(handle, _row!, EmptyText, empty, muted, center: false);
            return;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var top = (ListTop + i * RowHeight) * UIScale;
            var line = new UIBox2(inset, top, PixelWidth - inset, top + RowHeight * UIScale);
            var rowColor = rows[i].Tint ?? color;
            var amountWidth = handle.GetDimensions(_row!, rows[i].Amount, UIScale).X;
            var y = line.Center.Y - _row!.GetHeight(UIScale) / 2f;
            handle.DrawString(_row, new Vector2(line.Right - amountWidth, y), rows[i].Amount, UIScale, rowColor);
            Fitted(handle, _row, rows[i].Name, new UIBox2(line.Left, line.Top, line.Right - amountWidth - 8 * UIScale, line.Bottom),
                rowColor, center: false);
        }
    }

    private void Fitted(DrawingHandleScreen handle, Font font, string text, UIBox2 box, Color color, bool center)
    {
        var scale = WFInstrumentText.FitScale(handle, font, text, UIScale, box.Width, box.Height);
        if (scale == 0)
            return;
        var width = handle.GetDimensions(font, text, scale).X;
        var x = center ? box.Center.X - width / 2 : box.Left;
        handle.DrawString(font, new Vector2(x, box.Center.Y - font.GetHeight(scale) / 2f), text, scale, color);
    }

    /// <summary>Draws figures right-aligned in a box, as large as it allows.</summary>
    private void Right(DrawingHandleScreen handle, Font font, string text, UIBox2 box, Color color)
    {
        var scale = WFInstrumentText.FitDigits(handle, font, text, UIScale, box.Width, box.Height);
        if (scale == 0)
            return;
        var width = handle.GetDimensions(font, text, scale).X;
        handle.DrawString(font, new Vector2(box.Right - width, box.Center.Y - font.GetHeight(scale) / 2f), text, scale, color);
    }
}
