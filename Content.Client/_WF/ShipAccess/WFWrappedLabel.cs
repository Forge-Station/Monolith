using System.Linq;
using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.ShipAccess;

/// <summary>A label that breaks its text at spaces to fit the width it is given, so a notice is never clipped.</summary>
public sealed class WFWrappedLabel : Label
{
    private readonly List<(string Text, int Width)> _lines = new();
    private string? _wrappedText;
    private Font? _wrappedFont;
    private float _wrappedScale;
    private int _wrappedWidth = -1;

    private Font ActualFont => FontOverride
        ?? (TryGetStyleProperty<Font>(StylePropertyFont, out var font) ? font : UserInterfaceManager.ThemeDefaults.LabelFont);

    private Color ActualFontColor => FontColorOverride
        ?? (TryGetStyleProperty<Color>(StylePropertyFontColor, out var color) ? color : Color.White);

    /// <summary>The lines the text is broken into at the control's current size.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            Wrap(ActualFont, UIScale, DrawLimit());
            return _lines.Select(line => line.Text).ToList();
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var font = ActualFont;
        var scale = UIScale;
        var limit = float.IsFinite(availableSize.X) && availableSize.X > 0
            ? (int) Math.Min(Math.Floor(availableSize.X * scale), int.MaxValue / 2)
            : int.MaxValue;
        Wrap(font, scale, limit);
        var widest = _lines.Count == 0 ? 0 : _lines.Max(line => line.Width);
        return new Vector2(widest / scale, TextHeight(font, scale) / scale);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (PixelSize.X <= 0)
            return;

        var font = ActualFont;
        var scale = UIScale;
        Wrap(font, scale, DrawLimit());
        if (_lines.Count == 0)
            return;

        var color = ActualFontColor;
        var lineHeight = font.GetLineHeight(scale);
        var top = VAlign switch
        {
            VAlignMode.Bottom => PixelSize.Y - TextHeight(font, scale),
            VAlignMode.Center or VAlignMode.Fill => (PixelSize.Y - TextHeight(font, scale)) / 2,
            _ => 0,
        };
        for (var i = 0; i < _lines.Count; i++)
        {
            var (text, width) = _lines[i];
            var left = Align switch
            {
                AlignMode.Right => PixelSize.X - width,
                AlignMode.Left => 0,
                _ => (PixelSize.X - width) / 2,
            };
            var baseline = new Vector2(left, top + font.GetAscent(scale) + lineHeight * i);
            foreach (var rune in text.EnumerateRunes())
                baseline += new Vector2(font.DrawChar(handle, rune, baseline, scale, color), 0);
        }
    }

    /// <summary>The width to wrap at when drawing: the width the last measure wrapped at, unless the control got less.</summary>
    private int DrawLimit()
    {
        return _wrappedWidth > 0 && _wrappedWidth < PixelSize.X ? _wrappedWidth : PixelSize.X;
    }

    private int TextHeight(Font font, float scale)
    {
        return _lines.Count == 0 ? 0 : font.GetHeight(scale) + font.GetLineHeight(scale) * (_lines.Count - 1);
    }

    private static int Advance(Font font, Rune rune, float scale)
    {
        return font.GetCharMetrics(rune, scale)?.Advance ?? 0;
    }

    /// <summary>Breaks the text into lines of at most <paramref name="limit"/> pixels; a word wider than that is split.</summary>
    private void Wrap(Font font, float scale, int limit)
    {
        var text = Text ?? string.Empty;
        if (_wrappedText == text && _wrappedFont == font && _wrappedScale == scale && _wrappedWidth == limit)
            return;

        _wrappedText = text;
        _wrappedFont = font;
        _wrappedScale = scale;
        _wrappedWidth = limit;
        _lines.Clear();
        if (text.Length == 0)
            return;

        var space = Advance(font, new Rune(' '), scale);
        var line = new StringBuilder();
        var lineWidth = 0;
        var word = new StringBuilder();
        var wordWidth = 0;

        void EndLine()
        {
            _lines.Add((line.ToString(), lineWidth));
            line.Clear();
            lineWidth = 0;
        }

        void AddWord()
        {
            if (word.Length == 0)
                return;

            var wordText = word.ToString();
            var width = wordWidth;
            word.Clear();
            wordWidth = 0;
            if (line.Length > 0)
            {
                if (lineWidth + space + width <= limit)
                {
                    line.Append(' ').Append(wordText);
                    lineWidth += space + width;
                    return;
                }

                EndLine();
            }

            if (width <= limit)
            {
                line.Append(wordText);
                lineWidth = width;
                return;
            }

            foreach (var rune in wordText.EnumerateRunes())
            {
                var advance = Advance(font, rune, scale);
                if (line.Length > 0 && lineWidth + advance > limit)
                    EndLine();

                line.Append(rune.ToString());
                lineWidth += advance;
            }
        }

        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == '\n')
            {
                AddWord();
                EndLine();
            }
            else if (rune.Value == ' ')
                AddWord();
            else
            {
                word.Append(rune.ToString());
                wordWidth += Advance(font, rune, scale);
            }
        }

        AddWord();
        if (line.Length > 0)
            EndLine();
    }
}
