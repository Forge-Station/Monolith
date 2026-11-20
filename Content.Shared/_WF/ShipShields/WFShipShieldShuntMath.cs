using System.Numerics;

namespace Content.Shared._WF.ShipShields;

/// <summary>Shares angular shield allocation and perimeter clipping between simulation and rendering.</summary>
public static class WFShipShieldShuntMath
{
    /// <summary>The narrowest supported allocation sector.</summary>
    public const float MinimumArc = MathF.PI / 6f;
    /// <summary>A full circle of shield coverage.</summary>
    public const float FullArc = MathF.PI * 2f;
    /// <summary>How fast the allocation's bearing and width turn, in radians per second.</summary>
    public const float TurnSpeed = MathF.PI / 6f;
    /// <summary>How fast power moves into or out of the arc, in concentration per second.</summary>
    public const float TransferSpeed = 0.2f;

    /// <summary>Approaches a requested value with a bounded transfer rate.</summary>
    public static float Step(float current, float target, float dt, float maxSpeed)
    {
        if (!float.IsFinite(current) || !float.IsFinite(target) || !float.IsFinite(dt) ||
            !float.IsFinite(maxSpeed) || dt <= 0f || maxSpeed <= 0f)
            return current;
        var difference = target - current;
        if (MathF.Abs(difference) <= 0.001f)
            return target;
        var amount = difference * (1f - MathF.Exp(-dt / 0.65f));
        amount = Math.Clamp(amount, -maxSpeed * dt, maxSpeed * dt);
        return MathF.Abs(difference - amount) <= 0.001f ? target : current + amount;
    }

    /// <summary>Turns along the shortest path at no more than <see cref="TurnSpeed"/>.</summary>
    public static float StepAngle(float current, float target, float dt)
    {
        if (!float.IsFinite(current) || !float.IsFinite(target))
            return current;
        var destination = current + NormalizeAngle(target - current);
        return NormalizeAngle(Step(current, destination, dt, TurnSpeed));
    }

    /// <summary>Normalizes an angle to the range minus pi to pi.</summary>
    public static float NormalizeAngle(float angle)
    {
        return MathF.IEEERemainder(angle, FullArc);
    }

    /// <summary>Strength inside the arc. The cube root caps narrow arcs: a full 30-degree shunt gives about 2.3x, 90 degrees about 1.6x.</summary>
    public static float PeakStrength(float concentration, float arcRadians) =>
        1f + concentration * (MathF.Cbrt(FullArc / arcRadians) - 1f);

    /// <summary>Returns local strength: <see cref="PeakStrength"/> inside the arc, what's left of the perimeter outside it.</summary>
    public static float StrengthMultiplier(Vector2 position, Vector2 center, float directionRadians, float concentration, float arcRadians)
    {
        if (arcRadians >= FullArc - 0.00001f || concentration <= 0f)
            return 1f;
        var offset = position - center;
        var angle = MathF.Atan2(offset.Y, offset.X);
        var inside = MathF.Abs(NormalizeAngle(angle - directionRadians)) <= arcRadians * 0.5f + 0.00001f;
        return inside ? PeakStrength(concentration, arcRadians) : 1f - concentration;
    }

    /// <summary>Splits an edge at sector rays and returns only its protected intervals.</summary>
    public static List<(Vector2 Start, Vector2 End)> ProtectedSegments(Vector2 start, Vector2 end, Vector2 center,
        float directionRadians, float concentration, float arcRadians)
    {
        var result = new List<(Vector2 Start, Vector2 End)>();
        if (concentration < 1f || arcRadians >= FullArc - 0.00001f)
        {
            result.Add((start, end));
            return result;
        }
        var cuts = new List<float> { 0f, 1f };
        var edge = end - start;
        for (var side = -1; side <= 1; side += 2)
        {
            var angle = directionRadians + side * arcRadians * 0.5f;
            var ray = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var denominator = Cross(edge, ray);
            if (MathF.Abs(denominator) < 0.000001f)
                continue;
            var offset = center - start;
            var t = Cross(offset, ray) / denominator;
            var alongRay = Cross(offset, edge) / denominator;
            if (t > 0f && t < 1f && alongRay >= 0f)
                cuts.Add(t);
        }
        cuts.Sort();
        for (var i = 0; i < cuts.Count - 1; i++)
        {
            var a = start + edge * cuts[i];
            var b = start + edge * cuts[i + 1];
            if (Vector2.DistanceSquared(a, b) < 0.0001f ||
                StrengthMultiplier((a + b) * 0.5f, center, directionRadians, concentration, arcRadians) <= 0f)
                continue;
            result.Add((a, b));
        }
        return result;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.X * b.Y - a.Y * b.X;
    }
}
