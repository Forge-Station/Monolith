using System.Numerics;
using Content.Server.Ghost.Roles.Components;
using Content.Shared.Damage;
using Content.Shared.EntityTable;
using Content.Shared.Maps;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Physics;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Planets;

/// <summary>Bounded, player-local wildlife encounters.</summary>
public sealed partial class WFPlanetFaunaSystem : EntitySystem
{
    public const int MaxPerPlanet = 32;
    public const int MaxTotal = 128;
    public const int MaxNearby = 4;
    public const int MaxSites = 2048;
    public static readonly TimeSpan RetirementDelay = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan SiteCooldown = TimeSpan.FromSeconds(180);
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private EntityTableSystem _tables = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;

    private sealed class Site(EntityCoordinates coordinates, ProtoId<EntityTablePrototype> table)
    {
        public readonly EntityCoordinates Coordinates = coordinates;
        public readonly ProtoId<EntityTablePrototype> Table = table;
        public TimeSpan NextSpawn;
        public EntityUid? Source;
        public TimeSpan Cooldown = SiteCooldown;
        public LinkedListNode<(EntityUid, Vector2i)>? Order;
    }

    // Coalesce dense biome markers into 8-tile cells; do not keep growing an infinite-world registry.
    private readonly Dictionary<(EntityUid, Vector2i), Site> _sites = new();
    private readonly LinkedList<(EntityUid, Vector2i)> _siteOrder = new();
    private readonly Dictionary<EntityUid, EntityUid> _living = new();
    private readonly List<EntityUid> _expired = new();
    private readonly List<EntityCoordinates> _observers = new();
    private readonly List<EntityCoordinates> _visitors = new();
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        SubscribeLocalEvent<WFPlanetFaunaSpawnerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFPlanetWildlifeComponent, DamageChangedEvent>(OnDamage);
        SubscribeLocalEvent<WFPlanetWildlifeComponent, EntParentChangedMessage>(OnParentChanged);
    }

    private void OnDamage(Entity<WFPlanetWildlifeComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased)
            ent.Comp.Protected = true;
    }

    private void OnParentChanged(Entity<WFPlanetWildlifeComponent> ent, ref EntParentChangedMessage args)
    {
        if (args.Transform.ParentUid != ent.Comp.Ground)
            ent.Comp.Protected = true;
    }

    private void OnMapInit(Entity<WFPlanetFaunaSpawnerComponent> ent, ref MapInitEvent args)
    {
        if (!ent.Comp.KeepEntity)
            QueueDel(ent);
        var xform = Transform(ent);
        if (xform.MapUid is not { } ground || !HasComp<BiomeComponent>(ground))
            return;
        var coordinates = _transform.WithEntityId(xform.Coordinates, ground);
        var cell = new Vector2i((int)MathF.Floor(coordinates.X / 8), (int)MathF.Floor(coordinates.Y / 8));
        if (_sites.TryGetValue((ground, cell), out var existing))
        {
            if (!ent.Comp.KeepEntity || existing.Source != null)
                return;
            if (existing.Order != null) _siteOrder.Remove(existing.Order);
            _sites.Remove((ground, cell));
        }
        if (_sites.Count >= MaxSites)
        {
            // Oldest discovered site yields to newly explored terrain; no terrain is loaded here.
            if (_siteOrder.First is { } oldest)
            {
                _sites.Remove(oldest.Value);
                _siteOrder.RemoveFirst();
            }
        }
        var site = new Site(coordinates, ent.Comp.Table);
        if (ent.Comp.KeepEntity)
        {
            site.Source = ent.Owner;
            site.Cooldown = TimeSpan.FromSeconds(ent.Comp.SpawnDelay);
            site.NextSpawn = _timing.CurTime + site.Cooldown;
        }
        site.Order = _siteOrder.AddLast((ground, cell));
        _sites.Add((ground, cell), site);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(5);
        _observers.Clear();
        _visitors.Clear();
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } uid || !TryComp(uid, out TransformComponent? xform))
                continue;
            AddObserver(xform);
            if (xform.MapUid is { } ground && HasComp<BiomeComponent>(ground))
                _visitors.Add(_transform.WithEntityId(xform.Coordinates, ground));
            // Orbital/atmospheric viewers protect animals they can see, but never seed surface encounters.
            foreach (var view in session.ViewSubscriptions)
                if (TryComp(view, out TransformComponent? eye))
                    AddObserver(eye);
        }
        RefreshPopulation(_observers, _visitors);
    }

    private void AddObserver(TransformComponent xform)
    {
        if (xform.MapUid is { } ground && HasComp<BiomeComponent>(ground))
            _observers.Add(_transform.WithEntityId(xform.Coordinates, ground));
    }

    /// <summary>One bounded ecology pass. Coordinates are surface-local observer positions.</summary>
    public void RefreshPopulation(IReadOnlyList<EntityCoordinates> observers, IReadOnlyList<EntityCoordinates>? visitors = null)
    {
        visitors ??= observers;
        _expired.Clear();
        foreach (var (uid, ground) in _living)
        {
            if (TerminatingOrDeleted(uid) || !TryComp<MobStateComponent>(uid, out var state) || state.CurrentState == MobState.Dead)
            {
                _expired.Add(uid);
                continue;
            }
            var wildlife = Comp<WFPlanetWildlifeComponent>(uid);
            var xform = Transform(uid);
            if (xform.ParentUid != ground || HasComp<ActorComponent>(uid) ||
                TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind)
                wildlife.Protected = true;
            if (xform.ParentUid != ground || TerminatingOrDeleted(ground))
                continue;
            var pos = _transform.WithEntityId(xform.Coordinates, ground);
            if (NearObserver(pos, observers, 96))
                wildlife.LastNearby = _timing.CurTime;
            else if (!wildlife.Protected && !MetaData(uid).EntityPaused && _timing.CurTime - wildlife.LastNearby >= RetirementDelay)
            {
                QueueDel(uid);
                _expired.Add(uid);
            }
        }
        foreach (var uid in _expired)
            _living.Remove(uid);
        foreach (var key in new List<(EntityUid, Vector2i)>(_sites.Keys))
            if (TerminatingOrDeleted(key.Item1) ||
                _sites[key].Source is { } source &&
                (TerminatingOrDeleted(source) || Transform(source).ParentUid != key.Item1))
            {
                if (_sites[key].Order is { } order)
                    _siteOrder.Remove(order);
                _sites.Remove(key);
            }

        var candidates = new List<Site>();
        foreach (var site in _sites.Values)
        {
            if (site.NextSpawn <= _timing.CurTime && NearObserver(site.Coordinates, visitors, 64) &&
                (site.Source != null || !NearObserver(site.Coordinates, observers, 24)))
                candidates.Add(site);
        }
        var spawned = 0;
        // Random sampling avoids a directional bias toward the first biome chunks enumerated.
        for (var attempts = 0; candidates.Count > 0 && attempts < 64 && spawned < 4; attempts++)
        {
            var index = _random.Next(candidates.Count);
            var site = candidates[index];
            candidates.RemoveAt(index);
            var ground = site.Coordinates.EntityId;
            var local = 0;
            var nearby = 0;
            foreach (var (uid, origin) in _living)
            {
                if (origin != ground)
                    continue;
                local++;
                if (Transform(uid).MapUid == ground &&
                    Vector2.DistanceSquared(_transform.WithEntityId(Transform(uid).Coordinates, ground).Position,
                        site.Coordinates.Position) < 32 * 32)
                    nearby++;
            }
            if (_living.Count >= MaxTotal || local >= MaxPerPlanet || nearby >= MaxNearby)
                continue;
            var spawnCoordinates = site.Coordinates;
            // A sack occupies its own tile. Try a bounded ring beside it, never inside a wall.
            if (site.Source != null)
            {
                var found = false;
                for (var i = 0; i < 8; i++)
                {
                    var angle = i * MathF.Tau / 8;
                    var candidate = site.Coordinates.Offset(new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 2);
                    if (_turf.GetTileRef(candidate) is not { } nearbyTile || nearbyTile.GridUid != ground ||
                        nearbyTile.Tile.IsEmpty || _turf.IsTileBlocked(nearbyTile, CollisionGroup.MobMask)) continue;
                    spawnCoordinates = candidate;
                    found = true;
                    break;
                }
                if (!found) continue;
            }
            var tile = _turf.GetTileRef(spawnCoordinates);
            if (tile is not { } turf || turf.GridUid != ground || turf.Tile.IsEmpty ||
                _turf.IsTileBlocked(turf, CollisionGroup.MobMask))
                continue;
            site.NextSpawn = _timing.CurTime + site.Cooldown;
            foreach (var prototype in _tables.GetSpawns(_proto.Index(site.Table).Table))
            {
                if (_living.Count >= MaxTotal || local >= MaxPerPlanet || nearby >= MaxNearby || spawned >= 4)
                    break;
                var uid = EntityManager.CreateEntityUninitialized(prototype, spawnCoordinates);
                RemComp<GhostRoleComponent>(uid);
                RemComp<GhostTakeoverAvailableComponent>(uid);
                EntityManager.InitializeAndStartEntity(uid);
                if (!HasComp<MobStateComponent>(uid))
                {
                    QueueDel(uid);
                    continue;
                }
                var wildlife = EnsureComp<WFPlanetWildlifeComponent>(uid);
                wildlife.Ground = ground;
                wildlife.LastNearby = _timing.CurTime;
                _living.Add(uid, ground);
                local++;
                nearby++;
                spawned++;
            }
        }
    }

    private static bool NearObserver(EntityCoordinates position, IReadOnlyList<EntityCoordinates> observers, float range)
    {
        foreach (var observer in observers)
            if (observer.EntityId == position.EntityId && Vector2.DistanceSquared(observer.Position, position.Position) < range * range)
                return true;
        return false;
    }
}
