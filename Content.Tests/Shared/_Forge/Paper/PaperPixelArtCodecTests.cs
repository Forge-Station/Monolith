using System;
using System.Text;
using Content.Shared._Forge.Paper;
using NUnit.Framework;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Tests.Shared._Forge.Paper;

[TestFixture]
[TestOf(typeof(PaperPixelArtCodec))]
public sealed class PaperPixelArtCodecTests
{
    [Test]
    public void CompressesColorBlockDrawingsBelowPaperLimit()
    {
        var original = BuildColorGrid(20, 20, (x, y) => Color.FromHex($"#{x * 10:X2}{y * 10:X2}80"));
        Assert.That(original.Length, Is.GreaterThan(6000));

        var compressed = PaperPixelArtCodec.Compress(original);

        Assert.That(compressed, Does.StartWith("[px "));
        Assert.That(compressed.Length, Is.LessThan(6000));
        Assert.That(compressed.Length, Is.LessThan(original.Length / 2));
        Assert.That(PaperPixelArtCodec.Compress(compressed), Is.EqualTo(compressed));
    }

    [Test]
    public void EncodeDecodeRoundtripKeepsPixels()
    {
        var pixels = new[]
        {
            Color.Red, Color.Green, Color.Blue, Color.Red,
            Color.Green, Color.Blue, Color.Red, Color.Green
        };

        var tag = PaperPixelArtCodec.Encode(4, pixels);
        Assert.That(FormattedMessage.TryParse($"prefix{tag}suffix", out var nodes, out var error), Is.True, error);
        var px = nodes!.Find(n => n.Name == PaperPixelArtCodec.TagName);
        Assert.That(px, Is.Not.Null);
        Assert.That(PaperPixelArtCodec.TryDecode(px!, out var art), Is.True);
        Assert.That(art.Width, Is.EqualTo(4));
        Assert.That(art.Height, Is.EqualTo(2));
        Assert.That(art.Pixels, Is.EqualTo(pixels));
    }

    [Test]
    public void SplitDocumentKeepsTextAndDrawingsSeparate()
    {
        var pixels = new[] { Color.Red, Color.Green, Color.Blue, Color.Red };
        var tag = PaperPixelArtCodec.Encode(2, pixels);
        var markup = $"Memo\n{tag}\n- Officer";

        var parts = PaperPixelArtCodec.SplitDocument(markup);
        Assert.That(parts.Count, Is.EqualTo(3));
        Assert.That(parts[0].Text, Is.EqualTo("Memo\n"));
        Assert.That(parts[0].Art, Is.Null);
        Assert.That(parts[1].Art, Is.Not.Null);
        Assert.That(parts[1].Art!.Value.Width, Is.EqualTo(2));
        Assert.That(parts[2].Text, Is.EqualTo("\n- Officer"));
        Assert.That(parts[2].Art, Is.Null);
    }

    [Test]
    public void SplitDocumentLeavesPlainWritingAlone()
    {
        const string text = "Captain,\nPlease sign [italic]here[/italic].\n[color=red]Urgent[/color]";
        var parts = PaperPixelArtCodec.SplitDocument(text);
        Assert.That(parts.Count, Is.EqualTo(1));
        Assert.That(parts[0].Text, Is.EqualTo(text));
        Assert.That(parts[0].Art, Is.Null);
    }

    [Test]
    public void LeavesNormalWritingAlone()
    {
        const string text = "Captain,\nPlease sign [italic]here[/italic].\n[color=red]Urgent[/color]";
        Assert.That(PaperPixelArtCodec.Compress(text), Is.EqualTo(text));
    }

    [Test]
    public void CompressesMixedDocumentAndKeepsSurroundingText()
    {
        var art = BuildColorGrid(4, 4, (x, y) => (x + y) % 2 == 0 ? Color.Black : Color.White);
        var original = $"Memo\n{art}\n- Officer";
        var compressed = PaperPixelArtCodec.Compress(original);

        Assert.That(compressed, Does.StartWith("Memo\n[px "));
        Assert.That(compressed, Does.EndWith("\n- Officer"));
        Assert.That(compressed.Length, Is.LessThan(original.Length));
    }

    [Test]
    public void ExpandsRunLengthColorTagsIntoPixels()
    {
        const string original = "[color=#ff0000]██[/color][color=#00ff00]██[/color]\n[color=#0000ff]████[/color]";
        var compressed = PaperPixelArtCodec.Compress(original);
        Assert.That(FormattedMessage.TryParse(compressed, out var nodes, out var error), Is.True, error);
        var px = nodes!.Find(n => n.Name == PaperPixelArtCodec.TagName);
        Assert.That(PaperPixelArtCodec.TryDecode(px!, out var art), Is.True);
        Assert.That(art.Width, Is.EqualTo(4));
        Assert.That(art.Height, Is.EqualTo(2));
        Assert.That(art.Pixels[0], Is.EqualTo(Color.FromHex("#ff0000")));
        Assert.That(art.Pixels[2], Is.EqualTo(Color.FromHex("#00ff00")));
        Assert.That(art.Pixels[4], Is.EqualTo(Color.FromHex("#0000ff")));
    }

    private static string BuildColorGrid(int width, int height, Func<int, int, Color> pixel)
    {
        var builder = new StringBuilder(width * height * 24);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var hex = pixel(x, y).ToHexNoAlpha();
                builder.Append("[color=");
                builder.Append(hex);
                builder.Append("]█[/color]");
            }

            if (y < height - 1)
                builder.Append('\n');
        }

        return builder.ToString();
    }
}
