using System.Diagnostics.CodeAnalysis;
using System.IO;
using Content.Shared._Forge.Paper;
using Robust.Client.Utility;
using Robust.Shared.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;

namespace Content.Client._Forge.Paper;

public enum PaperPixelArtImportError
{
    None,
    InvalidImage,
    FileTooLarge,
    NoSpace
}

/// <summary>
/// Turns a player-picked image into compact <c>[px]</c> markup that fits the
/// remaining paper character budget.
/// </summary>
public static class PaperPixelArtImporter
{
    public const int MaxFileBytes = 8 * 1024 * 1024;
    private const int MaxDecodeSide = 2048;
    private const int MinSide = 2;
    private const int MaxPaletteColors = 16;

    public static bool TryImport(
        Stream stream,
        int maxChars,
        [NotNullWhen(true)] out string? markup,
        out PaperPixelArtImportError error)
    {
        markup = null;
        error = PaperPixelArtImportError.None;

        if (maxChars <= 0)
        {
            error = PaperPixelArtImportError.NoSpace;
            return false;
        }

        if (stream.CanSeek && stream.Length > MaxFileBytes)
        {
            error = PaperPixelArtImportError.FileTooLarge;
            return false;
        }

        Image<Rgba32> image;
        try
        {
            // Copy first: WebP/JPEG decoders need a seekable buffer, and some OS file streams are not.
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            if (copy.Length > MaxFileBytes)
            {
                error = PaperPixelArtImportError.FileTooLarge;
                return false;
            }

            copy.Position = 0;
            image = Image.Load<Rgba32>(copy);
        }
        catch (Exception)
        {
            error = PaperPixelArtImportError.InvalidImage;
            return false;
        }

        using (image)
        {
            if (image.Width <= 0 || image.Height <= 0)
            {
                error = PaperPixelArtImportError.InvalidImage;
                return false;
            }

            if (image.Width > MaxDecodeSide || image.Height > MaxDecodeSide)
            {
                error = PaperPixelArtImportError.FileTooLarge;
                return false;
            }

            if (!TryEncode(image, maxChars, out markup))
            {
                error = PaperPixelArtImportError.NoSpace;
                return false;
            }
        }

        return true;
    }

    public static bool TryEncode(Image<Rgba32> source, int maxChars, [NotNullWhen(true)] out string? markup)
    {
        markup = null;
        if (maxChars <= 0 || source.Width <= 0 || source.Height <= 0)
            return false;

        var maxSide = Math.Min(
            Math.Max(source.Width, source.Height),
            Math.Max(PaperPixelArtCodec.MaxWidth, PaperPixelArtCodec.MaxHeight));

        var lo = MinSide;
        var hi = Math.Max(MinSide, maxSide);
        string? best = null;

        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            FitSize(source.Width, source.Height, mid, out var width, out var height);
            var candidate = EncodeResampled(source, width, height);
            if (candidate.Length <= maxChars)
            {
                best = candidate;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        markup = best;
        return best != null;
    }

    private static string EncodeResampled(Image<Rgba32> source, int width, int height)
    {
        width = Math.Clamp(width, 1, PaperPixelArtCodec.MaxWidth);
        height = Math.Clamp(height, 1, PaperPixelArtCodec.MaxHeight);
        var pixels = Resample(source, width, height);
        Quantize(pixels, MaxPaletteColors);
        return PaperPixelArtCodec.Encode(width, pixels, PickScale(width, height));
    }

    private static Color[] Resample(Image<Rgba32> source, int width, int height)
    {
        var srcPixels = source.GetPixelSpan();
        var srcWidth = source.Width;
        var srcHeight = source.Height;
        var pixels = new Color[width * height];

        if (width == srcWidth && height == srcHeight)
        {
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = ToPaperColor(srcPixels[i]);
            return pixels;
        }

        // Area-average each destination pixel. Point sampling skipped thin ink
        // strokes and left only scattered dots on paper.
        for (var y = 0; y < height; y++)
        {
            var y0 = y * srcHeight / height;
            var y1 = Math.Max(y0 + 1, (y + 1) * srcHeight / height);
            for (var x = 0; x < width; x++)
            {
                var x0 = x * srcWidth / width;
                var x1 = Math.Max(x0 + 1, (x + 1) * srcWidth / width);
                pixels[y * width + x] = SampleBlock(srcPixels, srcWidth, x0, x1, y0, y1);
            }
        }

        return pixels;
    }

    private static Color SampleBlock(ReadOnlySpan<Rgba32> src, int srcWidth, int x0, int x1, int y0, int y1)
    {
        var sumR = 0;
        var sumG = 0;
        var sumB = 0;
        var count = 0;
        var minLuma = 255;
        byte darkR = 255;
        byte darkG = 255;
        byte darkB = 255;

        for (var sy = y0; sy < y1; sy++)
        {
            var row = sy * srcWidth;
            for (var sx = x0; sx < x1; sx++)
            {
                var paper = ToPaperColor(src[row + sx]);
                sumR += paper.RByte;
                sumG += paper.GByte;
                sumB += paper.BByte;
                count++;

                var luma = Luma(paper.RByte, paper.GByte, paper.BByte);
                if (luma >= minLuma)
                    continue;

                minLuma = luma;
                darkR = paper.RByte;
                darkG = paper.GByte;
                darkB = paper.BByte;
            }
        }

        if (count == 0)
            return Color.White;

        var avgR = (sumR + count / 2) / count;
        var avgG = (sumG + count / 2) / count;
        var avgB = (sumB + count / 2) / count;
        var avgLuma = Luma(avgR, avgG, avgB);
        var contrast = avgLuma - minLuma;
        if (contrast <= 24)
            return new Color((byte)avgR, (byte)avgG, (byte)avgB);

        // Bias toward the darkest sample so thin pencil/ink lines stay visible.
        var t = Math.Clamp(contrast / 140f, 0.2f, 0.75f);
        return new Color(
            LerpByte(avgR, darkR, t),
            LerpByte(avgG, darkG, t),
            LerpByte(avgB, darkB, t));
    }

    private static void Quantize(Color[] pixels, int maxColors)
    {
        if (CountUnique(pixels) <= maxColors)
            return;

        if (IsMostlyGray(pixels))
        {
            QuantizeGray(pixels, maxColors);
            return;
        }

        var palette = MedianCut(pixels, maxColors);
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = Nearest(palette, pixels[i]);
    }

    private static void QuantizeGray(Color[] pixels, int levels)
    {
        levels = Math.Max(2, levels);
        var last = levels - 1;
        for (var i = 0; i < pixels.Length; i++)
        {
            var luma = Luma(pixels[i].RByte, pixels[i].GByte, pixels[i].BByte);
            var step = (luma * last + 127) / 255;
            var gray = (byte)(step * 255 / last);
            pixels[i] = new Color(gray, gray, gray);
        }
    }

    private static List<Color> MedianCut(Color[] pixels, int maxColors)
    {
        var groups = new List<List<Color>>(maxColors) { new(pixels) };
        while (groups.Count < maxColors)
        {
            var splitAt = -1;
            var bestRange = 0;
            for (var i = 0; i < groups.Count; i++)
            {
                var range = ChannelRange(groups[i]);
                if (range <= bestRange || groups[i].Count < 2)
                    continue;

                bestRange = range;
                splitAt = i;
            }

            if (splitAt < 0)
                break;

            SplitGroup(groups, splitAt);
        }

        var palette = new List<Color>(groups.Count);
        foreach (var group in groups)
            palette.Add(Average(group));
        return palette;
    }

    private static void SplitGroup(List<List<Color>> groups, int index)
    {
        var group = groups[index];
        var channel = WidestChannel(group);
        group.Sort((a, b) => Channel(a, channel).CompareTo(Channel(b, channel)));
        var mid = Math.Max(1, group.Count / 2);
        var left = group.GetRange(0, mid);
        var right = group.GetRange(mid, group.Count - mid);
        groups[index] = left;
        groups.Insert(index + 1, right);
    }

    private static int ChannelRange(List<Color> group)
    {
        var (minR, minG, minB, maxR, maxG, maxB) = Bounds(group);
        return Math.Max(maxR - minR, Math.Max(maxG - minG, maxB - minB));
    }

    private static int WidestChannel(List<Color> group)
    {
        var (minR, minG, minB, maxR, maxG, maxB) = Bounds(group);
        var rangeR = maxR - minR;
        var rangeG = maxG - minG;
        var rangeB = maxB - minB;
        if (rangeG >= rangeR && rangeG >= rangeB)
            return 1;
        if (rangeB >= rangeR && rangeB >= rangeG)
            return 2;
        return 0;
    }

    private static (int minR, int minG, int minB, int maxR, int maxG, int maxB) Bounds(List<Color> group)
    {
        var minR = 255;
        var minG = 255;
        var minB = 255;
        var maxR = 0;
        var maxG = 0;
        var maxB = 0;
        foreach (var color in group)
        {
            minR = Math.Min(minR, color.RByte);
            minG = Math.Min(minG, color.GByte);
            minB = Math.Min(minB, color.BByte);
            maxR = Math.Max(maxR, color.RByte);
            maxG = Math.Max(maxG, color.GByte);
            maxB = Math.Max(maxB, color.BByte);
        }

        return (minR, minG, minB, maxR, maxG, maxB);
    }

    private static Color Average(List<Color> group)
    {
        var sumR = 0;
        var sumG = 0;
        var sumB = 0;
        foreach (var color in group)
        {
            sumR += color.RByte;
            sumG += color.GByte;
            sumB += color.BByte;
        }

        var n = Math.Max(1, group.Count);
        return new Color(
            (byte)((sumR + n / 2) / n),
            (byte)((sumG + n / 2) / n),
            (byte)((sumB + n / 2) / n));
    }

    private static Color Nearest(List<Color> palette, Color color)
    {
        var best = palette[0];
        var bestDist = int.MaxValue;
        foreach (var candidate in palette)
        {
            var dr = candidate.RByte - color.RByte;
            var dg = candidate.GByte - color.GByte;
            var db = candidate.BByte - color.BByte;
            var dist = dr * dr + dg * dg + db * db;
            if (dist >= bestDist)
                continue;

            bestDist = dist;
            best = candidate;
        }

        return best;
    }

    private static int CountUnique(Color[] pixels)
    {
        var seen = new HashSet<int>();
        foreach (var pixel in pixels)
            seen.Add((pixel.RByte << 16) | (pixel.GByte << 8) | pixel.BByte);
        return seen.Count;
    }

    private static bool IsMostlyGray(Color[] pixels)
    {
        var gray = 0;
        foreach (var pixel in pixels)
        {
            if (Math.Abs(pixel.RByte - pixel.GByte) <= 18 &&
                Math.Abs(pixel.GByte - pixel.BByte) <= 18 &&
                Math.Abs(pixel.RByte - pixel.BByte) <= 18)
                gray++;
        }

        return gray * 10 >= pixels.Length * 9;
    }

    private static int PickScale(int width, int height)
    {
        var maxSide = Math.Max(width, height);
        if (maxSide <= 16)
            return PaperPixelArtCodec.DefaultScale;
        if (maxSide <= 32)
            return 8;
        if (maxSide <= 48)
            return 6;
        return 4;
    }

    private static byte Channel(Color color, int channel)
    {
        return channel switch
        {
            1 => color.GByte,
            2 => color.BByte,
            _ => color.RByte
        };
    }

    private static int Luma(int r, int g, int b)
    {
        return (r * 54 + g * 183 + b * 19) / 256;
    }

    private static byte LerpByte(int from, int to, float t)
    {
        return (byte)Math.Clamp((int)Math.Round(from + (to - from) * t), 0, 255);
    }

    private static void FitSize(int sourceWidth, int sourceHeight, int maxSide, out int width, out int height)
    {
        maxSide = Math.Max(1, maxSide);
        if (sourceWidth >= sourceHeight)
        {
            width = Math.Min(maxSide, sourceWidth);
            height = Math.Max(1, (int)Math.Round(sourceHeight * (width / (float)sourceWidth)));
        }
        else
        {
            height = Math.Min(maxSide, sourceHeight);
            width = Math.Max(1, (int)Math.Round(sourceWidth * (height / (float)sourceHeight)));
        }

        width = Math.Clamp(width, 1, PaperPixelArtCodec.MaxWidth);
        height = Math.Clamp(height, 1, PaperPixelArtCodec.MaxHeight);
    }

    private static Color ToPaperColor(Rgba32 pixel)
    {
        if (pixel.A < 16)
            return Color.White;

        if (pixel.A >= 250)
            return new Color(pixel.R, pixel.G, pixel.B);

        var alpha = pixel.A / 255f;
        var inv = 1f - alpha;
        return new Color(
            (byte)Math.Clamp((int)Math.Round(pixel.R * alpha + 255f * inv), 0, 255),
            (byte)Math.Clamp((int)Math.Round(pixel.G * alpha + 255f * inv), 0, 255),
            (byte)Math.Clamp((int)Math.Round(pixel.B * alpha + 255f * inv), 0, 255));
    }
}
