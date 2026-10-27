using System.Numerics;
using Content.Shared._WF.AsteroidMining;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.AsteroidMining;

/// <summary>What the cockpit scanner reads off one asteroid. A rock out of view range gives only its signal.</summary>
public sealed class WFAsteroidScanReading
{
    public bool Resolved;
    public MapId Map;
    public Vector2 Center;
    public float Radius;
    public string Name = string.Empty;
    /// <summary>Distance from the pilot to the rock's surface, in metres.</summary>
    public float Range;
    public float Mass;
    public readonly List<WFMiningBeamOreEntry> Ores = new();
    public float Inert;
    public float Resistance;
    public float WindowLow;
    public float WindowHigh;
    public string Stability = string.Empty;
    public string Blast = string.Empty;
    public float Charge;
    public float Progress;
    public bool Overcharging;
}

/// <summary>Finds the asteroid under a point and reads it for the cockpit scanner.</summary>
public sealed class WFAsteroidScanReader
{
    /// <summary>Largest rock radius, so a lookup around a point reaches the centre of any rock it touches.</summary>
    private const float MaxRockRadius = 12f;

    private readonly IPrototypeManager _prototypes;
    private readonly EntityLookupSystem _lookup;
    private readonly SharedMapSystem _map;
    private readonly SharedTransformSystem _transform;
    private readonly EntityQuery<WFAsteroidComponent> _rocks;
    private readonly EntityQuery<WFAsteroidGridVisualsComponent> _gridVisuals;
    private readonly EntityQuery<MapGridComponent> _grids;
    private readonly EntityQuery<TransformComponent> _xforms;
    private readonly EntityQuery<MetaDataComponent> _metas;
    private readonly HashSet<Entity<WFAsteroidComponent>> _nearby = new();
    private List<Entity<MapGridComponent>> _gridHits = new();

    public WFAsteroidScanReader(IEntityManager entities, IPrototypeManager prototypes)
    {
        _prototypes = prototypes;
        _lookup = entities.System<EntityLookupSystem>();
        _map = entities.System<SharedMapSystem>();
        _transform = entities.System<SharedTransformSystem>();
        _rocks = entities.GetEntityQuery<WFAsteroidComponent>();
        _gridVisuals = entities.GetEntityQuery<WFAsteroidGridVisualsComponent>();
        _grids = entities.GetEntityQuery<MapGridComponent>();
        _xforms = entities.GetEntityQuery<TransformComponent>();
        _metas = entities.GetEntityQuery<MetaDataComponent>();
    }

    /// <summary>
    /// The asteroid whose surface is nearest the point, within <paramref name="slack"/> metres of it: a rock in view range,
    /// or else the grid of one that is out of it.
    /// </summary>
    public bool TryFind(MapId map, Vector2 point, float slack, out EntityUid key)
    {
        key = default;
        var best = float.MaxValue;
        _nearby.Clear();
        _lookup.GetEntitiesInRange(map, point, slack + MaxRockRadius, _nearby);
        foreach (var (uid, rock) in _nearby)
        {
            var gap = Vector2.Distance(_transform.GetWorldPosition(uid), point) - rock.Radius;
            if (gap > slack || gap >= best)
                continue;
            best = gap;
            key = KeyOf(uid);
        }

        if (best < float.MaxValue)
            return true;

        _gridHits.Clear();
        _map.FindGridsIntersecting(map, Box2.CenteredAround(point, new Vector2(2f * (slack + MaxRockRadius))), ref _gridHits);
        foreach (var (uid, grid) in _gridHits)
        {
            if (!_gridVisuals.HasComp(uid))
                continue;
            var (center, radius) = GridDisc(uid, grid);
            var gap = Vector2.Distance(center, point) - radius;
            if (gap > slack || gap >= best)
                continue;
            best = gap;
            key = uid;
        }

        return best < float.MaxValue;
    }

    /// <summary>Fills in a reading of the asteroid a key names, measured from the pilot; false once it is gone.</summary>
    public bool TryRead(EntityUid key, Vector2 pilot, WFAsteroidScanReading reading)
    {
        if (!_xforms.TryComp(key, out var keyXform))
            return false;

        reading.Map = keyXform.MapID;
        var rockUid = _rocks.HasComp(key) ? key : RockOn(key);
        if (rockUid is { } uid && _rocks.TryComp(uid, out var rock))
        {
            reading.Resolved = true;
            reading.Center = _transform.GetWorldPosition(uid);
            reading.Radius = rock.Radius;
            reading.Name = _metas.TryComp(uid, out var meta) ? meta.EntityName : string.Empty;
            reading.Mass = rock.Mass;
            reading.Ores.Clear();
            var ores = 0f;
            foreach (var ore in rock.Ores)
            {
                reading.Ores.Add(new WFMiningBeamOreEntry(WFAsteroidScan.OreName(_prototypes, ore.Ore), ore.Fraction, ore.Color));
                ores += ore.Fraction;
            }

            reading.Inert = Math.Clamp(1f - ores, 0f, 1f);
            reading.Resistance = rock.Resistance;
            reading.WindowLow = rock.WindowCenter - rock.WindowHalfWidth;
            reading.WindowHigh = rock.WindowCenter + rock.WindowHalfWidth;
            reading.Stability = WFAsteroidScan.StabilityBand(rock.Instability);
            reading.Blast = WFAsteroidScan.BlastBand(WFAsteroidScan.BlastIntensity(rock.Mass, WFAsteroidScan.Volatility(rock.Ores)));
            reading.Charge = rock.Charge;
            reading.Progress = rock.Progress;
            reading.Overcharging = rock.Overcharging;
        }
        else if (_grids.TryComp(key, out var grid) && _gridVisuals.TryComp(key, out var visuals))
        {
            reading.Resolved = false;
            (reading.Center, reading.Radius) = GridDisc(key, grid);
            reading.Name = visuals.Rock is { } proto && _prototypes.TryIndex(proto, out var rockProto) ? rockProto.Name : string.Empty;
            reading.Ores.Clear();
        }
        else
        {
            return false;
        }

        reading.Range = MathF.Max(0f, Vector2.Distance(pilot, reading.Center) - reading.Radius);
        return true;
    }

    /// <summary>A rock on an asteroid grid is keyed by its grid, so it keeps its lock as it comes into view range.</summary>
    private EntityUid KeyOf(EntityUid rock)
    {
        var parent = _xforms.GetComponent(rock).ParentUid;
        return _gridVisuals.HasComp(parent) ? parent : rock;
    }

    /// <summary>The rock on an asteroid grid, if it is in view range.</summary>
    private EntityUid? RockOn(EntityUid grid)
    {
        if (!_gridVisuals.HasComp(grid))
            return null;

        var children = _xforms.GetComponent(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (_rocks.HasComp(child))
                return child;
        }

        return null;
    }

    /// <summary>Where an asteroid grid's rock sits, on the centre of tile (0, 0), and the radius of its tile disc.</summary>
    private (Vector2 Center, float Radius) GridDisc(EntityUid uid, MapGridComponent grid)
    {
        var (position, rotation) = _transform.GetWorldPositionRotation(uid);
        var size = grid.LocalAABB.Size;
        return (position + rotation.RotateVec(new Vector2(0.5f, 0.5f)), MathF.Max(size.X, size.Y) / 2f);
    }
}
