using Content.Server.Electrocution;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Ghost;
using Content.Shared.Light.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Station.Components;
using Robust.Shared.Containers;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.WolfWeather;

/// <summary>Rolls lightning on maps whose weather has it: far flashes, and strikes near players that shock.</summary>
public sealed partial class WolfLightningSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private ElectrocutionSystem _electrocution = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WolfWeatherSystem _wolfWeather = default!;

    /// <summary>Weather strength below which no bolts fall, so they start and stop with the storm's peak.</summary>
    private const float MinStrength = 0.5f;

    private const float StrikeRadius = 0.7f;
    private const int StrikeAttempts = 8;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    private readonly Dictionary<EntityUid, TimeSpan> _nextBolt = new();
    private readonly List<EntityUid> _targets = new();
    private readonly HashSet<Entity<MobStateComponent>> _struck = new();
    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfWeatherMapComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<WolfWeatherMapComponent> ent, ref ComponentShutdown args)
    {
        _nextBolt.Remove(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextCheck)
            return;

        _nextCheck = now + CheckInterval;

        var query = EntityQueryEnumerator<WolfWeatherMapComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (Stormiest(comp, now) is not { } storm || storm.Lightning is not { } lightning)
            {
                _nextBolt.Remove(uid);
                continue;
            }

            if (!_nextBolt.TryGetValue(uid, out var next))
            {
                _nextBolt[uid] = now + TimeSpan.FromSeconds(_random.Roll(lightning.Interval));
                continue;
            }

            if (now < next)
                continue;

            _nextBolt[uid] = now + TimeSpan.FromSeconds(_random.Roll(lightning.Interval));
            Bolt(uid, storm, lightning);
        }
    }

    /// <summary>The strongest weather on the map that has lightning and is strong enough to bolt.</summary>
    private WolfWeatherPrototype? Stormiest(WolfWeatherMapComponent comp, TimeSpan now)
    {
        WolfWeatherPrototype? best = null;
        var bestStrength = MinStrength;
        foreach (var entry in comp.Weathers)
        {
            if (!_proto.TryIndex(entry.Weather, out var proto) || proto.Lightning == null)
                continue;

            var strength = SharedWolfWeatherSystem.GetStrength(entry, proto, now);
            if (strength < bestStrength)
                continue;

            best = proto;
            bestStrength = strength;
        }

        return best;
    }

    /// <summary>Strikes near a player if the roll and open ground allow, otherwise flashes far off. Anyone under the map's sky hears it, vehicle interiors included.</summary>
    private void Bolt(EntityUid map, WolfWeatherPrototype weather, WolfWeatherLightning lightning)
    {
        var filter = Filter.Empty().AddWhereAttachedEntity(body => _wolfWeather.GetSkyMap(body) == map);
        var strike = _random.Prob(lightning.StrikeChance) && TryStrike(map, lightning, filter);
        RaiseNetworkEvent(new WolfLightningEvent { Map = GetNetEntity(map), Weather = weather.ID, Strike = strike }, filter);
    }

    private bool TryStrike(EntityUid map, WolfWeatherLightning lightning, Filter filter)
    {
        _targets.Clear();
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is { } attached && Transform(attached).MapUid == map && IsValidTarget(attached))
                _targets.Add(attached);
        }

        if (_targets.Count == 0
            || FindStrikeTile(_random.Pick(_targets), lightning) is not { } coords)
            return false;

        Strike(coords, lightning, filter);
        return true;
    }

    /// <summary>
    /// Whether a strike may land near this player: a living body, not a ghost or an admin ghost, and not sealed in a
    /// container such as a locker or a vehicle.
    /// </summary>
    public bool IsValidTarget(EntityUid uid)
    {
        return HasComp<MobStateComponent>(uid)
               && !HasComp<GhostComponent>(uid)
               && !_container.IsEntityOrParentInContainer(uid);
    }

    /// <summary>
    /// A tile for a strike near the target: open sky, on the target's own grid, never a station's grid or a vessel's
    /// (a vessel on a planet is under cover throughout). Null when a few tries find none.
    /// </summary>
    public EntityCoordinates? FindStrikeTile(EntityUid target, WolfWeatherLightning lightning)
    {
        var xform = Transform(target);
        if (xform.GridUid is not { } gridUid
            || !TryComp<MapGridComponent>(gridUid, out var grid)
            || HasComp<StationMemberComponent>(gridUid))
            return null;

        TryComp<RoofComponent>(gridUid, out var roof);
        var origin = _transform.GetWorldPosition(xform);
        var range = lightning.StrikeRange;

        for (var attempt = 0; attempt < StrikeAttempts; attempt++)
        {
            var offset = _random.NextVector2(range.X, MathF.Max(range.X, range.Y));
            var tile = _map.GetTileRef(gridUid, grid, _map.WorldToTile(gridUid, grid, origin + offset));

            if (tile.Tile.IsEmpty || !_wolfWeather.IsExposed((gridUid, grid, roof), tile))
                continue;

            return _map.GridTileToLocal(gridUid, grid, tile.GridIndices);
        }

        return null;
    }

    /// <summary>Drops a bolt on a tile and shocks the living things standing out in the open on it.</summary>
    public void Strike(EntityCoordinates coords, WolfWeatherLightning lightning, Filter filter)
    {
        if (lightning.Bolt is { } bolt)
            Spawn(bolt, coords);

        if (lightning.NearSound is { } sound)
            _audio.PlayStatic(sound, filter, coords, true);

        _struck.Clear();
        _lookup.GetEntitiesInRange(coords, StrikeRadius, _struck);
        foreach (var mob in _struck)
        {
            if (!IsShockable(mob))
                continue;

            _electrocution.TryDoElectrocution(mob, null, lightning.StrikeDamage,
                TimeSpan.FromSeconds(lightning.StrikeStun), true, ignoreInsulation: true);
        }
    }

    /// <summary>A mob caught by a strike: not a ghost, not in a container, and standing on a tile open to the sky.</summary>
    public bool IsShockable(EntityUid mob)
    {
        if (HasComp<GhostComponent>(mob) || _container.IsEntityOrParentInContainer(mob))
            return false;

        var xform = Transform(mob);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        TryComp<RoofComponent>(gridUid, out var roof);
        return _wolfWeather.IsExposed((gridUid, grid, roof), _map.GetTileRef(gridUid, grid, xform.Coordinates));
    }
}
