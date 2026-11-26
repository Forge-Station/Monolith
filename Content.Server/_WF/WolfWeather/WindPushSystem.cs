using System.Numerics;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Buckle.Components;
using Content.Shared.Item;
using Content.Shared.Light.Components;
using Content.Shared.Mobs.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.WolfWeather;

/// <summary>Pushes loose items on exposed tiles with the wind.</summary>
public sealed partial class WindPushSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private WolfWeatherSystem _wolfWeather = default!;
    [Dependency] private EntityQuery<WolfWeatherMapComponent> _weatherQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private EntityQuery<RoofComponent> _roofQuery = default!;
    [Dependency] private EntityQuery<WindPushableComponent> _pushableQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _xformQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobQuery = default!;
    [Dependency] private EntityQuery<BuckleComponent> _buckleQuery = default!;

    /// <summary>How far ahead of an item, in tiles, the ground must go on for the wind to push it.</summary>
    private const float LookAhead = 0.6f;

    private readonly HashSet<Entity<ItemComponent>> _items = new();
    private readonly HashSet<EntityUid> _pushed = new();
    private TimeSpan _nextPush;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextPush)
            return;

        var interval = 0.5f;
        var budget = int.MaxValue;
        _pushed.Clear();

        EntityUid? settingsMap = null;
        WolfWeatherSettingsPrototype? settings = null;

        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapUid is not { } mapUid || !_weatherQuery.TryComp(mapUid, out var weather))
                continue;

            if (settingsMap != mapUid)
            {
                settingsMap = mapUid;
                settings = _proto.TryIndex(weather.Settings, out var found) ? found : null;
            }

            if (settings is not { PushEnabled: true })
                continue;

            interval = settings.PushInterval;
            budget = Math.Min(budget, settings.PushMaxPerTick);
            if (budget <= 0)
                break;

            budget -= PushAround(_transform.GetMapCoordinates(xform), weather.Wind, settings, budget);
        }

        _nextPush = now + TimeSpan.FromSeconds(interval);
    }

    /// <summary>
    /// Pushes loose items around a point with the wind, at most <paramref name="budget"/> of them, each only once per
    /// pass. Returns how many it pushed.
    /// </summary>
    public int PushAround(MapCoordinates center, Vector2 wind, WolfWeatherSettingsPrototype settings, int budget)
    {
        var speed = wind.Length();
        if (speed <= settings.PushThreshold || budget <= 0)
            return 0;

        var direction = wind / speed;
        var over = speed - settings.PushThreshold;
        var chance = Math.Clamp(settings.PushChance * over / settings.PushThreshold, 0f, 1f);
        var pushed = 0;

        _items.Clear();
        _lookup.GetEntitiesInRange(center, settings.PushRadius, _items, LookupFlags.Dynamic | LookupFlags.Sundries);

        foreach (var item in _items)
        {
            if (pushed >= budget)
                break;

            if (!_pushed.Add(item.Owner) || !_random.Prob(chance))
                continue;

            if (!CanPush(item, settings, direction, out var factor) || !_physicsQuery.TryComp(item.Owner, out var body))
                continue;

            var jitter = new Vector2(-direction.Y, direction.X) * _random.NextFloat(-0.15f, 0.15f);
            var push = (direction + jitter) * over * settings.PushStrength * factor * _random.NextFloat(0.5f, 1.5f);
            _physics.ApplyLinearImpulse(item.Owner, push * body.Mass, body: body);
            pushed++;
        }

        return pushed;
    }

    /// <summary>
    /// Whether the wind may push an item: loose, light, not a creature, not buckled or held or stowed, out in the open,
    /// and with its own grid's ground going on downwind so it never blows off one grid onto another or into space.
    /// </summary>
    public bool CanPush(Entity<ItemComponent> item, WolfWeatherSettingsPrototype settings, Vector2 direction, out float factor)
    {
        factor = 1f;
        if (_pushableQuery.TryComp(item.Owner, out var pushable))
        {
            if (!pushable.Enabled)
                return false;

            factor = pushable.Factor;
        }
        else if (_item.GetSizePrototype(item.Comp.Size).Weight > settings.PushMaxWeight)
        {
            return false;
        }

        if (_mobQuery.HasComp(item.Owner)
            || _buckleQuery.TryComp(item.Owner, out var buckle) && buckle.Buckled
            || _container.IsEntityOrParentInContainer(item.Owner))
            return false;

        var xform = _xformQuery.GetComponent(item.Owner);
        if (xform.Anchored || xform.GridUid is not { } gridUid || !_gridQuery.TryComp(gridUid, out var grid))
            return false;

        _roofQuery.TryComp(gridUid, out var roof);
        if (!_wolfWeather.IsExposed((gridUid, grid, roof), _map.GetTileRef(gridUid, grid, xform.Coordinates)))
            return false;

        var ahead = _transform.GetWorldPosition(xform) + direction * LookAhead;
        return _map.TryGetTileRef(gridUid, grid, _map.WorldToTile(gridUid, grid, ahead), out var next) && !next.Tile.IsEmpty;
    }
}
