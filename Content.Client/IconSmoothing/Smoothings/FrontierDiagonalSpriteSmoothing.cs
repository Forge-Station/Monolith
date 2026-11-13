using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client.IconSmoothing.Smoothings;

/// <summary>
/// Frontier five-state diagonal walls and windows.
/// States are none, east, south, both, and both plus the southeast corner.
/// </summary>
public sealed partial class FrontierDiagonalSpriteSmoothing : CornerSpriteSmoothing
{
    public override void InitializeStates(Entity<SpriteComponent> entity, SpriteSystem sprite)
    {
        sprite.LayerMapSet(entity.AsNullable(), LayerKey, sprite.AddRsiLayer(entity.AsNullable(), Base + 0, index: Index));
    }

    public override IEnumerable<SpriteLayer> EnumerateStates(HashSet<string>?[] layers, Entity<SpriteComponent> entity, SpriteSystem sprite)
    {
        var east = Overlaps(layers, Direction.East);
        var south = Overlaps(layers, Direction.South);
        var corner = Overlaps(layers, Direction.SouthEast);

        var value = 0;
        if (east)
            value |= 1;
        if (south)
            value |= 2;
        if (value == 3 && corner)
            value = 4;

        yield return new SpriteLayer(LayerKey, Base + value);
    }

    private bool Overlaps(HashSet<string>?[] layers, Direction direction)
    {
        return layers[(byte)direction] is { } keys && keys.Overlaps(Mask);
    }
}
