using System.Linq;
using Content.Shared._Forge.KIAS;
using Content.Shared.Destructible;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Server.Destructible;
using Robust.Shared.Timing;
using Robust.Shared.Map;

namespace Content.Server._Forge.KIAS;

public sealed class KiasHullSystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private KiasSafetySystem _safety = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Dictionary<EntityUid, Dictionary<(string Location, byte Kind), float>> _pending = new();
    private readonly Dictionary<EntityUid, TimeSpan> _due = new();
    private readonly Dictionary<EntityUid, Dictionary<EntityUid, TimeSpan>> _collisions = new();
    private TimeSpan _nextFlush;

    public override void Initialize()
    {
        SubscribeLocalEvent<KiasHullDamageEvent>(OnDamage);
        SubscribeLocalEvent<KiasHullImpactEvent>(OnImpact);
        SubscribeLocalEvent<KiasGridCollisionEvent>(OnCollision);
        SubscribeLocalEvent<KiasHullStructureComponent, DestructionEventArgs>(OnDestroyed);
        SubscribeLocalEvent<KiasHullStructureComponent, DamageChangedEvent>(OnNativeDamage, before: new[] { typeof(DestructibleSystem) });
        SubscribeLocalEvent<KiasAvailabilityChangedEvent>(OnAvailability);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoval);
    }

    public bool HasMonitor<T>(EntityUid grid, EntityUid? source = null) where T : Component
    {
        if (!_kias.HasRole(grid, KiasDeviceRole.Defence) || !TryComp<KiasGridComponent>(grid, out var runtime))
            return false;
        foreach (var device in runtime.Online)
        {
            if (HasComp<T>(device) && _kias.IsOnline(device))
            {
                if (source is { } target && typeof(T) == typeof(KiasHullSensorComponent)
                    && !TerminatingOrDeleted(target) && TryComp<KiasHullSensorComponent>(device, out var sensor)
                    && System.Numerics.Vector2.DistanceSquared(Transform(device).LocalPosition, Transform(target).LocalPosition)
                        > Math.Clamp(sensor.Range, 50, 100) * Math.Clamp(sensor.Range, 50, 100)) continue;
                return true;
            }
        }
        return false;
    }

    private void OnDamage(ref KiasHullDamageEvent args)
    {
        if (args.Damage > 0 && HasMonitor<KiasIntegrityMonitorComponent>(args.Grid))
            Accumulate(args.Grid, args.Structure, 0, args.Damage);
    }

    private void OnNativeDamage(EntityUid uid, KiasHullStructureComponent component, DamageChangedEvent args)
    {
        if (args.DamageDelta is not { } delta || !delta.AnyPositive() || Transform(uid).GridUid is not { } grid) return;
        var positive = FixedPoint2.Zero;
        foreach (var value in delta.DamageDict.Values)
            if (value > FixedPoint2.Zero) positive += value;
        var damage = new KiasHullDamageEvent(grid, uid, (float) positive);
        RaiseLocalEvent(grid, ref damage, true);
    }

    private void OnImpact(ref KiasHullImpactEvent args)
    {
        if (HasMonitor<KiasHullSensorComponent>(args.Grid, args.Structure))
            Accumulate(args.Grid, args.Structure, 1, 1);
    }

    private void OnDestroyed(Entity<KiasHullStructureComponent> ent, ref DestructionEventArgs args)
    {
        if (Transform(ent).GridUid is { } grid && HasMonitor<KiasIntegrityMonitorComponent>(grid))
            Accumulate(grid, ent, 2, 1);
    }

    private void Accumulate(EntityUid grid, EntityUid source, byte kind, float amount)
    {
        if (!_pending.TryGetValue(grid, out var buckets))
        {
            buckets = new();
            _pending.Add(grid, buckets);
            _due.Add(grid, _timing.CurTime + TimeSpan.FromSeconds(1));
            if (_due.Count == 1 || _due[grid] < _nextFlush)
                _nextFlush = _due[grid];
        }
        var location = Deleted(source) ? Loc.GetString("kias-sector-center") : _safety.Location(grid, source);
        var key = (location, kind);
        if (buckets.Count >= 16 && !buckets.ContainsKey(key))
            key = (Loc.GetString("kias-hull-other-sectors"), kind);
        buckets[key] = buckets.GetValueOrDefault(key) + amount;
    }

    private void OnCollision(ref KiasGridCollisionEvent args)
    {
        if (!DetectsCollision(args.Grid, args.RelativeSpeed))
            return;
        if (!_collisions.TryGetValue(args.Grid, out var pairs))
            _collisions.Add(args.Grid, pairs = new());
        foreach (var other in pairs.Where(p => p.Value <= _timing.CurTime).Select(p => p.Key).ToArray())
            pairs.Remove(other);
        if (pairs.ContainsKey(args.OtherGrid))
            return;
        if (pairs.Count >= 32)
            return;
        pairs.Add(args.OtherGrid, _timing.CurTime + TimeSpan.FromSeconds(3));
        _safety.Publish(args.Grid, Loc.GetString("kias-grid-collision", ("speed", args.RelativeSpeed)), true, announce: false, key: $"collision:{args.OtherGrid}");
    }

    public bool DetectsCollision(EntityUid grid, float speed)
    {
        if (!HasMonitor<KiasCollisionMonitorComponent>(grid) || !TryComp<KiasGridComponent>(grid, out var runtime))
            return false;
        return runtime.Online.Any(uid => _kias.IsOnline(uid) && TryComp<KiasCollisionMonitorComponent>(uid, out var sensor)
            && speed >= Math.Max(0.1f, sensor.MinimumSpeed));
    }

    private void OnAvailability(ref KiasAvailabilityChangedEvent args)
    {
        if (args.Active)
            return;
        _pending.Remove(args.Grid);
        _due.Remove(args.Grid);
        _collisions.Remove(args.Grid);
    }

    private void OnGridRemoval(GridRemovalEvent args)
    {
        _pending.Remove(args.EntityUid);
        _due.Remove(args.EntityUid);
        _collisions.Remove(args.EntityUid);
    }

    public override void Update(float frameTime)
    {
        using var measurement = new KiasUpdateMeasurement(_kias);
        if (_due.Count == 0 || _timing.CurTime < _nextFlush)
            return;
        foreach (var grid in _due.Where(p => p.Value <= _timing.CurTime).Select(p => p.Key).ToArray())
        {
            var buckets = _pending[grid];
            _pending.Remove(grid);
            _due.Remove(grid);
            foreach (var (key, value) in buckets)
            {
                if (key.Kind == 1 ? !HasMonitor<KiasHullSensorComponent>(grid) : !HasMonitor<KiasIntegrityMonitorComponent>(grid))
                    continue;
                var message = key.Kind switch { 1 => "kias-hull-impact", 2 => "kias-hull-destroyed", _ => "kias-hull-damage" };
                _safety.Publish(grid, Loc.GetString(message,
                    ("location", key.Location), ("amount", value)), true, announce: false, key: $"hull:{key.Kind}:{key.Location}");
            }
        }
        if (_due.Count > 0)
            _nextFlush = _due.Values.Min();
    }
}
