using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client.IconSmoothing.Smoothings;

/// <summary>
/// Calculates a sprite state from Cardinal Direction flags.
/// Shrimple as that.
/// </summary>
public sealed partial class DiagonalSpriteSmoothing : CornerSpriteSmoothing
{
    /// <summary>
    /// Neighbor smooth keys that join this diagonal even when the south/southeast pocket is not fully walled in.
    /// Reinforced diagonals list plastitanium here, and plastitanium diagonals list reinforced.
    /// </summary>
    [DataField]
    public HashSet<string>? EdgeKeys { get; set; }

    public override void InitializeStates(Entity<SpriteComponent> entity, SpriteSystem sprite)
    {
        sprite.LayerMapSet(entity.AsNullable(), LayerKey, sprite.AddRsiLayer(entity.AsNullable(), Base + 0, index: Index));
    }

    public override IEnumerable<SpriteLayer> EnumerateStates(HashSet<string>?[] layers, Entity<SpriteComponent> entity, SpriteSystem sprite)
    {
        if (!GetCorners(2, out var mask))
        {
            DebugTools.Assert($"Hardcoded check in {nameof(DiagonalSpriteSmoothing)} failed to return a mask!");
            yield break;
        }

        var match = Direction8Flag.None;
        var edge = false;
        for (byte i = 0; i <= (byte)Direction.East; i++)
        {
            if (layers[i] is not { } keys || !keys.Overlaps(Mask))
                continue;

            match |= (Direction8Flag)(1 << i);
            edge |= EdgeKeys != null && keys.Overlaps(EdgeKeys);
        }

        if (EdgeKeys != null && !edge)
        {
            for (byte i = (byte)Direction.NorthEast; i < layers.Length; i++)
            {
                if (layers[i] is { } keys && keys.Overlaps(EdgeKeys))
                {
                    edge = true;
                    break;
                }
            }
        }

        yield return new SpriteLayer(LayerKey, GetState((match & mask) == mask || edge));
    }

    private string GetState(bool match)
    {
        return match ? Base + 1 : Base + 0;
    }
}
