using System.Linq;
using Content.Server.Atmos.Monitor.Components;
using Content.Server.Atmos.Monitor.Systems;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Server._Forge.KIAS.Controllers;
using Content.Shared.Atmos.Monitor;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Map.Components;
using Content.Server.Shuttles.Components;
using Robust.Shared.Map;

namespace Content.Server._Forge.KIAS;

public sealed class KiasProtocolSystem : EntitySystem
{
    public void SetAlert(EntityUid grid, KiasAlert alert)
    {
        if (TryProtocol(grid, out var protocols)) protocols.Alert = alert;
    }
    public void ResetAlert(EntityUid grid)
    {
        if (!TryProtocol(grid, out var protocols)) return;
        protocols.Alert = KiasAlert.Normal;
        protocols.MaydayReason = string.Empty;
        protocols.CriticalLatched = false;
        protocols.CriticalSince = protocols.CriticalRecoverySince = TimeSpan.Zero;
        AcknowledgeCrew(grid);
        _shuttles.SetKiasMayday(grid, false);
        if (Comp<KiasGridComponent>(grid).Core is { } core)
            EntityManager.System<KiasControllerIoSystem>().Emit(core, "Automation", "AlertReset", KiasGraphValue.Pulse);
        _display.RefreshOpen(grid);
    }
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private KiasSafetySystem _safety = default!;
    [Dependency] private KiasCrewSystem _crew = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private SharedShuttleSystem _shuttles = default!;
    [Dependency] private KiasDisplaySystem _display = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private KiasHullSystem _hull = default!;
    private readonly HashSet<EntityUid> _executing = new();
    private readonly KiasPeriodicScheduler _emergencies = new(1);
    private readonly HashSet<EntityUid> _fireAlarms = new();
    private readonly HashSet<EntityUid> _atmosAlarms = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<KiasHullImpactEvent>(OnImpact);
        SubscribeLocalEvent<KiasHullDamageEvent>(OnDamage);
        SubscribeLocalEvent<KiasGridCollisionEvent>(OnCollision);
        SubscribeLocalEvent<KiasAnomalyGrowthEvent>(OnAnomaly);
        SubscribeLocalEvent<KiasAtmosStateChangedEvent>(OnAtmos);
        SubscribeLocalEvent<KiasBluespaceDisturbanceEvent>(OnContact);
        SubscribeLocalEvent<KiasAutopilotArrivedEvent>(OnArrival);
        SubscribeLocalEvent<KiasWeaponFlashEvent>(OnFlash);
        SubscribeLocalEvent<KiasProximityEvent>(OnProximity);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<AtmosAlarmEvent>(OnFireAlarm);
        SubscribeLocalEvent<KiasPowerDeficitEvent>(OnPowerDeficit);
        SubscribeLocalEvent<KiasDisplayComponent, KiasControlMessage>(OnControl);
        SubscribeLocalEvent<KiasAvailabilityChangedEvent>(OnAvailability);
        SubscribeLocalEvent<KiasTopologyChangedEvent>(OnTopology);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoved);
        SubscribeLocalEvent<TileChangedEvent>(OnTilesChanged);
    }

    private void OnTilesChanged(ref TileChangedEvent args)
    {
        if (TryProtocol(args.Entity.Owner, out var protocols)) protocols.HullSampled = false;
    }

    private void OnImpact(ref KiasHullImpactEvent args)
    {
        if (_hull.HasMonitor<KiasHullSensorComponent>(args.Grid, args.Structure))
            Trigger(args.Grid, KiasTrigger.HullImpact, message: Loc.GetString("kias-hull-impact", ("location", _safety.Location(args.Grid, args.Structure)), ("amount", 1)));
    }
    private void OnGridRemoved(GridRemovalEvent args)
    {
        _emergencies.Remove(args.EntityUid);
        ClearAlarmSources(args.EntityUid);
    }

    private void ClearAlarmSources(EntityUid grid)
    {
        _fireAlarms.RemoveWhere(uid => TerminatingOrDeleted(uid) || Transform(uid).GridUid == grid);
        _atmosAlarms.RemoveWhere(uid => TerminatingOrDeleted(uid) || Transform(uid).GridUid == grid);
    }

    private void OnTopology(ref KiasTopologyChangedEvent args)
    {
        ReconcileAlarms(args.Grid);
        if (TryProtocol(args.Grid, out var protocols)) SampleHull(args.Grid, protocols);
    }

    private void SampleHull(EntityUid grid, KiasProtocolComponent protocols)
    {
        if (TryComp<ShuttleComponent>(grid, out var shuttle) && shuttle.LinearThrust.Any(thrust => thrust > 0))
            protocols.HadPropulsion = true;
        if (!TryComp<MapGridComponent>(grid, out var map)
            || protocols.HullSampled && protocols.HullTileRevision == map.LastTileModifiedTick.Value) return;
        protocols.HullSampled = true;
        protocols.HullTileRevision = map.LastTileModifiedTick.Value;
        var count = EntityManager.System<SharedMapSystem>().GetFilledTileCount((grid, map));
        protocols.HullTiles = count;
        protocols.HullBaselineTiles = Math.Max(protocols.HullBaselineTiles, count);
    }

    public bool HasCriticalHullFailure(EntityUid grid)
    {
        if (!TryProtocol(grid, out var protocols)) return false;
        SampleHull(grid, protocols);
        if (protocols.DamageEvidenceUntil <= _timing.CurTime && !protocols.CriticalLatched) return false;
        var hullLoss = protocols.HullBaselineTiles > 0
            && protocols.HullBaselineTiles - protocols.HullTiles >= protocols.HullBaselineTiles * Math.Clamp(protocols.CriticalHullLossFraction, 0.1f, 1);
        var disabled = protocols.HadPropulsion && (!TryComp<ShuttleComponent>(grid, out var shuttle)
            || !shuttle.Enabled || !shuttle.LinearThrust.Any(thrust => thrust > 0));
        return hullLoss || disabled;
    }

    private void ReconcileAlarms(EntityUid grid)
    {
        var integration = EntityManager.System<KiasIntegrationSystem>();
        var enabled = _kias.HasRole(grid, KiasDeviceRole.Atmosphere);
        bool Observable(EntityUid source) => enabled && HasComp<KiasIntegratedComponent>(source) && integration.CanControl(source);
        void Reconcile(HashSet<EntityUid> sources, bool fire)
        {
            foreach (var source in sources.ToArray())
            {
                if (TerminatingOrDeleted(source)) { sources.Remove(source); continue; }
                if (Transform(source).GridUid != grid) continue;
                if (!Observable(source)) { sources.Remove(source); continue; }
                if (!TryComp<AtmosAlarmableComponent>(source, out var alarm) || alarm.LastAlarmState != AtmosAlarmType.Normal) continue;
                if (fire) OnFireAlarm(new AtmosAlarmEvent(AtmosAlarmType.Normal, source));
                else
                {
                    var clear = new KiasAtmosStateChangedEvent(grid, source, AtmosAlarmType.Normal, _safety.Location(grid, source));
                    RaiseLocalEvent(grid, ref clear, true);
                }
            }
        }
        Reconcile(_fireAlarms, true);
        Reconcile(_atmosAlarms, false);
        if (!enabled) return;
        foreach (var source in integration.NativeTargets(grid))
            if (HasComp<FireAlarmComponent>(source) && Observable(source)
                && TryComp<AtmosAlarmableComponent>(source, out var alarm) && alarm.LastAlarmState == AtmosAlarmType.Danger)
            {
                if (_fireAlarms.Add(source)) _emergencies.Add(grid, _timing.CurTime);
            }
    }

    private bool HasDistress(EntityUid grid, KiasProtocolComponent protocols) => protocols.DamageEvidenceUntil > _timing.CurTime
        || protocols.CrewDistressUntil > _timing.CurTime || EntityManager.System<KiasPowerSystem>().HasDeficit(grid)
        || protocols.CriticalLatched && (HasCriticalHullFailure(grid) || _crew.CrewUnavailable(grid))
        || _fireAlarms.Any(alarm => !TerminatingOrDeleted(alarm) && Transform(alarm).GridUid == grid);

    private EntityUid Antenna(EntityUid grid) => TryComp<KiasGridComponent>(grid, out var runtime)
        ? runtime.Online.FirstOrDefault(uid => HasComp<KiasMaydayAntennaComponent>(uid) && _kias.IsOnline(uid)) : default;

    public bool MedicalHelp(EntityUid grid, string reason)
    {
        if (!TryProtocol(grid, out var protocols) || protocols.MedicalAfter > _timing.CurTime
            || protocols.CrewUnavailableSince == TimeSpan.Zero || _timing.CurTime - protocols.CrewUnavailableSince < TimeSpan.FromSeconds(30)
            || !_crew.CrewUnavailable(grid) || !HasDistress(grid, protocols) || Comp<KiasGridComponent>(grid).Testing) return false;
        var source = Antenna(grid);
        if (!source.Valid) return false;
        protocols.MedicalAfter = _timing.CurTime + TimeSpan.FromSeconds(180);
        var position = EntityManager.System<SharedTransformSystem>().GetMapCoordinates(grid);
        var message = Loc.GetString("kias-medical-help", ("ship", Name(grid)), ("x", MathF.Round(position.X)),
            ("y", MathF.Round(position.Y)), ("reason", reason));
        _radio.SendRadioMessage(source, message, "Medical", source);
        _safety.Publish(grid, message, true, announce: false, key: "medical-help");
        return true;
    }

    public override void Update(float frameTime)
    {
        for (var i = 0; i < 8 && _emergencies.TryDue(_timing.CurTime, out var grid); i++)
        {
            if (!TryProtocol(grid, out var protocols)) { _emergencies.Remove(grid); continue; }
            ReconcileAlarms(grid);
            var distress = HasDistress(grid, protocols);
            if (distress && _crew.CrewUnavailable(grid))
            {
                if (protocols.CrewUnavailableSince == TimeSpan.Zero) protocols.CrewUnavailableSince = _timing.CurTime;
                if (_timing.CurTime - protocols.CrewUnavailableSince >= TimeSpan.FromSeconds(30))
                    Trigger(grid, KiasTrigger.CrewUnavailable);
            }
            else protocols.CrewUnavailableSince = TimeSpan.Zero;
            var critical = HasCriticalHullFailure(grid) || distress
                && (protocols.CrewDistressUntil > _timing.CurTime || protocols.CriticalLatched)
                && protocols.CrewUnavailableSince != TimeSpan.Zero
                && _timing.CurTime - protocols.CrewUnavailableSince >= TimeSpan.FromSeconds(30);
            if (critical)
            {
                protocols.CriticalRecoverySince = TimeSpan.Zero;
                if (protocols.CriticalSince == TimeSpan.Zero) protocols.CriticalSince = _timing.CurTime;
                if (!protocols.CriticalLatched && _timing.CurTime - protocols.CriticalSince >= TimeSpan.FromSeconds(Math.Clamp(protocols.CriticalConfirmationSeconds, 1, 120)))
                {
                    protocols.CriticalLatched = true;
                    Trigger(grid, KiasTrigger.VesselCritical);
                }
            }
            else
            {
                protocols.CriticalSince = TimeSpan.Zero;
                if (protocols.CriticalRecoverySince == TimeSpan.Zero) protocols.CriticalRecoverySince = _timing.CurTime;
                if (_timing.CurTime - protocols.CriticalRecoverySince >= TimeSpan.FromSeconds(30)) protocols.CriticalLatched = false;
            }
            if (protocols.PendingMaydayReason.Length > 0)
            {
                if (!distress) CancelMayday(protocols);
                else if (_timing.CurTime >= protocols.CrewResponseDeadline) SendMayday(grid, protocols);
            }
            if (protocols.MaydayReason.Length > 0)
            {
                _shuttles.SetKiasMayday(grid, Antenna(grid).Valid);
            }
            if (!distress && protocols.MaydayReason.Length == 0 && !protocols.CriticalLatched && protocols.PendingMaydayReason.Length == 0)
                _emergencies.Remove(grid);
        }
    }

    private void OnPowerDeficit(ref KiasPowerDeficitEvent args)
    {
        Trigger(args.Grid, KiasTrigger.PowerDeficit, value: args.Consumption - args.Supply,
            message: Loc.GetString("kias-power-deficit", ("channel", Loc.GetString($"kias-relay-{args.Channel.ToString().ToLowerInvariant()}"))), eventKey: args.Channel.ToString());
        _emergencies.Add(args.Grid, _timing.CurTime);
    }
    private void OnCollision(ref KiasGridCollisionEvent args)
    {
        if (_hull.DetectsCollision(args.Grid, args.RelativeSpeed))
            Trigger(args.Grid, KiasTrigger.Collision, value: args.RelativeSpeed, eventKey: args.OtherGrid.ToString());
    }
    private void OnAnomaly(ref KiasAnomalyGrowthEvent args) => Trigger(args.Grid, KiasTrigger.AnomalyGrowth,
        message: Loc.GetString("kias-anomaly-growth", ("location", args.Location)), eventKey: args.Source.ToString());
    private void OnAtmos(ref KiasAtmosStateChangedEvent args)
    {
        if (args.State == AtmosAlarmType.Danger)
        {
            _atmosAlarms.Add(args.Source);
            Trigger(args.Grid, KiasTrigger.AtmosDanger, message: Loc.GetString("kias-atmos-danger", ("location", args.Location)), eventKey: args.Source.ToString());
        }
        else if (args.State == AtmosAlarmType.Normal && _atmosAlarms.Remove(args.Source))
            Trigger(args.Grid, KiasTrigger.AtmosClear, message: Loc.GetString("kias-atmos-clear", ("location", args.Location)), eventKey: args.Source.ToString());
    }
    private void OnContact(ref KiasBluespaceDisturbanceEvent args) => Trigger(args.Grid, KiasTrigger.Contact, args.Disposition, args.Distance,
        Loc.GetString("kias-contact", ("disposition", Loc.GetString($"kias-contact-{args.Disposition.ToString().ToLowerInvariant()}")),
            ("range", MathF.Round(args.Distance / 1000f, 1)), ("bearing", Math.Round(args.Bearing))), args.Contact.ToString());
    private void OnArrival(ref KiasAutopilotArrivedEvent args) => Trigger(args.Grid, KiasTrigger.Arrival, message: Loc.GetString("kias-autopilot-arrived"));
    private void OnFlash(ref KiasWeaponFlashEvent args) => Trigger(args.Grid, KiasTrigger.WeaponFlash, args.Disposition,
        message: Loc.GetString("kias-weapon-flash"), eventKey: args.Source.ToString());
    private void OnProximity(ref KiasProximityEvent args) => Trigger(args.Grid, KiasTrigger.Proximity, args.Disposition, args.Distance, eventKey: args.Contact.ToString());

    private void OnDamage(ref KiasHullDamageEvent args)
    {
        if (!_hull.HasMonitor<KiasIntegrityMonitorComponent>(args.Grid))
            return;
        Trigger(args.Grid, KiasTrigger.HullDamage, value: args.Damage, eventKey: args.Structure.ToString());
        if (!TryProtocol(args.Grid, out var protocols))
            return;
        if (_timing.CurTime > protocols.DamageWindow)
        {
            protocols.RecentDamage = 0;
            protocols.DamageWindow = _timing.CurTime + TimeSpan.FromSeconds(5);
        }
        protocols.RecentDamage += args.Damage;
        if (protocols.RecentDamage >= protocols.CriticalDamageThreshold)
        {
            protocols.DamageEvidenceUntil = _timing.CurTime + TimeSpan.FromSeconds(60);
            _emergencies.Add(args.Grid, _timing.CurTime);
        }
    }

    private void OnMobState(MobStateChangedEvent args)
    {
        if (args.NewMobState is not (MobState.Critical or MobState.Dead) || !_crew.IsKiasTrackedEntity(args.Target)
            || Transform(args.Target).GridUid is not { } grid || !_kias.HasRole(grid, KiasDeviceRole.Crew)
            || !_crew.HasCoverage(grid, args.Target, KiasScannerModules.Biometric))
            return;
        var dead = args.NewMobState == MobState.Dead;
        var ev = new KiasCrewDistressEvent(grid, args.Target, dead);
        RaiseLocalEvent(grid, ref ev, true);
        var message = Loc.GetString(dead ? "kias-crew-dead" : "kias-crew-critical", ("location", _safety.Location(grid, args.Target)));
        _safety.Publish(grid, message, true, announce: false, key: $"crew:{args.Target}:{dead}");
        Trigger(grid, dead ? KiasTrigger.CrewDead : KiasTrigger.CrewCritical, message: message, eventKey: args.Target.ToString());
        if (_crew.IsRegisteredPerson(grid, args.Target) && TryProtocol(grid, out var clinical)) clinical.CrewDistressUntil = _timing.CurTime + TimeSpan.FromSeconds(60);
        if (_crew.IsRegisteredPerson(grid, args.Target) && _crew.HasUnknownAlongside(grid, args.Target))
            Trigger(grid, KiasTrigger.Boarding, message: Loc.GetString("kias-boarding", ("location", _safety.Location(grid, args.Target))), eventKey: args.Target.ToString());
        _emergencies.Add(grid, _timing.CurTime);
    }

    private void OnFireAlarm(AtmosAlarmEvent args)
    {
        if (args.Source is not { } source || !HasComp<FireAlarmComponent>(source)
            || Transform(source).GridUid is not { } grid || !_kias.HasRole(grid, KiasDeviceRole.Atmosphere)
            || !HasComp<KiasIntegratedComponent>(source) || !EntityManager.System<KiasIntegrationSystem>().CanControl(source))
            return;
        if (args.AlarmType != AtmosAlarmType.Danger)
        {
            if (args.AlarmType == AtmosAlarmType.Normal && _fireAlarms.Remove(source))
                Trigger(grid, KiasTrigger.FireClear, message: Loc.GetString("kias-fire-clear", ("location", _safety.Location(grid, source))), eventKey: source.ToString());
            return;
        }
        _fireAlarms.Add(source);
        _emergencies.Add(grid, _timing.CurTime);
        var ev = new KiasFireDetectedEvent(grid, source);
        RaiseLocalEvent(grid, ref ev, true);
        var message = Loc.GetString("kias-fire-alarm", ("location", _safety.Location(grid, source)));
        _safety.Publish(grid, message, true, announce: false, key: $"fire:{source}");
        Trigger(grid, KiasTrigger.Fire, message: message, eventKey: source.ToString());
    }

    private bool TryProtocol(EntityUid grid, out KiasProtocolComponent protocols)
    {
        protocols = default!;
        if (_kias.ActiveGrids.Contains(grid) && TryComp<KiasGridComponent>(grid, out var runtime)
            && runtime.Core is { } core && TryComp<KiasProtocolComponent>(core, out var found))
        {
            protocols = found;
            return true;
        }
        return false;
    }

    public void Trigger(EntityUid grid, KiasTrigger trigger, KiasContactDisposition? disposition = null, float value = 0, string? message = null, string? eventKey = null)
    {
        if (!TryProtocol(grid, out _)) return;
        if (trigger is KiasTrigger.HullDamage or KiasTrigger.HullImpact or KiasTrigger.Proximity or KiasTrigger.PowerLost
            or KiasTrigger.CrewUnavailable or KiasTrigger.VesselCritical)
            eventKey = trigger.ToString();
        if (!_executing.Add(grid)) return;
        try
        {
            if (Comp<KiasGridComponent>(grid).Core is not { } source) return;
            var controllers = EntityManager.System<KiasControllerIoSystem>();
            controllers.Emit(source, "Automation", "Message", KiasGraphValue.String(message ?? Loc.GetString($"kias-trigger-{trigger.ToString().ToLowerInvariant()}")));
            controllers.Emit(source, "Automation", "Value", KiasGraphValue.Numeric(value));
            controllers.Emit(source, "Automation", "Disposition", KiasGraphValue.Enumeration(disposition is { } contact ? (int) contact : -1));
            controllers.Emit(source, "Automation", "AllCrewUnavailable", KiasGraphValue.Boolean(_crew.CrewUnavailable(grid)));
            controllers.Emit(source, "Automation", "EventKey", KiasGraphValue.String(eventKey ?? string.Empty));
            controllers.Emit(source, "Automation", trigger.ToString(), KiasGraphValue.Pulse);
        }
        finally { _executing.Remove(grid); _display.RefreshOpen(grid); }
    }

    public bool Mayday(EntityUid grid, string reason)
    {
        if (!_kias.HasRole(grid, KiasDeviceRole.Navigation) || !TryProtocol(grid, out var protocols)
            || protocols.MaydayAfter > _timing.CurTime || protocols.CrewAcknowledgedUntil > _timing.CurTime
            || protocols.PendingMaydayReason.Length > 0 || !HasDistress(grid, protocols))
            return false;
        var runtime = Comp<KiasGridComponent>(grid);
        if (runtime.Testing)
            return false;
        var source = Antenna(grid);
        if (!source.Valid)
            return false;
        protocols.PendingMaydayReason = string.IsNullOrWhiteSpace(reason) ? Loc.GetString("kias-trigger-vesselcritical") : reason[..Math.Min(reason.Length, 256)];
        protocols.CrewResponseDeadline = _timing.CurTime + TimeSpan.FromSeconds(Math.Clamp(protocols.CrewResponseSeconds, 10, 120));
        _emergencies.Add(grid, _timing.CurTime);
        _safety.Publish(grid, Loc.GetString("kias-crew-response-request", ("seconds", Math.Clamp(protocols.CrewResponseSeconds, 10, 120))),
            true, key: "crew-response-request", channel: KiasAudioChannel.Emergency);
        return true;
    }

    private static void CancelMayday(KiasProtocolComponent protocols)
    {
        protocols.PendingMaydayReason = string.Empty;
        protocols.CrewResponseDeadline = TimeSpan.Zero;
    }

    public bool AcknowledgeCrew(EntityUid grid)
    {
        if (!TryProtocol(grid, out var protocols)) return false;
        CancelMayday(protocols);
        protocols.CrewAcknowledgedUntil = _timing.CurTime + TimeSpan.FromSeconds(180);
        protocols.MaydayReason = string.Empty;
        _shuttles.SetKiasMayday(grid, false);
        _display.RefreshOpen(grid);
        return true;
    }

    private void SendMayday(EntityUid grid, KiasProtocolComponent protocols)
    {
        var source = Antenna(grid);
        if (!source.Valid || !_kias.HasRole(grid, KiasDeviceRole.Navigation)) { CancelMayday(protocols); return; }
        protocols.MaydayReason = protocols.PendingMaydayReason;
        CancelMayday(protocols);
        protocols.MaydayAfter = _timing.CurTime + TimeSpan.FromSeconds(180);
        var position = EntityManager.System<SharedTransformSystem>().GetMapCoordinates(grid);
        var message = Loc.GetString("kias-mayday", ("ship", Name(grid)), ("reason", protocols.MaydayReason),
            ("x", MathF.Round(position.X)), ("y", MathF.Round(position.Y)),
            ("map", Transform(grid).MapUid is { } mapUid ? Name(mapUid) : position.MapId.ToString()));
        _radio.SendRadioMessage(source, message, "Traffic", source);
        _shuttles.SetKiasMayday(grid, true);
        _safety.Publish(grid, message, true);
    }

    private void OnAvailability(ref KiasAvailabilityChangedEvent args)
    {
        if (!args.Active)
        {
            _emergencies.Remove(args.Grid);
            ClearAlarmSources(args.Grid);
            _shuttles.SetKiasMayday(args.Grid, false);
            if (!TryComp<KiasGridComponent>(args.Grid, out var runtime))
                return;
            foreach (var uid in runtime.Devices)
            {
                if (!TryComp<KiasProtocolComponent>(uid, out var protocols))
                    continue;
                protocols.Cooldowns.Clear();
                protocols.EventCooldowns.Clear();
                protocols.RecentDamage = 0;
                protocols.DamageWindow = TimeSpan.Zero;
                protocols.Alert = KiasAlert.Normal;
                protocols.MaydayReason = string.Empty;
                protocols.CrewUnavailableSince = TimeSpan.Zero;
                protocols.DamageEvidenceUntil = TimeSpan.Zero;
                protocols.CrewDistressUntil = TimeSpan.Zero;
                protocols.CriticalLatched = false;
                protocols.CriticalSince = protocols.CriticalRecoverySince = TimeSpan.Zero;
                CancelMayday(protocols);
            }
        }
    }

    private void OnControl(Entity<KiasDisplayComponent> ent, ref KiasControlMessage args)
    {
        if (!HasComp<KiasManagementComponent>(ent) || Transform(ent).GridUid is not { } grid || !_kias.IsOnline(ent) || !_kias.CanConfigure(grid, args.Actor)
            || !TryProtocol(grid, out var protocols))
            return;
        if (args.Acknowledge) AcknowledgeCrew(grid);
        else if (args.Reset) ResetAlert(grid);
        else Trigger(grid, args.Quiet ? KiasTrigger.QuietMode : KiasTrigger.Manual);
    }
}
