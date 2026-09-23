using System.Numerics;

namespace Content.Client._WF.Tether;

/// <summary>
/// Client-only verlet chain maths for the drawn rope. There is no gravity in a top-down station,
/// so slack comes from the pinned ends' own motion plus a small per-point drift; the maximum
/// segment length is the only constraint, which lets a slack rope curl and a taut one snap
/// straight on its own.
/// </summary>
public static class RopeVerlet
{
    public const int MinPoints = 8;
    public const int MaxPoints = 96;

    /// <summary>A pinned end jumping further than this in one frame is a grid jump or PVS re-entry.</summary>
    public const float TeleportThreshold = 10f;

    /// <summary>Longest single integration step. Bigger frame times are split into substeps.</summary>
    public const float MaxStep = 1f / 30f;

    public const int Iterations = 10;
    public const float Damping = 0.98f;

    public static int PointCount(float length, float segmentsPerMetre)
    {
        if (!float.IsFinite(length) || !float.IsFinite(segmentsPerMetre))
            return MinPoints;

        var count = (int) MathF.Round(length * MathF.Max(segmentsPerMetre, 0.1f)) + 1;
        return Math.Clamp(count, MinPoints, MaxPoints);
    }

    /// <summary>Lays the chain along the straight line with a sine wiggle and zero velocity.</summary>
    public static void Seed(Vector2[] points, Vector2[] previous, int count, Vector2 endA, Vector2 endB, float wiggle)
    {
        count = Math.Clamp(count, 2, Math.Min(points.Length, previous.Length));
        var delta = endB - endA;
        var length = delta.Length();
        var normal = length > 0.0001f ? new Vector2(-delta.Y, delta.X) / length : Vector2.UnitY;
        for (var i = 0; i < count; i++)
        {
            var fraction = i / (count - 1f);
            var bow = MathF.Sin(fraction * MathF.PI) * wiggle;
            var point = endA + delta * fraction + normal * bow;
            points[i] = Sanitize(point, endA);
            previous[i] = points[i];
        }
    }

    /// <summary>
    /// Integrates one capped substep, pins both ends and relaxes the maximum-length constraint.
    /// <paramref name="drift"/> is the per-point wander in m/s that keeps slack rope from lying flat.
    /// </summary>
    public static void Step(
        Vector2[] points,
        Vector2[] previous,
        int count,
        Vector2 endA,
        Vector2 endB,
        float segmentRest,
        float frameTime,
        float drift,
        float time,
        int seed)
    {
        count = Math.Clamp(count, 2, Math.Min(points.Length, previous.Length));
        if (!float.IsFinite(frameTime) || frameTime <= 0f || !float.IsFinite(segmentRest) || segmentRest <= 0f)
            return;

        var step = MathF.Min(frameTime, MaxStep);
        for (var i = 1; i < count - 1; i++)
        {
            var current = points[i];
            var velocity = (current - previous[i]) * Damping;
            var wander = new Vector2(Noise(seed, i, time, 0f), Noise(seed, i, time, 1.7f)) * drift * step;
            previous[i] = current;
            points[i] = Sanitize(current + velocity + wander, endA);
        }

        points[0] = endA;
        points[count - 1] = endB;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            for (var i = 0; i < count - 1; i++)
            {
                var delta = points[i + 1] - points[i];
                var length = delta.Length();
                // Only the upper bound is enforced: a rope resists stretching, never compression.
                if (!float.IsFinite(length) || length <= segmentRest || length <= 0.00001f)
                    continue;

                var correction = delta * ((length - segmentRest) / length * 0.5f);
                points[i] += correction;
                points[i + 1] -= correction;
            }

            points[0] = endA;
            points[count - 1] = endB;
        }

        for (var i = 0; i < count; i++)
        {
            points[i] = Sanitize(points[i], endA);
            previous[i] = Sanitize(previous[i], endA);
        }
    }

    /// <summary>True when either pinned end moved far enough that the chain must be laid out afresh.</summary>
    public static bool NeedsReseed(Vector2 previousEndA, Vector2 previousEndB, Vector2 endA, Vector2 endB)
    {
        return !float.IsFinite(endA.X) || !float.IsFinite(endA.Y) ||
               !float.IsFinite(endB.X) || !float.IsFinite(endB.Y) ||
               Vector2.DistanceSquared(previousEndA, endA) > TeleportThreshold * TeleportThreshold ||
               Vector2.DistanceSquared(previousEndB, endB) > TeleportThreshold * TeleportThreshold;
    }

    /// <summary>Stable per-point wander, so a rope's curls do not change when the chain is rebuilt.</summary>
    public static float Noise(int seed, int index, float time, float phase)
    {
        var hash = (seed * 73856093) ^ (index * 19349663);
        var offset = (hash & 0xFFFF) / 65536f * MathF.Tau;
        return MathF.Sin(time * (0.7f + index % 5 * 0.13f) + offset + phase);
    }

    private static Vector2 Sanitize(Vector2 value, Vector2 fallback)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) ? value : fallback;
    }
}
