using System.Numerics;

namespace Content.Shared._Forge.KIAS;

[RegisterComponent]
public sealed partial class KiasDefenceComponent : Component
{
    [DataField]
    public bool PdcEnabled;
    [DataField] public bool FireLock;
}

[RegisterComponent]
public sealed partial class KiasPdcRadarComponent : Component
{
    [DataField]
    public float Range = 500;
}

[RegisterComponent]
public sealed partial class KiasPdcWeaponComponent : Component
{
    public TimeSpan ManualUntil;
    public TimeSpan AutomaticUntil;
    public EntityUid? AutomaticGrid;
    public EntityUid? Target;
    public Robust.Shared.Map.EntityCoordinates? PreviousAim;
}

public static class KiasThreatMath
{
    public static bool SweptHit(Vector2 previousOffset, Vector2 currentOffset, float radius)
    {
        var step = currentOffset - previousOffset;
        var length = step.LengthSquared();
        var fraction = length < 0.0001f ? 0 : Math.Clamp(-Vector2.Dot(previousOffset, step) / length, 0, 1);
        return (previousOffset + fraction * step).LengthSquared() <= radius * radius;
    }
    public static bool Approaches(Vector2 relativePosition, Vector2 relativeVelocity, float radius, out float time)
    {
        time = 0;
        var speed = relativeVelocity.LengthSquared();
        if (speed < 0.01f)
            return false;
        time = -Vector2.Dot(relativePosition, relativeVelocity) / speed;
        return time is > 0 and <= 10 && (relativePosition + relativeVelocity * time).LengthSquared() <= radius * radius;
    }

    public static bool Intercept(Vector2 offset, Vector2 velocity, float speed, out float time)
    {
        time = 0;
        var a = velocity.LengthSquared() - speed * speed;
        var b = 2 * Vector2.Dot(offset, velocity);
        var c = offset.LengthSquared();
        if (MathF.Abs(a) < 0.001f)
        {
            if (MathF.Abs(b) < 0.001f)
                return false;
            time = -c / b;
        }
        else
        {
            var discriminant = b * b - 4 * a * c;
            if (discriminant < 0)
                return false;
            var root = MathF.Sqrt(discriminant);
            var first = (-b - root) / (2 * a);
            var second = (-b + root) / (2 * a);
            time = first > 0 && second > 0 ? MathF.Min(first, second) : MathF.Max(first, second);
        }
        return float.IsFinite(time) && time is > 0 and <= 3;
    }
}
