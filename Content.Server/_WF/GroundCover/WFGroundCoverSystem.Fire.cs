using System.Numerics;
using Content.Server._Mono.Temperature.Components;
using Content.Server.Atmos.Components;
using Content.Shared._WF.GroundCover;
using Content.Shared.Atmos.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.GroundCover;

public sealed partial class WFGroundCoverSystem
{
    /// <summary>Radius of the warmth a burning thing gives off.</summary>
    public const float BurnRadius = 1.5f;

    /// <summary>Thermal energy a burning thing gives a body in reach each second, toward body heat and no further.</summary>
    public const float BurnThermalEnergy = 12000f;

    /// <summary>Kelvin a burning thing warms a body to at most, a little over body heat.</summary>
    public const float BurnLimit = 311f;

    /// <summary>Snow levels a minute melted on a tile with an atmos hotspot, and half that on each tile next to it.</summary>
    public const float HotspotMeltLevelsPerMinute = 12f;

    private static readonly TimeSpan FireInterval = TimeSpan.FromSeconds(2);

    private readonly List<EntityUid> _burning = new();
    private TimeSpan _nextFire;

    /// <summary>
    /// Gives what is on fire a temporary radiant heater, so the heat pass melts the snow round it and nearby bodies
    /// warm, and takes it off when the fire is out. Atmos hotspots melt the snow on and beside their tiles.
    /// </summary>
    private void UpdateFire(TimeSpan now)
    {
        if (now < _nextFire)
            return;

        _nextFire = now + FireInterval;
        _burning.Clear();
        var flames = EntityQueryEnumerator<FlammableComponent, TransformComponent>();
        while (flames.MoveNext(out var uid, out var flammable, out var xform))
        {
            if (flammable.OnFire && xform.GridUid != null)
                _burning.Add(uid);
        }

        foreach (var uid in _burning)
        {
            if (HasComp<EntityRadiusHeaterComponent>(uid))
                continue;

            AddComp(uid, new EntityRadiusHeaterComponent
            {
                Radius = BurnRadius,
                ThermalEnergy = BurnThermalEnergy,
                Limit = BurnLimit,
                RequireActivation = true,
            });
            EnsureComp<WFBurnWarmthComponent>(uid);
        }

        var spent = EntityQueryEnumerator<WFBurnWarmthComponent, FlammableComponent>();
        while (spent.MoveNext(out var uid, out _, out var flammable))
        {
            if (!flammable.OnFire)
                _burning.Add(uid);
        }

        foreach (var uid in _burning)
        {
            if (!TryComp<FlammableComponent>(uid, out var flammable) || flammable.OnFire || !HasComp<WFBurnWarmthComponent>(uid))
                continue;

            RemComp<EntityRadiusHeaterComponent>(uid);
            RemComp<WFBurnWarmthComponent>(uid);
        }

        MeltHotspots(FireInterval);
    }

    /// <summary>Melts the snow on every atmos hotspot's tile, and on the eight round it at half the rate.</summary>
    private void MeltHotspots(TimeSpan interval)
    {
        var perPass = HotspotMeltLevelsPerMinute * (float) interval.TotalMinutes * WFSnowCover.UnitsPerLevel;
        var query = EntityQueryEnumerator<GridAtmosphereComponent, WFSnowCoverComponent, MapGridComponent>();
        while (query.MoveNext(out var grid, out var atmosphere, out _, out _))
        {
            if (atmosphere.HotspotTilesCount == 0)
                continue;

            foreach (var tile in atmosphere.HotspotTiles)
            {
                if (tile.Hotspot.Valid)
                    MeltAround(grid, tile.GridIndices, perPass);
            }
        }
    }

    /// <summary>Melts some depth units of snow off a tile, and half as much off each of the eight round it. Public for tests.</summary>
    public void MeltAround(EntityUid grid, Vector2i centre, float units)
    {
        for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++)
        {
            var at = centre + new Vector2i(x, y);
            var depth = GetDepth(grid, at);
            if (depth == 0)
                continue;

            SetDepth(grid, at, depth - (int) MathF.Ceiling(x == 0 && y == 0 ? units : units / 2f));
        }
    }
}
