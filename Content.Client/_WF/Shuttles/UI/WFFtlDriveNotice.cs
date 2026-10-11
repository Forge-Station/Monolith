using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Client._WF.Stylesheets;
using Content.Client.Resources;
using Content.Shared._WF.Shuttles;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Shuttles.UI;

/// <summary>A boxed notice in the skin's danger colour that says why a ship cannot jump; it takes no room while it can.</summary>
public sealed class WFFtlDriveNotice : Control
{
    private const float NoticeHeight = 48;
    private const int FontSize = 14;

    /// <summary>Width of the lamp bar down the left edge, in UI units.</summary>
    private const float BarWidth = 6;

    private readonly List<string> _lines = new();
    private string _text = string.Empty;
    private Font? _font;
    private string? _fontSkin;
    private (string?, string, int, int, float) _layoutKey;
    private float _layoutScale;

    /// <summary>Starts hidden; the page shows it from the server's drive status.</summary>
    public WFFtlDriveNotice()
    {
        Name = "WfFtlDriveNotice";
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Ignore;
        RectClipContent = true;
        Visible = false;
    }

    /// <summary>The wording on display, empty while the notice is hidden.</summary>
    public string Text => _text;

    /// <summary>The colour the notice is drawn in, the active skin's danger colour.</summary>
    public Color Tint => WFInstrumentTheme.Skin.Danger;

    /// <summary>The reason a ship with this drive status cannot jump, or null when it can.</summary>
    public static string? Reason(WFFtlDriveStatus status) => status switch
    {
        WFFtlDriveStatus.Missing => Loc.GetString("shuttle-console-no-powered-ftl-drive"),
        WFFtlDriveStatus.Unpowered => Loc.GetString("shuttle-console-ftl-drive-unpowered"),
        _ => null,
    };

    /// <summary>Shows the reason the drive cannot jump, or hides the notice for a ready drive.</summary>
    public void SetStatus(WFFtlDriveStatus status)
    {
        _text = Reason(status) ?? string.Empty;
        Visible = _text.Length > 0;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize) => new(0, NoticeHeight);

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var skin = WFInstrumentTheme.Skin;
        var danger = Tint;
        var box = new UIBox2(Vector2.Zero, PixelSize);
        var bar = BarWidth * UIScale;
        if (WFInstrumentTheme.Digital)
            DrawDigital(handle, box, skin, danger, bar);
        else
            DrawAnalog(handle, box, skin, danger, bar);

        if (_fontSkin != skin.Id)
        {
            _font = IoCManager.Resolve<IResourceCache>().GetFont(skin.MenuFonts, FontSize);
            _fontSkin = skin.Id;
        }
        var area = new UIBox2(box.Left + bar + 10 * UIScale, box.Top + 4 * UIScale, box.Right - 8 * UIScale, box.Bottom - 4 * UIScale);
        var scale = Layout(handle, _font!, area.Width, area.Height);
        if (scale == 0)
            return;

        var lineHeight = _font!.GetHeight(scale);
        var y = area.Top + (area.Height - lineHeight * _lines.Count) / 2f;
        foreach (var line in _lines)
        {
            var x = area.Left + (area.Width - handle.GetDimensions(_font, line, scale).X) / 2f;
            if (!WFInstrumentTheme.Digital)
                handle.DrawString(_font, new Vector2(x, y + UIScale), line, scale, skin.Ink);
            handle.DrawString(_font, new Vector2(x, y), line, scale, danger);
            y += lineHeight;
        }
    }

    /// <summary>A recessed glass window with a lit bar down its edge.</summary>
    private void DrawAnalog(DrawingHandleScreen handle, UIBox2 box, WolfgateSkin skin, Color danger, float bar)
    {
        WFConsoleMetal.Bevel(handle, box, UIScale, Color.InterpolateBetween(skin.Ink, danger, 0.2f), skin.EdgeLight, inset: true);
        var frame = 2 * UIScale;
        handle.DrawRect(new UIBox2(box.Left + frame, box.Top + frame, box.Right - frame, box.Bottom - frame), danger, false);
        var inset = 4 * UIScale;
        handle.DrawRect(new UIBox2(box.Left + inset, box.Top + inset, box.Left + inset + bar, box.Bottom - inset), danger);
    }

    /// <summary>A clipped-corner glass panel with a lit bar down its edge.</summary>
    private void DrawDigital(DrawingHandleScreen handle, UIBox2 box, WolfgateSkin skin, Color danger, float bar)
    {
        WFConsoleDigital.Panel(handle, box, UIScale, Color.InterpolateBetween(skin.Glass, danger, 0.16f), danger, brackets: false, corner: 6);
        var inset = 4 * UIScale;
        handle.DrawRect(new UIBox2(box.Left + inset, box.Top + inset, box.Left + inset + bar, box.Bottom - inset), danger);
    }

    /// <summary>Fits the wording to the box in as few lines as the largest readable size allows, and returns that size.</summary>
    private float Layout(DrawingHandleScreen handle, Font font, float width, float height)
    {
        var key = (_fontSkin, _text, (int) width, (int) height, UIScale);
        if (key == _layoutKey)
            return _layoutScale;

        _layoutKey = key;
        _layoutScale = 0;
        for (var scale = WFGaugeScale.FontScale(UIScale); scale > 0; scale = WFGaugeScale.FontScale(scale - 0.125f))
        {
            if (!Wrap(handle, font, scale, width) || _lines.Count * font.GetHeight(scale) > height)
                continue;

            _layoutScale = scale;
            break;
        }
        if (_layoutScale == 0)
            _lines.Clear();
        return _layoutScale;
    }

    /// <summary>Breaks the wording at spaces into lines no wider than the box, or fails if a word is wider on its own.</summary>
    private bool Wrap(DrawingHandleScreen handle, Font font, float scale, float width)
    {
        _lines.Clear();
        var line = string.Empty;
        foreach (var word in _text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = line.Length == 0 ? word : line + " " + word;
            if (handle.GetDimensions(font, next, scale).X <= width)
            {
                line = next;
                continue;
            }
            if (line.Length == 0)
                return false;

            _lines.Add(line);
            line = word;
            if (handle.GetDimensions(font, line, scale).X > width)
                return false;
        }
        if (line.Length > 0)
            _lines.Add(line);
        return _lines.Count > 0;
    }
}
