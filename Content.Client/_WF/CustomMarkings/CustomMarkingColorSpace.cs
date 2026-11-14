using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace Content.Client._WF.CustomMarkings;

/// <summary>
/// The colour space a PNG says its pixels are in, read from its cICP, iCCP, sRGB and gAMA chunks, and how to bring
/// them to sRGB. ImageSharp hands back the stored values as they are, so art saved in a wide gamut, linear light or
/// HDR looked washed out once read as sRGB.
/// </summary>
/// <remarks>The client sandbox keeps ImageSharp's own metadata out of reach, so the chunks are read here.</remarks>
public sealed class CustomMarkingColorSpace
{
    /// <summary>How stored values become linear light, each channel 0 to 1, before the matrix.</summary>
    private readonly Func<float, float>[] _toLinear;

    /// <summary>From the source's linear RGB to linear sRGB.</summary>
    private readonly Matrix4x4 _toSrgb;

    private CustomMarkingColorSpace(Func<float, float>[] toLinear, Matrix4x4 toSrgb)
    {
        _toLinear = toLinear;
        _toSrgb = toSrgb;
    }

    /// <summary>Linear XYZ (D50, as ICC profiles use) to linear sRGB, Bradford adapted.</summary>
    private static readonly Matrix4x4 D50ToSrgb = Rows(
        3.1338561f, -1.6168667f, -0.4906146f,
        -0.9787684f, 1.9161415f, 0.0334540f,
        0.0719453f, -0.2289914f, 1.4052427f);

    private static readonly Matrix4x4 Bt2020ToSrgb = Rows(
        1.6604910f, -0.5876411f, -0.0728499f,
        -0.1245505f, 1.1328999f, -0.0083494f,
        -0.0181508f, -0.1005789f, 1.1187297f);

    private static readonly Matrix4x4 DisplayP3ToSrgb = Rows(
        1.2249401f, -0.2249404f, 0f,
        -0.0420569f, 1.0420571f, 0f,
        -0.0196376f, -0.0786361f, 1.0982735f);

    /// <summary>PQ's 10,000 nits over the 203 nits HDR reference white, which lands on sRGB white.</summary>
    private const float PqToReferenceWhite = 10000f / 203f;

    /// <summary>Where HLG puts reference white in scene light: its signal at 75 %.</summary>
    private static readonly float HlgReferenceWhite = HlgToLinear(0.75f);

    private const int MaxProfileBytes = 1024 * 1024;

    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    /// <summary>
    /// What a PNG's pixels need to become sRGB, or null when they already are, say so through an sRGB chunk, say
    /// nothing, or are in a space this can't convert from.
    /// </summary>
    public static CustomMarkingColorSpace? Read(ReadOnlySpan<byte> png)
    {
        if (png.Length < Signature.Length || !png[..Signature.Length].SequenceEqual(Signature))
            return null;

        byte[]? cicp = null, iccp = null;
        float? gamma = null;
        var srgb = false;
        var at = Signature.Length;
        while (at + 8 <= png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png[at..]);
            if (length < 0 || at + 12L + length > png.Length)
                break;

            var type = Encoding.ASCII.GetString(png.Slice(at + 4, 4));
            var data = png.Slice(at + 8, length);
            at += 12 + length;
            switch (type)
            {
                case "cICP" when length == 4:
                    cicp = data.ToArray();
                    break;
                case "iCCP":
                    iccp = data.ToArray();
                    break;
                case "sRGB":
                    srgb = true;
                    break;
                case "gAMA" when length == 4:
                    gamma = BinaryPrimitives.ReadUInt32BigEndian(data) / 100000f;
                    break;
                case "IDAT":
                case "IEND":
                    at = png.Length;
                    break;
            }
        }

        // The PNG specification's order: cICP over iCCP over sRGB over gAMA.
        if (cicp != null)
            return FromCicp(cicp);
        if (iccp != null)
            return FromIccChunk(iccp);
        if (srgb || gamma is not { } encoding || encoding <= 0f || MathF.Abs(encoding - 1f / 2.2f) < 0.01f)
            return null;

        // A gamma alone: its exponent, over the sRGB primaries.
        var exponent = 1f / encoding;
        return Uniform(v => MathF.Pow(v, exponent), Matrix4x4.Identity);
    }

    /// <summary>Converts one pixel, each channel 0 to 1, to sRGB values 0 to 1. Alpha is left alone.</summary>
    public Vector3 ToSrgb(Vector3 stored)
    {
        var linear = new Vector3(_toLinear[0](stored.X), _toLinear[1](stored.Y), _toLinear[2](stored.Z));
        var srgb = Vector3.Transform(linear, Matrix4x4.Transpose(_toSrgb));
        return new Vector3(Encode(srgb.X), Encode(srgb.Y), Encode(srgb.Z));

        static float Encode(float linear)
        {
            linear = Math.Clamp(linear, 0f, 1f);
            return linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
        }
    }

    private static CustomMarkingColorSpace? FromCicp(byte[] cicp)
    {
        Matrix4x4 primaries;
        switch (cicp[0])
        {
            case 1:
                primaries = Matrix4x4.Identity;
                break;
            case 9:
                primaries = Bt2020ToSrgb;
                break;
            case 12:
                primaries = DisplayP3ToSrgb;
                break;
            default:
                return null;
        }

        Func<float, float> transfer;
        switch (cicp[1])
        {
            case 13:
                if (cicp[0] == 1)
                    return null;
                transfer = SrgbToLinear;
                break;
            case 1:
            case 6:
            case 14:
            case 15:
                transfer = Bt709ToLinear;
                break;
            case 8:
                transfer = v => v;
                break;
            case 16:
                transfer = v => PqToLinear(v) * PqToReferenceWhite;
                break;
            case 18:
                transfer = v => HlgToLinear(v) / HlgReferenceWhite;
                break;
            default:
                return null;
        }

        return Uniform(transfer, primaries);
    }

    /// <summary>An iCCP chunk: a name, a nul, a compression method of 0 and a zlib-compressed ICC profile.</summary>
    private static CustomMarkingColorSpace? FromIccChunk(byte[] chunk)
    {
        var nul = Array.IndexOf(chunk, (byte) 0);
        // Past the nul and the method byte, then the two-byte zlib header that DeflateStream doesn't read.
        if (nul < 0 || nul + 4 > chunk.Length || chunk[nul + 1] != 0)
            return null;

        try
        {
            using var deflate = new DeflateStream(new MemoryStream(chunk, nul + 4, chunk.Length - nul - 4), CompressionMode.Decompress);
            using var profile = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
            {
                profile.Write(buffer, 0, read);
                if (profile.Length > MaxProfileBytes)
                    return null;
            }

            return FromIcc(profile.ToArray());
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// A matrix-shaper RGB profile: three primaries as XYZ columns and a tone curve for each channel. Profiles built
    /// on lookup tables instead, which painting programs rarely embed in a PNG, are left as they are.
    /// </summary>
    public static CustomMarkingColorSpace? FromIcc(byte[] profile)
    {
        if (profile.Length < 132 || Encoding.ASCII.GetString(profile, 16, 4) != "RGB " || Encoding.ASCII.GetString(profile, 20, 4) != "XYZ ")
            return null;

        var count = BinaryPrimitives.ReadInt32BigEndian(profile.AsSpan(128));
        if (count < 0 || 132L + count * 12L > profile.Length)
            return null;

        var tags = new Dictionary<string, (int Offset, int Size)>();
        for (var i = 0; i < count; i++)
        {
            var entry = profile.AsSpan(132 + i * 12);
            var offset = BinaryPrimitives.ReadInt32BigEndian(entry[4..]);
            var size = BinaryPrimitives.ReadInt32BigEndian(entry[8..]);
            if (offset >= 0 && size >= 0 && (long) offset + size <= profile.Length)
                tags[Encoding.ASCII.GetString(profile, 132 + i * 12, 4)] = (offset, size);
        }

        if (Xyz(profile, tags, "rXYZ") is not { } red || Xyz(profile, tags, "gXYZ") is not { } green
            || Xyz(profile, tags, "bXYZ") is not { } blue
            || Curve(profile, tags, "rTRC") is not { } redCurve || Curve(profile, tags, "gTRC") is not { } greenCurve
            || Curve(profile, tags, "bTRC") is not { } blueCurve)
            return null;

        var toXyz = Rows(
            red.X, green.X, blue.X,
            red.Y, green.Y, blue.Y,
            red.Z, green.Z, blue.Z);
        // Matrices here are written row by row and applied to column vectors, so the product reads right to left.
        return new CustomMarkingColorSpace(new[] { redCurve, greenCurve, blueCurve }, D50ToSrgb * toXyz);
    }

    private static Vector3? Xyz(byte[] profile, Dictionary<string, (int Offset, int Size)> tags, string tag)
    {
        if (!tags.TryGetValue(tag, out var at) || at.Size < 20 || Encoding.ASCII.GetString(profile, at.Offset, 4) != "XYZ ")
            return null;

        var data = profile.AsSpan(at.Offset + 8);
        return new Vector3(Fixed(data), Fixed(data[4..]), Fixed(data[8..]));
    }

    private static Func<float, float>? Curve(byte[] profile, Dictionary<string, (int Offset, int Size)> tags, string tag)
    {
        if (!tags.TryGetValue(tag, out var at) || at.Size < 12)
            return null;

        var type = Encoding.ASCII.GetString(profile, at.Offset, 4);
        var data = profile.AsSpan(at.Offset + 8, at.Size - 8);
        if (type == "curv")
        {
            var entries = BinaryPrimitives.ReadInt32BigEndian(data);
            if (entries == 0)
                return v => v;
            if (entries == 1 && data.Length >= 6)
            {
                var exponent = BinaryPrimitives.ReadUInt16BigEndian(data[4..]) / 256f;
                return v => MathF.Pow(v, exponent);
            }

            if (entries < 2 || data.Length < 4 + entries * 2L)
                return null;

            var table = new float[entries];
            for (var i = 0; i < entries; i++)
            {
                table[i] = BinaryPrimitives.ReadUInt16BigEndian(data[(4 + i * 2)..]) / 65535f;
            }

            return v =>
            {
                var position = Math.Clamp(v, 0f, 1f) * (table.Length - 1);
                var low = (int) position;
                var high = Math.Min(low + 1, table.Length - 1);
                return table[low] + (table[high] - table[low]) * (position - low);
            };
        }

        if (type != "para" || data.Length < 8)
            return null;

        var function = BinaryPrimitives.ReadUInt16BigEndian(data);
        var needed = function switch { 0 => 1, 1 => 3, 2 => 4, 3 => 5, 4 => 7, _ => -1 };
        if (needed < 0 || data.Length < 4 + needed * 4)
            return null;

        var p = new float[7];
        for (var i = 0; i < needed; i++)
        {
            p[i] = Fixed(data[(4 + i * 4)..]);
        }

        var (g, a, b, c, d, e, f) = (p[0], p[1], p[2], p[3], p[4], p[5], p[6]);
        return function switch
        {
            0 => v => MathF.Pow(v, g),
            1 => v => v >= -b / a ? MathF.Pow(a * v + b, g) : 0f,
            2 => v => v >= -b / a ? MathF.Pow(a * v + b, g) + c : c,
            3 => v => v >= d ? MathF.Pow(a * v + b, g) : c * v,
            _ => v => v >= d ? MathF.Pow(a * v + b, g) + e : c * v + f,
        };
    }

    /// <summary>An ICC s15Fixed16Number.</summary>
    private static float Fixed(ReadOnlySpan<byte> data)
    {
        return BinaryPrimitives.ReadInt32BigEndian(data) / 65536f;
    }

    private static CustomMarkingColorSpace Uniform(Func<float, float> transfer, Matrix4x4 toSrgb)
    {
        return new CustomMarkingColorSpace(new[] { transfer, transfer, transfer }, toSrgb);
    }

    private static float SrgbToLinear(float v)
    {
        return v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
    }

    private static float Bt709ToLinear(float v)
    {
        return v < 0.081f ? v / 4.5f : MathF.Pow((v + 0.099f) / 1.099f, 1f / 0.45f);
    }

    /// <summary>SMPTE ST 2084: a stored value to light, 1 being 10,000 nits.</summary>
    private static float PqToLinear(float v)
    {
        const float m1 = 2610f / 16384f, m2 = 2523f / 4096f * 128f;
        const float c1 = 3424f / 4096f, c2 = 2413f / 4096f * 32f, c3 = 2392f / 4096f * 32f;
        var power = MathF.Pow(Math.Max(v, 0f), 1f / m2);
        return MathF.Pow(Math.Max(power - c1, 0f) / (c2 - c3 * power), 1f / m1);
    }

    /// <summary>ARIB STD-B67's inverse: a stored value to scene light, 0 to 1.</summary>
    private static float HlgToLinear(float v)
    {
        const float a = 0.17883277f, b = 0.28466892f, c = 0.55991073f;
        return v <= 0.5f ? v * v / 3f : (MathF.Exp((v - c) / a) + b) / 12f;
    }

    /// <summary>A 3x3 matrix, given row by row, in the corner of a 4x4.</summary>
    private static Matrix4x4 Rows(float m11, float m12, float m13, float m21, float m22, float m23, float m31, float m32, float m33)
    {
        return new Matrix4x4(m11, m12, m13, 0f, m21, m22, m23, 0f, m31, m32, m33, 0f, 0f, 0f, 0f, 1f);
    }
}
