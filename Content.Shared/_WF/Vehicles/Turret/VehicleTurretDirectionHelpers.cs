using System.Numerics;
using System;
using Robust.Shared.Maths;

namespace Content.Shared._WF.Vehicles.Turret;

/// <summary>Turns a facing into the cardinal its sprite is drawn in.</summary>
public static class VehicleTurretDirectionHelpers
{
    private const double SpriteDirectionBiasRadians = -0.05;

    /// <summary>The cardinal direction of a facing as the sprites are drawn.</summary>
    public static Direction GetRenderAlignedCardinalDir(Angle facing)
    {
        var angle = facing.Reduced().FlipPositive();
        var mod = (Math.Floor(angle.Theta / MathHelper.PiOver2) % 2) - 0.5;
        var modTheta = angle.Theta + mod * SpriteDirectionBiasRadians;

        return ((int) Math.Round(modTheta / MathHelper.PiOver2) % 4) switch
        {
            0 => Direction.South,
            1 => Direction.East,
            2 => Direction.North,
            _ => Direction.West
        };
    }

    /// <summary>A pose's pixel offset in the axes of the thing that shows the pose: turned back by the pose's own angle.</summary>
    public static Vector2 GetLocalOffsetForRenderDirection(Vector2 offset, Angle facing)
    {
        return (-GetRenderAlignedCardinalDir(facing).ToAngle()).RotateVec(offset);
    }
}
