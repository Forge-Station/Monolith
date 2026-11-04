using System.Numerics;
using Content.Shared.Light.Components;
using Content.Shared.Maps;
using Content.Shared.Tag;
using Content.Shared.Weather;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._WF.WolfWeather;

/// <summary>Sets map weather and decides which tiles it reaches.</summary>
public abstract partial class SharedWolfWeatherSystem : EntitySystem
{
    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] protected IPrototypeManager Proto = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private SharedWeatherSystem _weather = default!;
    [Dependency] private EntityQuery<BlockWeatherComponent> _blockQuery = default!;
    [Dependency] private EntityQuery<IsRoofComponent> _isRoofQuery = default!;

    public static readonly ProtoId<TagPrototype> DiagonalTag = "Diagonal";

    /// <summary>Most a weather command or schedule may set a weather for: a week.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(7);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfWeatherMapComponent, EntityUnpausedEvent>(OnUnpaused);
    }

    private void OnUnpaused(Entity<WolfWeatherMapComponent> ent, ref EntityUnpausedEvent args)
    {
        var weathers = ent.Comp.Weathers;
        for (var i = 0; i < weathers.Count; i++)
        {
            var entry = weathers[i];
            entry.Start += args.PausedTime;
            entry.End += args.PausedTime;
            weathers[i] = entry;
        }

        Dirty(ent);
    }

    /// <summary>Starts a weather, or clears with null. Weathers already there fade out from their current strength.</summary>
    public void SetWeather(EntityUid map, ProtoId<WolfWeatherPrototype>? weather, TimeSpan? duration, int seed)
    {
        // Only a surface has weather; a level below or above one stays clear.
        if (!IsSurface(map))
        {
            if (TryComp<WolfWeatherMapComponent>(map, out var stale) && (stale.Weathers.Count > 0 || stale.Wind != Vector2.Zero))
            {
                stale.Weathers.Clear();
                stale.Wind = Vector2.Zero;
                Dirty(map, stale);
            }

            return;
        }

        var comp = EnsureComp<WolfWeatherMapComponent>(map);
        var now = Timing.CurTime;

        for (var i = comp.Weathers.Count - 1; i >= 0; i--)
        {
            var entry = comp.Weathers[i];
            var strength = Proto.TryIndex(entry.Weather, out var proto) ? GetStrength(entry, proto, now) : 0f;
            if (proto == null || strength <= 0f)
            {
                comp.Weathers.RemoveAt(i);
                continue;
            }

            var end = now + proto.FadeOut * strength;
            if (entry.End is { } oldEnd && oldEnd <= end)
                continue;

            entry.End = end;
            comp.Weathers[i] = entry;
        }

        if (weather != null)
        {
            comp.Weathers.Add(new WolfWeatherEntry
            {
                Weather = weather.Value,
                Start = now,
                End = now + duration,
                Seed = seed,
            });
        }

        comp.Wind = weather != null && Proto.TryIndex(weather, out var newProto) ? newProto.Wind : Vector2.Zero;
        Dirty(map, comp);
    }

    public void SetWind(Entity<WolfWeatherMapComponent?> map, Vector2 wind)
    {
        if (!Resolve(map, ref map.Comp, false))
            return;

        map.Comp.Wind = wind;
        Dirty(map);
    }

    /// <summary>The newest weather on the map, if any.</summary>
    public static WolfWeatherEntry? GetCurrent(WolfWeatherMapComponent comp)
    {
        return comp.Weathers.Count > 0 ? comp.Weathers[^1] : null;
    }

    protected void ClearExpired(Entity<WolfWeatherMapComponent> map, TimeSpan now)
    {
        var removed = map.Comp.Weathers.RemoveAll(entry => entry.End is { } end && now >= end);
        if (removed == 0)
            return;

        if (map.Comp.Weathers.Count == 0)
            map.Comp.Wind = Vector2.Zero;

        Dirty(map);
    }

    public float GetStrength(WolfWeatherEntry entry, TimeSpan now)
    {
        return Proto.TryIndex(entry.Weather, out var proto) ? GetStrength(entry, proto, now) : 0f;
    }

    /// <summary>How far a weather has faded in, and not yet out, from 0 to 1.</summary>
    public static float GetStrength(WolfWeatherEntry entry, WolfWeatherPrototype proto, TimeSpan now)
    {
        var fadeIn = proto.FadeIn > TimeSpan.Zero ? (float) ((now - entry.Start) / proto.FadeIn) : 1f;
        var fadeOut = entry.End is not { } end
            ? 1f
            : proto.FadeOut > TimeSpan.Zero
                ? (float) ((end - now) / proto.FadeOut)
                : now < end ? 1f : 0f;

        return Math.Clamp(MathF.Min(fadeIn, fadeOut), 0f, 1f);
    }

    /// <summary>How weather meets a tile, from one pass over its anchored entities.</summary>
    public WolfWeatherTile Classify(Entity<MapGridComponent, RoofComponent?> grid, TileRef tile)
    {
        if (tile.Tile.IsEmpty)
            return WolfWeatherTile.Exposed | WolfWeatherTile.ExposedPastDiagonals;

        // On a planet roofs decide rather than the tile, and a vessel is under cover throughout, as for stock weather.
        var planet = _weather.WfPlanetExposure(grid.Owner);
        var covered = planet == false
                      || planet == null && !((ContentTileDefinition) _tileDefs[tile.Tile.TypeId]).Weather
                      || grid.Comp2 != null && InRoofMask(grid.Comp2, tile.GridIndices);
        var coveredPastDiagonals = covered;
        var result = WolfWeatherTile.None;

        var anchored = _map.GetAnchoredEntities(grid.Owner, grid.Comp1, tile.GridIndices);
        while (anchored.MoveNext(out var ent))
        {
            var blocker = _blockQuery.HasComponent(ent.Value);
            var roof = grid.Comp2 != null && _isRoofQuery.TryComp(ent.Value, out var isRoof) && isRoof.Enabled;
            if (!blocker && !roof)
                continue;

            if (blocker)
                result |= WolfWeatherTile.Blocker;

            covered = true;
            if (!_tag.HasTag(ent.Value, DiagonalTag))
                coveredPastDiagonals = true;
        }

        if (!covered)
            result |= WolfWeatherTile.Exposed;

        if (!coveredPastDiagonals)
            result |= WolfWeatherTile.ExposedPastDiagonals;

        return result;
    }

    /// <summary>Whether weather reaches a tile. With ignoreDiagonals, diagonal wall pieces don't count as cover.</summary>
    public bool IsExposed(Entity<MapGridComponent, RoofComponent?> grid, TileRef tile, bool ignoreDiagonals = false)
    {
        var flag = ignoreDiagonals ? WolfWeatherTile.ExposedPastDiagonals : WolfWeatherTile.Exposed;
        return (Classify(grid, tile) & flag) != 0;
    }

    private static bool InRoofMask(RoofComponent roof, Vector2i index)
    {
        var chunk = SharedMapSystem.GetChunkIndices(index, RoofComponent.ChunkSize);
        if (!roof.Data.TryGetValue(chunk, out var mask))
            return false;

        var relative = SharedMapSystem.GetChunkRelative(index, RoofComponent.ChunkSize);
        var bit = (ulong) 1 << (relative.X + relative.Y * RoofComponent.ChunkSize);
        return (mask & bit) == bit;
    }
}

/// <summary>How weather meets a tile.</summary>
[Flags]
public enum WolfWeatherTile : byte
{
    None = 0,

    /// <summary>Weather reaches the tile.</summary>
    Exposed = 1,

    /// <summary>Weather would reach the tile if diagonal wall pieces didn't cover it.</summary>
    ExposedPastDiagonals = 2,

    /// <summary>A wall, window, door or other weather blocker is anchored on the tile.</summary>
    Blocker = 4,
}
