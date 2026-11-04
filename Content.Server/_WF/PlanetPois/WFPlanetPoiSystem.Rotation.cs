using System.Numerics;
using Content.Shared._WF.MappingTools;
using Content.Shared._WF.PlanetPois;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Server._WF.PlanetPois;

/// <summary>Quarter turns of an authored site: its tiles, footprint and entrances, the way the grid merge turns them.</summary>
public sealed partial class WFPlanetPoiSystem
{
    private static Angle Turns(int turns) => Angle.FromDegrees(90 * turns);

    /// <summary>Where a tile lands after the merge turns its grid by <paramref name="turns"/> quarter turns about the origin.</summary>
    internal static Vector2i TurnTile(Vector2i tile, int turns)
    {
        if (turns == 0)
            return tile;
        return Turns(turns).RotateVec(new Vector2(tile.X + 0.5f, tile.Y + 0.5f)).Floored();
    }

    internal static Box2i TurnBox(Box2i box, int turns)
    {
        if (turns == 0)
            return box;
        var a = TurnTile(new Vector2i(box.Left, box.Bottom), turns);
        var b = TurnTile(new Vector2i(box.Right - 1, box.Top - 1), turns);
        return new Box2i(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);
    }

    internal static WFPlanetPoiDirection TurnDirection(WFPlanetPoiDirection direction, int turns)
    {
        var vector = Turns(turns).RotateVec((Vector2) Direction(direction));
        var rounded = new Vector2i((int) MathF.Round(vector.X), (int) MathF.Round(vector.Y));
        return rounded.X > 0 ? WFPlanetPoiDirection.East
            : rounded.X < 0 ? WFPlanetPoiDirection.West
            : rounded.Y > 0 ? WFPlanetPoiDirection.North
            : WFPlanetPoiDirection.South;
    }

    /// <summary>
    /// The grid merge moves tiles but not their drawn rotation: turns a rotatable tile's texture with its site, so road
    /// lines and markings point the way they did on the map.
    /// </summary>
    private void TurnTileStates(EntityUid ground, MapGridComponent grid, List<Vector2i> sourceTiles, Vector2i origin, int turns)
    {
        if (turns == 0 || sourceTiles.Count == 0)
            return;

        var turned = new List<(Vector2i, Tile)>();
        foreach (var index in sourceTiles)
        {
            var target = TurnTile(index, turns) + origin;
            var tile = _maps.GetTileRef(ground, grid, target).Tile;
            if (tile.IsEmpty || !((ContentTileDefinition) _tiles[tile.TypeId]).AllowRotationMirror)
                continue;

            // The site turns anticlockwise; the tile state helper counts clockwise turns.
            turned.Add((target, new Tile(tile.TypeId, tile.Flags, tile.Variant, MappingToolsMath.RotateTileState(tile.RotationMirroring, -turns))));
        }

        if (turned.Count > 0)
            _maps.SetTiles(ground, grid, turned);
    }

    /// <summary>
    /// The grid merge turns every entity with the grid. Puts back to no rotation the ones that never rotate (noRot),
    /// which only break when deleted, and the ones whose smoothed sprite is drawn square to the grid.
    /// </summary>
    internal void UnturnFixedRotation(IEnumerable<EntityUid> entities)
    {
        foreach (var uid in entities)
        {
            if (TerminatingOrDeleted(uid) || Transform(uid) is not { } xform || xform.LocalRotation == Angle.Zero)
                continue;
            if (xform.NoLocalRotation)
            {
                // Rotation writes are ignored while the flag is up; raising it again zeroes the rotation.
                _transform.SetNoLocalRotation((uid, xform), false);
                _transform.SetNoLocalRotation((uid, xform), true);
            }
            else if (GridAligned(MetaData(uid).EntityPrototype?.ID))
                _transform.SetLocalRotation(uid, Angle.Zero, xform);
        }
    }

    /// <summary>
    /// True for a prototype whose IconSmooth mode (CardinalFlags, Bitmask) picks its state from grid neighbours
    /// without allowing for its own rotation, so its one-direction sprite only joins up at rotation zero.
    /// </summary>
    internal bool GridAligned(string? prototype) =>
        prototype != null && SmoothingMode(prototype, 0) is "CardinalFlags" or "Bitmask";

    /// <summary>
    /// The IconSmooth mode a prototype sets or inherits, null if none does. Read from the YAML by hand: the server
    /// ignores the component, so it never merges a parent's into the child. An earlier parent wins, as in the merge.
    /// </summary>
    private string? SmoothingMode(string prototype, int depth)
    {
        if (depth > 32 || !_proto.TryGetMapping(typeof(EntityPrototype), prototype, out var mapping))
            return null;

        if (mapping.TryGet<SequenceDataNode>("components", out var components))
        {
            foreach (var component in components)
            {
                if (component is MappingDataNode node
                    && node.TryGet<ValueDataNode>("type", out var type) && type.Value == "IconSmooth"
                    && node.TryGet<ValueDataNode>("mode", out var mode))
                    return mode.Value;
            }
        }

        if (!mapping.TryGet("parent", out var parents))
            return null;
        if (parents is ValueDataNode single)
            return SmoothingMode(single.Value, depth + 1);
        if (parents is SequenceDataNode sequence)
        {
            foreach (var parent in sequence)
            {
                if (parent is ValueDataNode value && SmoothingMode(value.Value, depth + 1) is { } inherited)
                    return inherited;
            }
        }

        return null;
    }

    /// <summary>The entry as placed after a turn: its footprint and entrances turned with the map.</summary>
    private static WFPlanetPoiEntry Turned(WFPlanetPoiEntry entry, int turns)
    {
        var sockets = new List<WFPlanetPoiSocket>();
        foreach (var socket in entry.Sockets)
        {
            sockets.Add(new WFPlanetPoiSocket
            {
                Offset = TurnTile(socket.Offset, turns),
                Direction = TurnDirection(socket.Direction, turns),
            });
        }

        return new WFPlanetPoiEntry
        {
            Dungeon = entry.Dungeon,
            MapPath = entry.MapPath,
            Footprint = entry.Footprint is { } footprint ? TurnBox(footprint, turns) : null,
            MinCount = entry.MinCount,
            MaxCount = entry.MaxCount,
            Connection = entry.Connection,
            Sockets = sockets,
            Name = entry.Name,
            Weight = entry.Weight,
            Expeditions = entry.Expeditions,
            Difficulty = entry.Difficulty,
            Rotate = entry.Rotate,
            Trail = entry.Trail,
            Dwelling = entry.Dwelling,
            Priority = entry.Priority,
            Below = entry.Below,
        };
    }
}
