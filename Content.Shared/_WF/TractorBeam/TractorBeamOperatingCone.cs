using System;
using System.Numerics;

namespace Content.Shared._WF.TractorBeam;

/// <summary>The dish's finite forward firing sector, shared by server validation and the control display.</summary>
public static class TractorBeamOperatingCone
{
    public const float DefaultHalfAngle = MathF.PI / 4f;

    /// <summary>Offset is measured from the dish. Forward is the dish's local +Y axis in the same coordinates.</summary>
    public static bool Contains(Vector2 offset, Vector2 forward, float range, float halfAngle)
    {
        return TryMeasure(offset, forward, range, halfAngle, out var distance, out var angle) &&
               distance <= range && angle <= halfAngle + 0.0000001;
    }

    /// <summary>Warn inside the outer 15% of either the range or angular opening, before the lock is lost.</summary>
    public static bool IsNearEdge(Vector2 offset, Vector2 forward, float range, float halfAngle, float margin = 0.15f)
    {
        if (!float.IsFinite(margin) || margin < 0f || margin > 1f ||
            !TryMeasure(offset, forward, range, halfAngle, out var distance, out var angle) ||
            distance > range || angle > halfAngle + 0.0000001)
            return false;

        return distance >= range * (1.0 - margin) || angle >= halfAngle * (1.0 - margin);
    }

    private static bool TryMeasure(Vector2 offset, Vector2 forward, float range, float halfAngle,
        out double distance, out double angle)
    {
        distance = angle = 0;
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y) ||
            !float.IsFinite(forward.X) || !float.IsFinite(forward.Y) || forward == Vector2.Zero ||
            !float.IsFinite(range) || range <= 0f || !float.IsFinite(halfAngle) ||
            halfAngle <= 0f || halfAngle > MathF.PI / 2f)
            return false;

        // Doubles keep finite but unusually large map coordinates from overflowing intermediate products.
        distance = Math.Sqrt((double) offset.X * offset.X + (double) offset.Y * offset.Y);
        if (distance > 0)
            angle = Math.Atan2(Math.Abs((double) offset.X * forward.Y - (double) offset.Y * forward.X),
                (double) offset.X * forward.X + (double) offset.Y * forward.Y);
        return true;
    }
}
