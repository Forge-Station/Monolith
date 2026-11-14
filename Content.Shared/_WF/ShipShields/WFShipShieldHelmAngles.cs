namespace Content.Shared._WF.ShipShields;

/// <summary>Converts clockwise helm bearings to ship-local shield directions.</summary>
public static class WFShipShieldHelmAngles
{
    /// <summary>Zero is forward on the resolved helm, and 180 degrees is aft.</summary>
    public static float GridDirection(float degrees, float helmRotation)
    {
        return helmRotation + MathF.PI / 2f - degrees * MathF.PI / 180f;
    }

    /// <summary>Returns a helm-relative bearing in the range zero to 360 degrees.</summary>
    public static float Bearing(float gridDirection, float helmRotation)
    {
        var degrees = (helmRotation + MathF.PI / 2f - gridDirection) * 180f / MathF.PI;
        return (degrees % 360f + 360f) % 360f;
    }
}
