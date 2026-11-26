using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using Content.Shared._WF.CustomMarkings;
using Robust.Client.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.CustomMarkings;

/// <summary>Reads marking art from PNG: sheets the server sends, and files a player imports.</summary>
public static class CustomMarkingPng
{
    /// <summary>Largest file the importer will try to read: room for a sheet of every frame, however noisy.</summary>
    public const int MaxFileBytes = 1024 * 1024;

    /// <summary>Art from a PNG of a whole sheet or of one facing; null for anything else.</summary>
    public static CustomMarkingArt? Read(byte[] png)
    {
        if (png.Length > MaxFileBytes || !HasArtSize(png))
            return null;

        try
        {
            if (CustomMarkingColorSpace.Read(png) is { } space)
                return ReadConverted(png, space);

            using var image = Image.Load<Rgba32>(new MemoryStream(png, false));
            return CustomMarkingArt.FromImage(image.Width, image.Height, image.GetPixelSpan());
        }
        catch (Exception)
        {
            // Not an image ImageSharp can read.
            return null;
        }
    }

    /// <summary>
    /// Whether the PNG's header gives the size of one facing or of a sheet of frames, read before decoding so an
    /// image of any other size never gets its pixels allocated.
    /// </summary>
    private static bool HasArtSize(byte[] png)
    {
        // The signature, then IHDR's length and type, then its width and height.
        if (png.Length < 24 || png[12] != 'I' || png[13] != 'H' || png[14] != 'D' || png[15] != 'R')
            return false;

        var width = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16));
        var height = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20));
        if (width == CustomMarkingRules.FrameSize && height == CustomMarkingRules.FrameSize)
            return true;

        return width == CustomMarkingRules.SheetSize && height > 0 && height % CustomMarkingRules.SheetSize == 0
               && height / CustomMarkingRules.SheetSize <= CustomMarkingRules.MaxFrames;
    }

    /// <summary>Reads a PNG saved in another colour space at full depth and brings its pixels to sRGB.</summary>
    private static CustomMarkingArt? ReadConverted(byte[] png, CustomMarkingColorSpace space)
    {
        using var image = Image.Load<Rgba64>(new MemoryStream(png, false));
        var stored = image.GetPixelSpan();
        var pixels = new Rgba32[stored.Length];
        for (var i = 0; i < stored.Length; i++)
        {
            var pixel = stored[i];
            var srgb = space.ToSrgb(new Vector3(pixel.R, pixel.G, pixel.B) / ushort.MaxValue);
            pixels[i] = new Rgba32(Channel(srgb.X), Channel(srgb.Y), Channel(srgb.Z), (byte) (pixel.A >> 8));
        }

        return CustomMarkingArt.FromImage(image.Width, image.Height, pixels);

        static byte Channel(float value)
        {
            return (byte) Math.Clamp(MathF.Round(value * byte.MaxValue), byte.MinValue, byte.MaxValue);
        }
    }
}
