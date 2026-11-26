using Content.Shared.Maps;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.ContentPack;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.Caverns;

/// <summary>
/// The floor under a hole's lip: the pit art has no ground of its own, so each piece gets a layer of the floor round
/// the hole, cut to the piece's lip mask, under it.
/// </summary>
// The floor is the most common tile among the shade's eight neighbours, picked on the client so retiling round a hole
// (a road, a jelly patch, a site floor) shows at once.
public sealed partial class WFCavernShadeVisualsSystem
{
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;

    /// <summary>Every piece's lip mask, in the order of <see cref="AllStates"/> without <c>pit</c>; made by gen_pits.py.</summary>
    public static readonly ResPath LipsPath = new("/Textures/_WF/Caverns/Mouths/pit_lips.png");

    /// <summary>Cells per row of the lip sheet.</summary>
    private const int LipColumns = 16;

    /// <summary>The pixel size of a piece and of a lip cell.</summary>
    private const int PieceSize = 32;

    private Image<Rgba32>? _lips;
    private bool _lipsFailed;
    private Dictionary<string, int>? _lipIndex;

    /// <summary>Each tile sprite's image, or null when it can't be read.</summary>
    private readonly Dictionary<ResPath, Image<Rgba32>?> _floorImages = new();

    /// <summary>Floor pieces already made, by tile type, variant and piece state.</summary>
    private readonly Dictionary<(int Type, byte Variant, string State), OwnedTexture> _floorPieces = new();

    /// <summary>The floor each shade lays under its lip, if it found one.</summary>
    private readonly Dictionary<EntityUid, Tile> _floors = new();

    // Scratch for PickFloor: the kinds of floor round a tile and how often each shows.
    private readonly Tile[] _seenFloors = new Tile[8];
    private readonly int[] _floorCounts = new int[8];

    private void InitializeFloors()
    {
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
    }

    private void OnTileChanged(ref TileChangedEvent args)
    {
        if (!_pits.ContainsKey(args.Entity))
            return;

        foreach (var change in args.Changes)
        {
            RefreshAround(args.Entity, change.GridIndices);
        }
    }

    /// <summary>The floor a shade lays under its lip, for tests and tools.</summary>
    public bool TryGetFloor(EntityUid shade, out Tile floor)
    {
        return _floors.TryGetValue(shade, out floor);
    }

    /// <summary>The most common floor among a tile's eight neighbours, its first-seen variant with it.</summary>
    private Tile? PickFloor(EntityUid grid, MapGridComponent gridComp, Vector2i tile)
    {
        var seen = _seenFloors;
        var counts = _floorCounts;
        var kinds = 0;

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                var neighbour = _map.GetTileRef(grid, gridComp, tile + new Vector2i(dx, dy)).Tile;
                if (neighbour.IsEmpty)
                    continue;

                var found = false;
                for (var i = 0; i < kinds; i++)
                {
                    if (seen[i].TypeId != neighbour.TypeId)
                        continue;
                    counts[i]++;
                    found = true;
                    break;
                }

                if (found)
                    continue;

                seen[kinds] = neighbour;
                counts[kinds++] = 1;
            }
        }

        if (kinds == 0)
            return null;

        var best = 0;
        for (var i = 1; i < kinds; i++)
        {
            if (counts[i] > counts[best])
                best = i;
        }

        return seen[best];
    }

    /// <summary>A piece's floor: the tile's sprite under the piece's lip mask, or null when either can't be read.</summary>
    private Texture? FloorPiece(Tile floor, string state)
    {
        var key = (floor.TypeId, floor.Variant, state);
        if (_floorPieces.TryGetValue(key, out var made))
            return made;

        if (Lips() is not { } lips || _lipIndex is not { } index || !index.TryGetValue(state, out var cell))
            return null;

        if (_tileDefs[floor.TypeId] is not ContentTileDefinition { Sprite: { } sprite } def || FloorImage(sprite) is not { } source)
            return null;

        // A sprite holds its variants side by side; a smaller or larger tile is sampled to fit.
        var variants = Math.Max((int) def.Variants, 1);
        var size = source.Width / variants;
        if (size <= 0 || source.Height < size)
            return null;

        var left = Math.Min((int) floor.Variant, variants - 1) * size;
        var lipX = cell % LipColumns * PieceSize;
        var lipY = cell / LipColumns * PieceSize;
        var half = PieceSize / 2;

        // The sandbox allows reading pixels only through spans.
        var lipPixels = lips.GetPixelSpan();
        var sourcePixels = source.GetPixelSpan();
        using var image = new Image<Rgba32>(PieceSize, PieceSize);
        for (var y = 0; y < PieceSize; y++)
        {
            for (var x = 0; x < PieceSize; x++)
            {
                var mask = lipPixels[(lipY + y) * lips.Width + lipX + x].A;
                if (mask == 0)
                    continue;

                // A piece is centred on a tile corner, so its pixels lie half a tile off the floor's own.
                var sx = (x + half) % PieceSize * size / PieceSize;
                var sy = (y + half) % PieceSize * size / PieceSize;
                var pixel = sourcePixels[sy * source.Width + left + sx];
                pixel.A = (byte) (pixel.A * mask / 255);
                image[x, y] = pixel;
            }
        }

        made = _clyde.LoadTextureFromImage(image, $"wf-pit-floor-{floor.TypeId}-{floor.Variant}-{state}");
        _floorPieces[key] = made;
        return made;
    }

    private Image<Rgba32>? Lips()
    {
        if (_lips != null || _lipsFailed)
            return _lips;

        _lips = ReadImage(LipsPath);
        _lipsFailed = _lips == null;
        if (_lips == null)
            return null;

        _lipIndex = new Dictionary<string, int>();
        var cell = 0;
        foreach (var state in AllStates())
        {
            if (state != "pit")
                _lipIndex[state] = cell++;
        }

        if (_lips.Width < LipColumns * PieceSize || _lips.Height < (cell + LipColumns - 1) / LipColumns * PieceSize)
        {
            Log.Error($"{LipsPath} is too small for {cell} lip masks; rerun Tools/_WF/Caverns/gen_pits.py.");
            _lips.Dispose();
            _lips = null;
            _lipsFailed = true;
        }

        return _lips;
    }

    private Image<Rgba32>? FloorImage(ResPath path)
    {
        if (_floorImages.TryGetValue(path, out var image))
            return image;

        image = ReadImage(path);
        _floorImages[path] = image;
        return image;
    }

    private Image<Rgba32>? ReadImage(ResPath path)
    {
        try
        {
            using var stream = _resources.ContentFileRead(path);
            return Image.Load<Rgba32>(stream);
        }
        catch (Exception e)
        {
            Log.Error($"Could not read {path} for a cavern pit's floor: {e.Message}");
            return null;
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _lips?.Dispose();
        foreach (var image in _floorImages.Values)
        {
            image?.Dispose();
        }

        foreach (var texture in _floorPieces.Values)
        {
            texture.Dispose();
        }

        _floorImages.Clear();
        _floorPieces.Clear();
    }
}
