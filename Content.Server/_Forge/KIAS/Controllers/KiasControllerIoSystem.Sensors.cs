using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Atmos.Monitor;

namespace Content.Server._Forge.KIAS.Controllers;

public sealed partial class KiasControllerIoSystem
{
    private void InitializeSensors()
    {
        SubscribeLocalEvent<KiasHullDamageEvent>(OnDamage);
        SubscribeLocalEvent<KiasHullImpactEvent>(OnImpact);
        SubscribeLocalEvent<KiasGridCollisionEvent>(OnCollision);
        SubscribeLocalEvent<KiasPowerDeficitEvent>(OnDeficit);
        SubscribeLocalEvent<KiasAtmosStateChangedEvent>(OnAtmos);
        SubscribeLocalEvent<KiasFireDetectedEvent>(OnFire);
        SubscribeLocalEvent<KiasCrewDistressEvent>(OnDistress);
        SubscribeLocalEvent<KiasAnomalyGrowthEvent>(OnGrowth);
        SubscribeLocalEvent<KiasAutopilotArrivedEvent>(OnArrival);
    }
    private void OnDamage(ref KiasHullDamageEvent args)
    {
        if (args.Damage <= 0 || !_kias.HasRole(args.Grid, KiasDeviceRole.Defence)) return;
        foreach (var device in Devices(args.Grid, "IntegrityMonitor"))
        {
            Emit(device, "IntegrityMonitor", "Structure", KiasGraphValue.Reference(args.Structure));
            Emit(device, "IntegrityMonitor", "Amount", KiasGraphValue.Numeric(args.Damage));
            Emit(device, "IntegrityMonitor", "Damage", KiasGraphValue.Pulse);
        }
    }
    private void OnImpact(ref KiasHullImpactEvent args)
    {
        if (!_kias.HasRole(args.Grid, KiasDeviceRole.Defence) || TerminatingOrDeleted(args.Structure)) return;
        foreach (var device in Devices(args.Grid, "HullSensor"))
        {
            var range = Math.Clamp(Comp<KiasHullSensorComponent>(device).Range, 50, 100);
            if (System.Numerics.Vector2.DistanceSquared(Transform(device).LocalPosition, Transform(args.Structure).LocalPosition) > range * range) continue;
            Emit(device, "HullSensor", "Structure", KiasGraphValue.Reference(args.Structure));
            Emit(device, "HullSensor", "Impact", KiasGraphValue.Pulse);
        }
    }
    private void OnCollision(ref KiasGridCollisionEvent args)
    {
        if (!_kias.HasRole(args.Grid, KiasDeviceRole.Defence)) return;
        foreach (var device in Devices(args.Grid, "CollisionMonitor"))
        {
            if (args.RelativeSpeed < Math.Max(.1f, Comp<KiasCollisionMonitorComponent>(device).MinimumSpeed)) continue;
            Emit(device, "CollisionMonitor", "OtherGrid", KiasGraphValue.Reference(args.OtherGrid));
            Emit(device, "CollisionMonitor", "RelativeSpeed", KiasGraphValue.Numeric(args.RelativeSpeed));
            Emit(device, "CollisionMonitor", "Collision", KiasGraphValue.Pulse);
        }
    }
    private void OnDeficit(ref KiasPowerDeficitEvent args)
    {
        foreach (var device in Devices(args.Grid, "PowerMonitor"))
        {
            Emit(device, "PowerMonitor", "Supply", KiasGraphValue.Numeric(args.Supply));
            Emit(device, "PowerMonitor", "Consumption", KiasGraphValue.Numeric(args.Consumption));
            Emit(device, "PowerMonitor", "Difference", KiasGraphValue.Numeric(args.Consumption - args.Supply));
            Emit(device, "PowerMonitor", "Channel", KiasGraphValue.Enumeration((int) args.Channel));
            Emit(device, "PowerMonitor", "Deficit", KiasGraphValue.Pulse);
        }
    }
    private void OnAtmos(ref KiasAtmosStateChangedEvent args)
    {
        if (args.State is not (AtmosAlarmType.Danger or AtmosAlarmType.Normal)) return;
        foreach (var device in Devices(args.Grid, "AtmosSafety"))
        {
            Emit(device, "AtmosSafety", "Source", KiasGraphValue.Reference(args.Source));
            Emit(device, "AtmosSafety", args.State == AtmosAlarmType.Danger ? "Danger" : "Clear", KiasGraphValue.Pulse);
        }
    }
    private void OnFire(ref KiasFireDetectedEvent args)
    {
        foreach (var device in Devices(args.Grid, "AtmosSafety"))
        {
            Emit(device, "AtmosSafety", "Source", KiasGraphValue.Reference(args.Source));
            Emit(device, "AtmosSafety", "Fire", KiasGraphValue.Pulse);
        }
    }
    private void OnDistress(ref KiasCrewDistressEvent args)
    {
        var crew = EntityManager.System<KiasCrewSystem>();
        foreach (var scanner in crew.ScannersCovering(args.Grid, args.Person, KiasScannerModules.Biometric))
        {
            Emit(scanner, "RoomScanner", "Person", KiasGraphValue.Reference(args.Person));
            Emit(scanner, "RoomScanner", args.Dead ? "PersonDead" : "PersonCritical", KiasGraphValue.Pulse);
        }
        foreach (var device in Devices(args.Grid, "CrewMonitor"))
        {
            Emit(device, "CrewMonitor", "Person", KiasGraphValue.Reference(args.Person));
            Emit(device, "CrewMonitor", "Unavailable", KiasGraphValue.Boolean(crew.CrewUnavailable(args.Grid)));
            Emit(device, "CrewMonitor", args.Dead ? "CrewDead" : "CrewCritical", KiasGraphValue.Pulse);
        }
    }
    private void OnGrowth(ref KiasAnomalyGrowthEvent args)
    {
        foreach (var scanner in EntityManager.System<KiasCrewSystem>().ScannersCovering(args.Grid, args.Source, KiasScannerModules.Spectral))
            Emit(scanner, "RoomScanner", "AnomalyGrowth", KiasGraphValue.Pulse);
    }
    private void OnArrival(ref KiasAutopilotArrivedEvent args)
    {
        foreach (var device in Devices(args.Grid, "NavigationComms")) Emit(device, "NavigationComms", "Arrival", KiasGraphValue.Pulse);
    }
}
