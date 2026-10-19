using System.Numerics;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Server._Forge.KIAS.Controllers;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Server._Mono.SpaceArtillery.Components;
using Content.Server._Mono.Radar;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using Content.Server.Shuttles.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared._NF.Shipyard.Components;
using System.Linq;

namespace Content.Server._Forge.KIAS;

public sealed class KiasExternalSensorSystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private KiasNavigationSystem _navigation = default!;
    [Dependency] private KiasSafetySystem _safety = default!;
    [Dependency] private KiasControllerIoSystem _controllers = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IGameTiming _timing = default!;
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _proximity = new();
    private readonly KiasPeriodicScheduler _scans = new(1);
    private readonly HashSet<Entity<KiasWeaponFlashComponent>> _flashLookup = new();
    private readonly HashSet<EntityUid> _announced = new();
    private bool _processingFlash;

    public override void Initialize()
    {
        SubscribeLocalEvent<KiasWeaponFiredEvent>(OnFired);
        SubscribeLocalEvent<TransformComponent, AmmoShotEvent>(OnNativeFired);
        SubscribeLocalEvent<KiasTopologyChangedEvent>(OnTopology);
        SubscribeLocalEvent<KiasAvailabilityChangedEvent>(OnAvailability);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoved);
        SubscribeLocalEvent<KiasWeaponFlashEvent>(OnFlash);
        SubscribeLocalEvent<KiasProximityEvent>(OnProximity);
        SubscribeLocalEvent<GridSplitEvent>(OnSplit);
    }

    private void OnSplit(ref GridSplitEvent args)
    {
        var origin = EnsureComp<KiasGridOriginComponent>(args.Grid);
        if (!origin.Origin.Valid) origin.Origin = args.Grid;
        foreach (var fragment in args.NewGrids)
            EnsureComp<KiasGridOriginComponent>(fragment).Origin = origin.Origin;
    }

    public bool IsVesselContact(EntityUid observer, EntityUid contact)
    {
        if (observer == contact || TerminatingOrDeleted(contact) || HasComp<MapComponent>(contact)
            || !TryComp<ShuttleComponent>(contact, out var shuttle)) return false;
        if (TryComp<KiasGridOriginComponent>(observer, out var own)
            && own.Origin.Valid && TryComp<KiasGridOriginComponent>(contact, out var other) && own.Origin == other.Origin) return false;
        return HasComp<IFFComponent>(contact) || HasComp<ShuttleDeedComponent>(contact)
            || shuttle.LinearThrust.Any(thrust => thrust > 0);
    }

    private void OnGridRemoved(GridRemovalEvent args) => Clear(args.EntityUid);
    private void OnAvailability(ref KiasAvailabilityChangedEvent args)
    {
        if (!args.Active)
            Clear(args.Grid);
    }

    private void Clear(EntityUid grid)
    {
        if (!_proximity.Remove(grid, out var sensors))
            return;
        foreach (var uid in sensors)
        {
            _scans.Remove(uid);
            if (TryComp<KiasProximityComponent>(uid, out var sensor))
                sensor.Contacts.Clear();
        }
    }

    private void OnTopology(ref KiasTopologyChangedEvent args)
    {
        if (!_kias.HasRole(args.Grid, KiasDeviceRole.Navigation) || !TryComp<KiasGridComponent>(args.Grid, out var runtime))
        {
            Clear(args.Grid);
            return;
        }
        var sensors = runtime.Online.Where(HasComp<KiasProximityComponent>).ToHashSet();
        if (_proximity.TryGetValue(args.Grid, out var previous))
        {
            foreach (var removed in previous.Except(sensors))
            {
                _scans.Remove(removed);
                if (TryComp<KiasProximityComponent>(removed, out var sensor))
                    sensor.Contacts.Clear();
            }
        }
        if (sensors.Count > 0)
        {
            _proximity[args.Grid] = sensors;
            foreach (var uid in sensors) _scans.Add(uid, _timing.CurTime);
        }
        else
            _proximity.Remove(args.Grid);
    }

    private void OnFired(ref KiasWeaponFiredEvent args)
    {
        if (_processingFlash || _kias.ActiveGrids.Count == 0 || TerminatingOrDeleted(args.Source))
            return;
        _processingFlash = true;
        try { ProcessFire(args.Source); }
        finally { _processingFlash = false; }
    }

    private void OnNativeFired(EntityUid uid, TransformComponent component, AmmoShotEvent args)
    {
        if (!HasComp<GunComponent>(uid) || !HasComp<SpaceArtilleryComponent>(uid)
            || !HasComp<RadarBlipComponent>(uid)) return;
        var fired = new KiasWeaponFiredEvent(uid);
        OnFired(ref fired);
    }

    private void ProcessFire(EntityUid sourceUid)
    {
        var source = _transform.GetMapCoordinates(sourceUid);
        var sourceGrid = Transform(sourceUid).GridUid;
        _announced.Clear();
        _flashLookup.Clear();
        _lookup.GetEntitiesInRange(source, 1000, _flashLookup);
        foreach (var detector in _flashLookup)
        {
            if (!_kias.IsOnline(detector) || Transform(detector).GridUid is not { } grid || grid == sourceGrid
                || !_kias.HasRole(grid, KiasDeviceRole.Defence))
                continue;
            var position = _transform.GetMapCoordinates(detector);
            var offset = source.Position - position.Position;
            var range = Math.Clamp(detector.Comp.Range, 0, 1000);
            if (source.MapId != position.MapId || offset.LengthSquared() > range * range || offset.LengthSquared() < 0.01f)
                continue;
            var forward = _transform.GetWorldRotation(detector).ToWorldVec();
            if (Vector2.Dot(Vector2.Normalize(offset), forward) < MathF.Cos(MathF.PI / 4))
                continue;
            var disposition = sourceGrid is { } ship ? _navigation.Classify(grid, ship) : KiasContactDisposition.Unknown;
            _controllers.Emit(detector, "WeaponFlashDetector", "Source", KiasGraphValue.Reference(sourceUid));
            _controllers.Emit(detector, "WeaponFlashDetector", "Disposition", KiasGraphValue.Enumeration((int) disposition));
            _controllers.Emit(detector, "WeaponFlashDetector", "Triggered", KiasGraphValue.Pulse);
            if (!_announced.Add(grid))
                continue;
            var ev = new KiasWeaponFlashEvent(grid, sourceUid, disposition);
            RaiseLocalEvent(grid, ref ev, true);
        }
    }

    private void OnFlash(ref KiasWeaponFlashEvent args)
    {
        if (args.Disposition is KiasContactDisposition.Hostile or KiasContactDisposition.Unknown)
            _safety.Publish(args.Grid, Loc.GetString("kias-weapon-flash"), true, announce: false, key: $"flash:{args.Source}");
    }

    private void OnProximity(ref KiasProximityEvent args) => _safety.Publish(args.Grid,
        Loc.GetString("kias-proximity-contact", ("range", MathF.Round(args.Distance)),
            ("disposition", Loc.GetString($"kias-contact-{args.Disposition.ToString().ToLowerInvariant()}"))),
        args.Disposition is KiasContactDisposition.Hostile or KiasContactDisposition.Unknown, announce: false, key: $"proximity:{args.Contact}");

    public override void Update(float frameTime)
    {
        const int sensorBudget = 8;
        for (var i = 0; i < sensorBudget && _scans.TryDue(_timing.CurTime, out var uid); i++)
        {
            if (!_kias.IsOnline(uid) || !TryComp<KiasProximityComponent>(uid, out var sensor) || Transform(uid).GridUid is not { } grid)
                continue;
            var position = _transform.GetMapCoordinates(uid);
            var range = Math.Clamp(sensor.Range, 0, 500);
            var nearby = new List<Entity<MapGridComponent>>();
            _map.FindGridsIntersecting(position.MapId, new Box2(position.Position - new Vector2(range), position.Position + new Vector2(range)), ref nearby, includeMap: false);
            var current = new HashSet<EntityUid>();
            foreach (var contact in nearby.OrderBy(contact => sensor.Contacts.Contains(contact.Owner) ? 0 : 1).ThenBy(contact => contact.Owner.Id))
            {
                if (!IsVesselContact(grid, contact))
                    continue;
                var target = _transform.ToMapCoordinates(new EntityCoordinates(contact, contact.Comp.LocalAABB.Center));
                var distance = Vector2.Distance(target.Position, position.Position);
                if (distance > range || current.Count >= 16)
                    continue;
                current.Add(contact);
                if (sensor.Contacts.Contains(contact))
                    continue;
                var disposition = _navigation.Classify(grid, contact);
                _controllers.Emit(uid, "ProximitySensor", "ContactEntity", KiasGraphValue.Reference(contact));
                _controllers.Emit(uid, "ProximitySensor", "Distance", KiasGraphValue.Numeric(distance));
                _controllers.Emit(uid, "ProximitySensor", "Disposition", KiasGraphValue.Enumeration((int) disposition));
                _controllers.Emit(uid, "ProximitySensor", "Contact", KiasGraphValue.Pulse);
                var ev = new KiasProximityEvent(grid, contact, distance, disposition);
                RaiseLocalEvent(grid, ref ev, true);
            }
            sensor.Contacts.Clear();
            sensor.Contacts.UnionWith(current);
        }
    }
}
