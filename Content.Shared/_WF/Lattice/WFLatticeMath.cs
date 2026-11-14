using System.Numerics;
using System.Text;

namespace Content.Shared._WF.Lattice;

/// <summary>The Lattice's field: anchors' summed hold, bands, depth and garbling.</summary>
public static class WFLatticeMath
{
    /// <summary>Directions sampled round the centre when working out the band thresholds.</summary>
    private const int ThresholdSamples = 72;

    private const string Noise = "#$%@!&*?~^";

    /// <summary>One anchor's hold at a distance: its strength, halved at <paramref name="reach"/>.</summary>
    public static float Hold(float distance, float reach, float strength)
    {
        var ratio = distance / reach;
        return strength / (1f + ratio * ratio);
    }

    /// <summary>
    /// The local hold of a secondary anchor (a relay or a station's own), which makes anchored space round it without
    /// bending the bands far off: a sharp well at a <see cref="LocalReachShare"/> of the reach, falling with the fourth power.
    /// </summary>
    public static float LocalHold(float distance, float reach, float strength)
    {
        var ratio = distance / (reach * LocalReachShare);
        return strength * LocalPeak / (1f + ratio * ratio * ratio * ratio);
    }

    /// <summary>A secondary anchor's well, as a share of the reach, and its hold at its centre per unit of strength.</summary>
    public const float LocalReachShare = 0.375f;
    public const float LocalPeak = 1f;

    /// <summary>
    /// The summed hold at a point. Primary anchors (positive strength) hold out to the band edges; secondary anchors
    /// (negative strength) only make a well of anchored space round themselves.
    /// </summary>
    public static float Field(Vector2 point, IReadOnlyList<(Vector2 Position, float Strength)> anchors, float reach)
    {
        var field = 0f;
        foreach (var (position, strength) in anchors)
        {
            var distance = Vector2.Distance(point, position);
            field += strength >= 0f ? Hold(distance, reach, strength) : LocalHold(distance, reach, -strength);
        }

        return field;
    }

    /// <summary>The corners of a regular polygon about <paramref name="centre"/>, the first at <paramref name="degrees"/>.</summary>
    public static List<Vector2> Corners(Vector2 centre, int count, float radius, float degrees)
    {
        var corners = new List<Vector2>(count);
        for (var i = 0; i < count; i++)
        {
            var angle = degrees * MathF.PI / 180f + MathF.Tau * i / count;
            corners.Add(centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        }

        return corners;
    }

    /// <summary>
    /// The field strength at each band edge after the first, averaged round the centre, for the given anchors. A point
    /// whose field falls below a threshold is past that edge, so a lost anchor pulls the edges in on its side.
    /// </summary>
    public static List<float> Thresholds(IReadOnlyList<float> edges, IReadOnlyList<(Vector2 Position, float Strength)> anchors, float reach, Vector2 centre)
    {
        var thresholds = new List<float>();
        for (var i = 1; i < edges.Count; i++)
        {
            var sum = 0f;
            for (var s = 0; s < ThresholdSamples; s++)
            {
                var angle = MathF.Tau * s / ThresholdSamples;
                sum += Field(centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * edges[i], anchors, reach);
            }

            thresholds.Add(sum / ThresholdSamples);
        }

        return thresholds;
    }

    /// <summary>The band a field strength falls in: how many thresholds it is below.</summary>
    public static int Band(float field, IReadOnlyList<float> thresholds)
    {
        var band = 0;
        while (band < thresholds.Count && field < thresholds[band])
        {
            band++;
        }

        return band;
    }

    /// <summary>How far past the band's inner edge a field is, 0 at the edge towards 1 far out.</summary>
    public static float Depth(float field, int band, IReadOnlyList<float> thresholds)
    {
        if (band <= 0 || band > thresholds.Count)
            return 0f;

        var edge = thresholds[band - 1];
        return edge <= 0f ? 1f : Math.Clamp(1f - field / edge, 0f, 1f);
    }

    /// <summary>Drift gained per second in a band at a depth.</summary>
    public static float DriftRate(WFLatticeBand band, float depth)
    {
        return band.Drift * MathF.Exp(band.DriftGrowth * depth);
    }

    /// <summary>Share of letters a garbling band swaps at a depth: some at its edge, all far out.</summary>
    public static float GarbleStrength(float depth)
    {
        return 0.2f + 0.8f * Math.Clamp(depth, 0f, 1f);
    }

    /// <summary>Swaps a share of the letters for noise, the same way for the same seed.</summary>
    public static string Garble(string text, float strength, int seed)
    {
        if (strength <= 0f)
            return text;

        var builder = new StringBuilder(text.Length);
        var state = (uint) seed * 2654435761u + 1u;
        foreach (var c in text)
        {
            state = state * 1664525u + 1013904223u;
            if (char.IsWhiteSpace(c) || (state >> 8) / 16777216f >= strength)
            {
                builder.Append(c);
                continue;
            }

            builder.Append(Noise[(int) ((state >> 4) % (uint) Noise.Length)]);
        }

        return builder.ToString();
    }
}
