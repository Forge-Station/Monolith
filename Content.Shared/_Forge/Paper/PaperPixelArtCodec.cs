using System.Text;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Shared._Forge.Paper;

/// <summary>
/// Compact paper drawings. Converters used to emit one
/// <c>[color=#RRGGBB]█[/color]</c> per pixel (~20-25 chars), so a 20x20 image
/// already blew past the 6k paper limit. This stores the same art as a single
/// <c>[px]</c> tag and expands it on the client.
/// </summary>
/// <remarks>
/// Format:
/// <code>
/// [px w=20 d="rrggbbrrgabb..."][/px]
/// [px w=20 p="ff0000,00ff00" d="001122..."][/px]
/// </code>
/// <c>d</c> is row-major pixels. Without <c>p</c> each pixel is 3, 6 or 8 hex
/// digits. With <c>p</c> each pixel is a palette index (1 hex digit if the
/// palette has at most 16 colors, otherwise 2). Optional <c>s</c> is the
/// on-paper scale in UI pixels per art pixel (default 12).
/// </remarks>
public static class PaperPixelArtCodec
{
    public const string TagName = "px";
    public const int DefaultScale = 12;
    public const int MaxScale = 24;
    public const int MaxWidth = 64;
    public const int MaxHeight = 64;
    public const int MaxPixels = MaxWidth * MaxHeight;

    private const int MinCompressWidth = 2;
    private const int MinCompressHeight = 2;
    private const int MinCompressPixels = 8;
    private const string HexDigits = "0123456789abcdef";

    /// <summary>
    /// Parsed drawing ready for the client to turn into a texture.
    /// </summary>
    public readonly record struct PaperPixelArt(int Width, int Height, int Scale, Color[] Pixels);

    /// <summary>
    /// One stretch of a paper page: either markup text or a decoded drawing.
    /// Drawings are kept out of <c>RichTextLabel</c> so the engine does not
    /// have to layout tall inline controls.
    /// </summary>
    public readonly record struct PaperDocumentPart(string? Text, PaperPixelArt? Art);

    /// <summary>
    /// Replaces runs of <c>[color=...]█[/color]</c> with a compact <c>[px]</c>
    /// tag when that saves characters. Idempotent for already-compact text.
    /// </summary>
    public static string Compress(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf("[color=", StringComparison.OrdinalIgnoreCase) < 0)
            return text;

        var builder = new StringBuilder(text.Length);
        var index = 0;

        while (index < text.Length)
        {
            var colorIndex = text.IndexOf("[color=", index, StringComparison.OrdinalIgnoreCase);
            if (colorIndex < 0)
            {
                builder.Append(text, index, text.Length - index);
                break;
            }

            builder.Append(text, index, colorIndex - index);

            if (!TryCompressBlock(text, colorIndex, out var nextIndex, out var encoded))
            {
                builder.Append(text[colorIndex]);
                index = colorIndex + 1;
                continue;
            }

            builder.Append(encoded);
            index = nextIndex;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Splits paper markup so <c>[px]...[/px]</c> drawings can be rendered as
    /// sibling controls instead of engine inline rich-text widgets.
    /// </summary>
    public static List<PaperDocumentPart> SplitDocument(string markup)
    {
        var parts = new List<PaperDocumentPart>();
        if (string.IsNullOrEmpty(markup))
            return parts;

        const string open = "[px";
        const string close = "[/px]";
        var index = 0;

        while (index < markup.Length)
        {
            var tagStart = IndexOfTagOpen(markup, open, index);
            if (tagStart < 0)
            {
                parts.Add(new PaperDocumentPart(markup[index..], null));
                break;
            }

            if (tagStart > index)
                parts.Add(new PaperDocumentPart(markup[index..tagStart], null));

            var tagEnd = markup.IndexOf(close, tagStart, StringComparison.OrdinalIgnoreCase);
            if (tagEnd < 0)
            {
                parts.Add(new PaperDocumentPart(markup[tagStart..], null));
                break;
            }

            var tag = markup.Substring(tagStart, tagEnd + close.Length - tagStart);
            if (FormattedMessage.TryParse(tag, out var nodes, out _) &&
                FindOpenPxNode(nodes) is { } node &&
                TryDecode(node, out var art))
            {
                parts.Add(new PaperDocumentPart(null, art));
            }
            else
            {
                parts.Add(new PaperDocumentPart(tag, null));
            }

            index = tagEnd + close.Length;
        }

        return parts;
    }

    /// <summary>
    /// Cheap check for a <c>[px]</c> tag. False positives are possible if someone
    /// types that text; <see cref="GetImageSizes"/> confirms a real drawing.
    /// </summary>
    public static bool ContainsPixelArt([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? markup)
    {
        return !string.IsNullOrEmpty(markup) &&
               markup.IndexOf("[px", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Decoded width/height of every valid drawing on the page.
    /// </summary>
    public static List<(int Width, int Height)> GetImageSizes(string? markup)
    {
        var sizes = new List<(int Width, int Height)>();
        if (!ContainsPixelArt(markup))
            return sizes;

        foreach (var part in SplitDocument(markup))
        {
            if (part.Art is { } art)
                sizes.Add((art.Width, art.Height));
        }

        return sizes;
    }

    /// <summary>
    /// Replaces decoded drawings with <c>[image WxH]</c> so admin logs and
    /// console output stay readable instead of dumping hex palettes.
    /// </summary>
    public static string SummarizeForLogs(string markup)
    {
        if (!ContainsPixelArt(markup))
            return markup;

        var builder = new StringBuilder(markup.Length);
        foreach (var part in SplitDocument(markup))
        {
            if (part.Art is { } art)
            {
                builder.Append("[image ");
                builder.Append(art.Width);
                builder.Append('x');
                builder.Append(art.Height);
                builder.Append(']');
                continue;
            }

            if (part.Text != null)
                builder.Append(part.Text);
        }

        return builder.ToString();
    }

    public static string FormatImageSizes(IReadOnlyList<(int Width, int Height)> sizes)
    {
        if (sizes.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        for (var i = 0; i < sizes.Count; i++)
        {
            if (i > 0)
                builder.Append(", ");

            builder.Append(sizes[i].Width);
            builder.Append('x');
            builder.Append(sizes[i].Height);
        }

        return builder.ToString();
    }

    public static string Encode(int width, IReadOnlyList<Color> pixels, int? scale = null)
    {
        if (width <= 0 || pixels.Count == 0 || pixels.Count % width != 0)
            throw new ArgumentException("Pixel buffer must be a non-empty rectangle.");

        var palette = BuildPalette(pixels);
        var useIndexed = ShouldUsePalette(palette.Count, pixels.Count);
        var builder = new StringBuilder(32 + pixels.Count * 6);

        builder.Append('[');
        builder.Append(TagName);
        builder.Append(" w=");
        builder.Append(width);

        if (scale is > 0 && scale != DefaultScale)
        {
            builder.Append(" s=");
            builder.Append(Math.Clamp(scale.Value, 1, MaxScale));
        }

        if (useIndexed)
        {
            builder.Append(" p=\"");
            for (var i = 0; i < palette.Count; i++)
            {
                if (i > 0)
                    builder.Append(',');
                AppendRgb(builder, palette[i]);
            }

            builder.Append('"');
        }

        builder.Append(" d=\"");
        if (useIndexed)
        {
            var digits = palette.Count <= 16 ? 1 : 2;
            foreach (var pixel in pixels)
            {
                var idx = palette.IndexOf(pixel);
                if (digits == 1)
                {
                    builder.Append(HexDigits[idx]);
                }
                else
                {
                    builder.Append(HexDigits[idx >> 4]);
                    builder.Append(HexDigits[idx & 0xF]);
                }
            }
        }
        else
        {
            foreach (var pixel in pixels)
            {
                AppendRgb(builder, pixel);
            }
        }

        builder.Append("\"][/px]");
        return builder.ToString();
    }

    public static bool TryDecode(MarkupNode node, out PaperPixelArt art)
    {
        art = default;
        if (!string.Equals(node.Name, TagName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!TryGetPositiveInt(node, "w", out var width) || width > MaxWidth)
            return false;

        if (!node.Attributes.TryGetValue("d", out var dataParam) || !dataParam.TryGetString(out var data) || data is null)
            return false;

        string? palette = null;
        if (node.Attributes.TryGetValue("p", out var paletteParam))
            paletteParam.TryGetString(out palette);

        var scale = DefaultScale;
        if (TryGetPositiveInt(node, "s", out var parsedScale))
            scale = Math.Clamp(parsedScale, 1, MaxScale);

        return TryDecodeData(width, data, palette, scale, out art);
    }

    public static bool TryDecodeData(int width, string data, string? palette, int scale, out PaperPixelArt art)
    {
        art = default;
        if (width <= 0 || width > MaxWidth)
            return false;

        var stripped = StripIgnored(data);
        if (stripped.Length == 0)
            return false;

        Color[] pixels;
        if (!string.IsNullOrWhiteSpace(palette))
        {
            if (!TryDecodeIndexed(width, stripped, palette, out pixels))
                return false;
        }
        else if (!TryDecodeRaw(width, stripped, out pixels))
        {
            return false;
        }

        var height = pixels.Length / width;
        if (height <= 0 || height > MaxHeight || pixels.Length > MaxPixels)
            return false;

        art = new PaperPixelArt(width, height, scale, pixels);
        return true;
    }

    private static bool TryCompressBlock(string text, int start, out int nextIndex, out string encoded)
    {
        nextIndex = start;
        encoded = string.Empty;

        var rows = new List<List<Color>>();
        var index = start;
        var currentRow = new List<Color>();

        if (!TryReadPixelRun(text, index, out var color, out var count, out index))
            return false;

        AddPixels(currentRow, color, count);

        while (index < text.Length)
        {
            if (TryReadPixelRun(text, index, out color, out count, out var runEnd))
            {
                AddPixels(currentRow, color, count);
                index = runEnd;
                continue;
            }

            // Only treat a newline as a new art row when more pixels follow.
            // Otherwise the break belongs to the rest of the paper.
            if (!TryReadRowBreak(text, index, out var afterBreak))
                break;

            if (!TryReadPixelRun(text, afterBreak, out color, out count, out runEnd))
                break;

            if (currentRow.Count == 0)
                break;

            rows.Add(currentRow);
            currentRow = new List<Color>();
            AddPixels(currentRow, color, count);
            index = runEnd;
        }

        if (currentRow.Count > 0)
            rows.Add(currentRow);

        if (rows.Count < MinCompressHeight)
            return false;

        var width = rows[0].Count;
        if (width < MinCompressWidth || width > MaxWidth)
            return false;

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.Count == width)
                continue;

            // Allow a short final row (partial last line from a converter).
            if (r == rows.Count - 1 && row.Count < width)
            {
                while (row.Count < width)
                    row.Add(Color.Transparent);
                continue;
            }

            return false;
        }

        if (rows.Count > MaxHeight)
            return false;

        var pixels = new List<Color>(width * rows.Count);
        foreach (var row in rows)
            pixels.AddRange(row);

        if (pixels.Count < MinCompressPixels || pixels.Count > MaxPixels)
            return false;

        encoded = Encode(width, pixels);
        if (encoded.Length >= index - start)
            return false;

        nextIndex = index;
        return true;
    }

    private static bool TryReadPixelRun(string text, int index, out Color color, out int count, out int nextIndex)
    {
        color = default;
        count = 0;
        nextIndex = index;

        const string open = "[color=";
        const string close = "[/color]";
        if (index + open.Length + close.Length + 2 > text.Length)
            return false;

        if (string.Compare(text, index, open, 0, open.Length, StringComparison.OrdinalIgnoreCase) != 0)
            return false;

        var valueStart = index + open.Length;
        var bracket = text.IndexOf(']', valueStart);
        if (bracket < 0)
            return false;

        if (!TryParseColorValue(text.Substring(valueStart, bracket - valueStart), out color))
            return false;

        var blocksStart = bracket + 1;
        var n = 0;
        while (blocksStart + n < text.Length && IsBlockChar(text[blocksStart + n]))
            n++;

        if (n == 0)
            return false;

        var closeStart = blocksStart + n;
        if (closeStart + close.Length > text.Length)
            return false;

        if (string.Compare(text, closeStart, close, 0, close.Length, StringComparison.OrdinalIgnoreCase) != 0)
            return false;

        count = n;
        nextIndex = closeStart + close.Length;
        return true;
    }

    private static bool TryReadRowBreak(string text, int index, out int nextIndex)
    {
        nextIndex = index;
        if (index >= text.Length)
            return false;

        if (text[index] == '\r')
        {
            index++;
            if (index < text.Length && text[index] == '\n')
                index++;
            nextIndex = index;
            return true;
        }

        if (text[index] == '\n')
        {
            nextIndex = index + 1;
            return true;
        }

        return false;
    }

    private static void AddPixels(List<Color> row, Color color, int count)
    {
        for (var i = 0; i < count; i++)
            row.Add(color);
    }

    private static bool IsBlockChar(char c)
    {
        return c is '█' or '■' or '▓';
    }

    private static bool TryParseColorValue(string value, out Color color)
    {
        value = value.Trim();
        if (value.Length == 0)
        {
            color = default;
            return false;
        }

        if (value[0] == '#')
        {
            var parsed = Color.TryFromHex(value);
            if (parsed != null)
            {
                color = parsed.Value;
                return true;
            }

            color = default;
            return false;
        }

        if (Color.TryFromName(value, out color))
            return true;

        var fallback = Color.TryFromHex("#" + value);
        if (fallback != null)
        {
            color = fallback.Value;
            return true;
        }

        color = default;
        return false;
    }

    private static List<Color> BuildPalette(IReadOnlyList<Color> pixels)
    {
        var palette = new List<Color>();
        foreach (var pixel in pixels)
        {
            if (!palette.Contains(pixel))
                palette.Add(pixel);
        }

        return palette;
    }

    private static bool ShouldUsePalette(int unique, int pixelCount)
    {
        if (unique <= 0 || unique > 256)
            return false;

        var indexChars = unique <= 16 ? 1 : 2;
        var indexedCost = unique * 7 + indexChars * pixelCount;
        var rawCost = 6 * pixelCount;
        return indexedCost < rawCost;
    }

    private static bool TryDecodeRaw(int width, string data, out Color[] pixels)
    {
        pixels = [];
        foreach (var charsPerPixel in new[] { 6, 8, 3 })
        {
            if (data.Length % charsPerPixel != 0)
                continue;

            var count = data.Length / charsPerPixel;
            if (count == 0 || count % width != 0)
                continue;

            pixels = new Color[count];
            var ok = true;
            for (var i = 0; i < count; i++)
            {
                var slice = data.Substring(i * charsPerPixel, charsPerPixel);
                if (!TryReadRgb(slice, out pixels[i]))
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
                return true;
        }

        pixels = [];
        return false;
    }

    private static bool TryDecodeIndexed(int width, string data, string paletteText, out Color[] pixels)
    {
        pixels = [];
        var palette = new List<Color>();
        foreach (var part in paletteText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryReadRgb(part, out var color))
                return false;

            palette.Add(color);
        }

        if (palette.Count == 0 || palette.Count > 256)
            return false;

        var digits = palette.Count <= 16 ? 1 : 2;
        if (data.Length % digits != 0)
            return false;

        var count = data.Length / digits;
        if (count == 0 || count % width != 0)
            return false;

        pixels = new Color[count];
        for (var i = 0; i < count; i++)
        {
            var slice = data.Substring(i * digits, digits);
            if (!TryReadHexIndex(slice, out var idx) || idx >= palette.Count)
                return false;

            pixels[i] = palette[idx];
        }

        return true;
    }

    private static bool TryReadRgb(string hex, out Color color)
    {
        color = default;
        if (string.IsNullOrEmpty(hex) || hex.Length > 9)
            return false;

        if (hex[0] == '#')
        {
            var parsedPrefixed = Color.TryFromHex(hex);
            if (parsedPrefixed == null)
                return false;
            color = parsedPrefixed.Value;
            return true;
        }

        if (hex.Length > 8)
            return false;

        var parsed = Color.TryFromHex("#" + hex);
        if (parsed == null)
            return false;

        color = parsed.Value;
        return true;
    }

    private static bool TryReadHexIndex(string hex, out int value)
    {
        value = 0;
        for (var i = 0; i < hex.Length; i++)
        {
            var nibble = HexValue(hex[i]);
            if (nibble < 0)
                return false;
            value = (value << 4) | nibble;
        }

        return true;
    }

    private static int HexValue(char c)
    {
        return c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1
        };
    }

    private static void AppendRgb(StringBuilder builder, Color color)
    {
        AppendByte(builder, color.RByte);
        AppendByte(builder, color.GByte);
        AppendByte(builder, color.BByte);
    }

    private static void AppendByte(StringBuilder builder, byte value)
    {
        builder.Append(HexDigits[value >> 4]);
        builder.Append(HexDigits[value & 0xF]);
    }

    private static string StripIgnored(string data)
    {
        var builder = new StringBuilder(data.Length);
        foreach (var c in data)
        {
            if (char.IsWhiteSpace(c))
                continue;
            builder.Append(c);
        }

        return builder.ToString();
    }

    private static bool TryGetPositiveInt(MarkupNode node, string name, out int value)
    {
        value = 0;
        if (!node.Attributes.TryGetValue(name, out var param) || !param.TryGetLong(out var parsed) || parsed is null or <= 0)
            return false;

        if (parsed.Value > int.MaxValue)
            return false;

        value = (int)parsed.Value;
        return true;
    }

    private static int IndexOfTagOpen(string markup, string open, int start)
    {
        while (start < markup.Length)
        {
            var found = markup.IndexOf(open, start, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return -1;

            var after = found + open.Length;
            if (after >= markup.Length)
                return found;

            var next = markup[after];
            if (next is ' ' or '=' or ']' or '\t' or '\r' or '\n')
                return found;

            start = found + 1;
        }

        return -1;
    }

    private static MarkupNode? FindOpenPxNode(List<MarkupNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (!node.Closing && string.Equals(node.Name, TagName, StringComparison.OrdinalIgnoreCase))
                return node;
        }

        return null;
    }
}
