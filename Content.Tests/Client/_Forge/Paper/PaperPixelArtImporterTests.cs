using System.IO;
using Content.Client._Forge.Paper;
using Content.Shared._Forge.Paper;
using NUnit.Framework;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Tests.Client._Forge.Paper;

[TestFixture]
[TestOf(typeof(PaperPixelArtImporter))]
public sealed class PaperPixelArtImporterTests
{
    [Test]
    public void ImportsPngIntoPxTag()
    {
        using var image = new Image<Rgba32>(4, 2);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                image[x, y] = x < 2
                    ? new Rgba32(255, 0, 0)
                    : new Rgba32(0, 255, 0);
            }
        }

        using var stream = SavePng(image);
        Assert.That(PaperPixelArtImporter.TryImport(stream, 6000, out var markup, out var error), Is.True);
        Assert.That(error, Is.EqualTo(PaperPixelArtImportError.None));
        Assert.That(markup, Does.StartWith("[px "));

        Assert.That(FormattedMessage.TryParse(markup!, out var nodes, out var parseError), Is.True, parseError);
        var px = nodes!.Find(n => n.Name == PaperPixelArtCodec.TagName);
        Assert.That(PaperPixelArtCodec.TryDecode(px!, out var art), Is.True);
        Assert.That(art.Width, Is.EqualTo(4));
        Assert.That(art.Height, Is.EqualTo(2));
        Assert.That(art.Pixels[0], Is.EqualTo(Robust.Shared.Maths.Color.FromHex("#ff0000")));
        Assert.That(art.Pixels[2], Is.EqualTo(Robust.Shared.Maths.Color.FromHex("#00ff00")));
    }

    [Test]
    public void FitsFullSizePhotoWithPalette()
    {
        using var image = new Image<Rgba32>(64, 64);
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                image[x, y] = new Rgba32((byte)(x * 4), (byte)(y * 4), (byte)((x + y) * 2));
            }
        }

        Assert.That(PaperPixelArtImporter.TryEncode(image, 6000, out var markup), Is.True);
        Assert.That(markup!.Length, Is.LessThanOrEqualTo(6000));
        Assert.That(FormattedMessage.TryParse(markup, out var nodes, out var parseError), Is.True, parseError);
        var px = nodes!.Find(n => n.Name == PaperPixelArtCodec.TagName);
        Assert.That(PaperPixelArtCodec.TryDecode(px!, out var art), Is.True);
        Assert.That(art.Width, Is.EqualTo(64));
        Assert.That(art.Height, Is.EqualTo(64));
    }

    [Test]
    public void KeepsInkWhenDownsamplingLineArt()
    {
        using var image = new Image<Rgba32>(128, 32, new Rgba32(255, 255, 255));
        for (var y = 14; y < 18; y++)
        {
            for (var x = 0; x < 128; x++)
                image[x, y] = new Rgba32(20, 20, 20);
        }

        Assert.That(PaperPixelArtImporter.TryEncode(image, 6000, out var markup), Is.True);
        Assert.That(FormattedMessage.TryParse(markup!, out var nodes, out var parseError), Is.True, parseError);
        var px = nodes!.Find(n => n.Name == PaperPixelArtCodec.TagName);
        Assert.That(PaperPixelArtCodec.TryDecode(px!, out var art), Is.True);

        var dark = 0;
        foreach (var pixel in art.Pixels)
        {
            if (pixel.RByte < 200)
                dark++;
        }

        Assert.That(dark, Is.GreaterThan(art.Width), "Thin strokes must survive downsampling.");
    }

    [Test]
    public void TreatsTransparentPixelsAsWhite()
    {
        using var image = new Image<Rgba32>(2, 2);
        image[0, 0] = new Rgba32(0, 0, 0, 0);
        image[1, 0] = new Rgba32(0, 0, 255);
        image[0, 1] = new Rgba32(0, 0, 255);
        image[1, 1] = new Rgba32(0, 0, 0, 0);

        Assert.That(PaperPixelArtImporter.TryEncode(image, 6000, out var markup), Is.True);
        Assert.That(FormattedMessage.TryParse(markup!, out var nodes, out var parseError), Is.True, parseError);
        var px = nodes!.Find(n => n.Name == PaperPixelArtCodec.TagName);
        Assert.That(PaperPixelArtCodec.TryDecode(px!, out var art), Is.True);
        Assert.That(art.Pixels[0], Is.EqualTo(Robust.Shared.Maths.Color.White));
        Assert.That(art.Pixels[1], Is.EqualTo(Robust.Shared.Maths.Color.FromHex("#0000ff")));
    }

    [Test]
    public void FailsWhenThereIsNoSpace()
    {
        using var image = new Image<Rgba32>(8, 8, new Rgba32(255, 0, 0));
        Assert.That(PaperPixelArtImporter.TryEncode(image, 10, out _), Is.False);
    }

    [Test]
    public void ImportsWebpIntoPxTag()
    {
        using var image = new Image<Rgba32>(2, 2, new Rgba32(0, 0, 255));
        using var stream = new MemoryStream();
        image.SaveAsWebp(stream);
        stream.Position = 0;

        Assert.That(PaperPixelArtImporter.TryImport(stream, 6000, out var markup, out var error), Is.True);
        Assert.That(error, Is.EqualTo(PaperPixelArtImportError.None));
        Assert.That(markup, Does.StartWith("[px "));
    }

    private static MemoryStream SavePng(Image<Rgba32> image)
    {
        var stream = new MemoryStream();
        image.SaveAsPng(stream);
        stream.Position = 0;
        return stream;
    }
}
