using System.Numerics;

namespace Content.Server._WF.BoardingAction;

/// <summary>Geometry for the hack's gun lockout.</summary>
public static class WFBoardingMath
{
    /// <summary>Whether a ray from <paramref name="origin"/> enters <paramref name="box"/> within <paramref name="reach"/>.</summary>
    public static bool RayHitsBox(Vector2 origin, Vector2 direction, float reach, Box2 box)
    {
        var enter = 0f;
        var exit = reach;
        return Axis(origin.X, direction.X, box.Left, box.Right, ref enter, ref exit) &&
               Axis(origin.Y, direction.Y, box.Bottom, box.Top, ref enter, ref exit);
    }

    /// <summary>Narrows the ray's span to this axis' slab, and reports whether any of it is left.</summary>
    private static bool Axis(float origin, float direction, float min, float max, ref float enter, ref float exit)
    {
        if (MathF.Abs(direction) < 0.00001f)
            return origin >= min && origin <= max;

        var near = (min - origin) / direction;
        var far = (max - origin) / direction;
        if (near > far)
            (near, far) = (far, near);

        enter = MathF.Max(enter, near);
        exit = MathF.Min(exit, far);
        return enter <= exit;
    }
}
