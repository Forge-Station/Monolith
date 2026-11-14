namespace Content.Shared._WF.MappingMode;

/// <summary>Why a PNG was refused before decoding, or <see cref="Ok"/>.</summary>
public enum WFMappingPngCheck : byte
{
    Ok,
    NotPng,
    FileTooLarge,
    ImageTooLarge,
}

/// <summary>Reads a PNG's size from its IHDR chunk, so oversized images are refused before they are decoded.</summary>
public static class WFMappingPngHeader
{
    /// <summary>Largest file a template may be.</summary>
    public const int MaxFileBytes = 16 * 1024 * 1024;

    /// <summary>Longest side in pixels.</summary>
    public const int MaxSide = 8192;

    /// <summary>Most pixels in all, 4096 squared.</summary>
    public const long MaxPixels = 4096L * 4096L;

    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>Width and height from the signature and the IHDR chunk that must follow it.</summary>
    public static bool TryReadSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 24)
            return false;

        for (var i = 0; i < Signature.Length; i++)
        {
            if (data[i] != Signature[i])
                return false;
        }

        if (ReadUInt32(data, 8) != 13 || data[12] != 'I' || data[13] != 'H' || data[14] != 'D' || data[15] != 'R')
            return false;

        var w = ReadUInt32(data, 16);
        var h = ReadUInt32(data, 20);
        if (w == 0 || h == 0 || w > int.MaxValue || h > int.MaxValue)
            return false;

        width = (int) w;
        height = (int) h;
        return true;
    }

    /// <summary>Checks the file length, the header and the image size against the caps.</summary>
    public static WFMappingPngCheck Check(ReadOnlySpan<byte> data, out int width, out int height)
    {
        return Check(data, MaxFileBytes, out width, out height);
    }

    /// <summary>As <see cref="Check(ReadOnlySpan{byte}, out int, out int)"/>, with its own file size cap.</summary>
    public static WFMappingPngCheck Check(ReadOnlySpan<byte> data, long maxBytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length > maxBytes)
            return WFMappingPngCheck.FileTooLarge;

        if (!TryReadSize(data, out width, out height))
            return WFMappingPngCheck.NotPng;

        if (width > MaxSide || height > MaxSide || (long) width * height > MaxPixels)
            return WFMappingPngCheck.ImageTooLarge;

        return WFMappingPngCheck.Ok;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int at)
    {
        return (uint) data[at] << 24 | (uint) data[at + 1] << 16 | (uint) data[at + 2] << 8 | data[at + 3];
    }
}
