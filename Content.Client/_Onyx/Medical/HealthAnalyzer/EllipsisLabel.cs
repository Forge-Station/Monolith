// WOLFGATE(Wolfmed): ported from Onyx for Wolfmed.
using System;
using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._Onyx.Medical.HealthAnalyzer;

/// <summary>
/// A single-line text control that truncates its text with an ellipsis when it does not fit the available width.
/// </summary>
public sealed class EllipsisLabel : Control
{
    private const string Ellipsis = "…";

    private string _text = "";
    private Color? _fontColorOverride;
    private string _drawn = "";
    private int _drawnCacheWidth = -1;

    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (_text == value)
                return;

            _text = value;
            _drawn = "";
            _drawnCacheWidth = -1;
            InvalidateMeasure();
        }
    }

    public Color? FontColorOverride
    {
        get => _fontColorOverride;
        set => _fontColorOverride = value;
    }

    public EllipsisLabel()
    {
        VerticalAlignment = VAlignment.Center;
        MouseFilter = MouseFilterMode.Pass;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var height = GetFont().GetHeight(UIScale);
        return new Vector2(0, height / UIScale);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (_text.Length == 0)
            return;

        var font = GetFont();
        var scale = UIScale;
        var width = PixelSize.X;

        if (_drawnCacheWidth != width || _drawn.Length == 0)
        {
            _drawn = GetFittedText(font, scale, width);
            _drawnCacheWidth = width;
        }

        var y = (PixelSize.Y - font.GetHeight(scale)) / 2f;
        handle.DrawString(font, new Vector2(0, y), _drawn, scale, GetColor());
    }

    private string GetFittedText(Font font, float scale, float maxWidth)
    {
        var ellipsisWidth = GetCharWidth(font, new Rune(Ellipsis[0]), scale);
        var budget = MathF.Max(0, maxWidth - ellipsisWidth);
        var used = 0f;
        var truncated = false;
        var sb = new StringBuilder();

        foreach (var rune in _text.EnumerateRunes())
        {
            if (rune == new Rune('\n'))
                continue;

            var w = GetCharWidth(font, rune, scale);
            if (used + w > budget)
            {
                truncated = true;
                break;
            }

            used += w;
            sb.Append(rune.ToString());
        }

        return truncated ? sb.Append(Ellipsis).ToString() : _text;
    }

    private static float GetCharWidth(Font font, Rune rune, float scale)
    {
        var metrics = font.GetCharMetrics(rune, scale);
        return metrics?.Advance ?? 0f;
    }

    private Font GetFont()
    {
        if (TryGetStyleProperty<Font>("font", out var font))
            return font;

        return UserInterfaceManager.ThemeDefaults.LabelFont;
    }

    private Color GetColor()
    {
        if (_fontColorOverride is { } overrideColor)
            return overrideColor;

        if (TryGetStyleProperty<Color>("font-color", out var styleColor))
            return styleColor;

        return Color.White;
    }

    protected override void StylePropertiesChanged()
    {
        _drawnCacheWidth = -1;
        base.StylePropertiesChanged();
    }
}