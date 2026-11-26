using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Atmos.Components;
using Content.Server.Body.Components;
using Content.Server.NPC.HTN;
using Content.Server.Temperature.Components;
using Content.Shared._Shitmed.Body.Components;
using Content.Shared._WF.Planets;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetNpcGear;

/// <summary>
/// Dresses the NPCs on a planet for its air: a world too hot, too cold or at the wrong pressure for them gets them the
/// suit for it, and air they can't breathe a mask and an oxygen tank on internals, kept topped up. Crew bring their own
/// kit (<see cref="WFCrewEvaSystem"/>) and are left alone.
/// </summary>
public sealed class WFPlanetNpcGearSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedGasTankSystem _tanks = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>How close to a body's heat or cold damage threshold the air may get before he's dressed for it.</summary>
    private const float TemperatureMargin = 20f;

    /// <summary>The pressure, in kPa, a worn tank is kept topped up to.</summary>
    private const float TankPressure = 1000f;

    private static readonly ProtoId<StartingGearPrototype> PressureGear = "WFPlanetNpcGearPressure";
    private static readonly ProtoId<StartingGearPrototype> HotGear = "WFPlanetNpcGearHot";
    private static readonly ProtoId<StartingGearPrototype> ColdGear = "WFPlanetNpcGearCold";
    private static readonly ProtoId<StartingGearPrototype> BreathGear = "WFPlanetNpcGearBreath";
    private static readonly EntProtoId Tank = "EmergencyOxygenTankFilled";

    /// <summary>Where the tank goes, first free slot first.</summary>
    private static readonly string[] TankSlots = { "suitstorage", "belt", "pocket2", "pocket1" };

    private TimeSpan _next;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _next)
            return;
        _next = now + Interval;

        var query = EntityQueryEnumerator<HTNComponent, InventoryComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (xform.MapUid is not { } map || HasComp<ActorComponent>(uid) || HasComp<WFCrewComponent>(uid))
                continue;

            if (TryComp(uid, out WFPlanetNpcGearComponent? gear) && gear.Map == map)
            {
                KeepBreathing((uid, gear));
                continue;
            }

            if (!HasComp<WFPlanetLayerComponent>(map) || !TryComp(map, out MapAtmosphereComponent? atmosphere)
                || atmosphere.Space || !_mobs.IsAlive(uid))
                continue;
            Dress(uid, map, atmosphere.Mixture);
        }
    }

    /// <summary>Puts on whatever this world's air calls for, and marks him dressed for it.</summary>
    public void Dress(EntityUid uid, EntityUid map, GasMixture air)
    {
        var gear = EnsureComp<WFPlanetNpcGearComponent>(uid);
        gear.Map = map;

        var pressure = air.Pressure;
        if (!HasComp<PressureImmunityComponent>(uid)
            && (pressure < Atmospherics.WarningLowPressure || pressure > Atmospherics.WarningHighPressure))
            Equip(uid, PressureGear);
        else if (TryComp(uid, out TemperatureComponent? temperature))
        {
            if (air.Temperature > temperature.HeatDamageThreshold - TemperatureMargin)
                Equip(uid, HotGear);
            else if (air.Temperature < temperature.ColdDamageThreshold + TemperatureMargin)
                Equip(uid, ColdGear);
        }

        if (!HasComp<RespiratorComponent>(uid) || HasComp<BreathingImmunityComponent>(uid)
            || WFCrewPlannerSystem.IsBreathable(air, pressure))
            return;

        // Whatever he wore over his face goes, for a mask a tank can feed.
        if (!_inventory.TryGetSlotEntity(uid, "mask", out var mask) || !HasComp<BreathToolComponent>(mask))
            Equip(uid, BreathGear);
        gear.Tank = GiveTank(uid);
        KeepBreathing((uid, gear));
    }

    /// <summary>Puts on a starting gear's pieces, replacing what he had in those slots.</summary>
    private void Equip(EntityUid uid, ProtoId<StartingGearPrototype> gearId)
    {
        if (!_prototypes.TryIndex(gearId, out var gear))
            return;
        var coordinates = Transform(uid).Coordinates;
        foreach (var (slot, proto) in gear.Equipment)
        {
            if (_inventory.TryGetSlotEntity(uid, slot, out var old))
            {
                if (!_inventory.TryUnequip(uid, slot, silent: true, force: true))
                    continue;
                QueueDel(old);
            }
            var item = Spawn(proto, coordinates);
            if (!_inventory.TryEquip(uid, item, slot, silent: true, force: true))
                QueueDel(item);
        }
    }

    /// <summary>An oxygen tank in the first free slot that takes one.</summary>
    private EntityUid? GiveTank(EntityUid uid)
    {
        var coordinates = Transform(uid).Coordinates;
        foreach (var slot in TankSlots)
        {
            if (_inventory.TryGetSlotEntity(uid, slot, out _))
                continue;
            var tank = Spawn(Tank, coordinates);
            if (_inventory.TryEquip(uid, tank, slot, silent: true, force: true))
                return tank;
            QueueDel(tank);
        }
        return null;
    }

    /// <summary>Keeps his tank on internals and topped up while he still has it on him.</summary>
    private void KeepBreathing(Entity<WFPlanetNpcGearComponent> ent)
    {
        if (ent.Comp.Tank is not { } tank || TerminatingOrDeleted(tank) || !TryComp(tank, out GasTankComponent? gas)
            || !_inventory.TryGetContainingSlot(tank, out _) || Transform(tank).ParentUid != ent.Owner)
        {
            ent.Comp.Tank = null;
            return;
        }

        var air = gas.Air;
        if (air.Volume > 0f && air.Pressure < TankPressure * 0.5f)
        {
            if (air.TotalMoles <= 0f)
                air.Temperature = Atmospherics.T20C;
            air.AdjustMoles(Gas.Oxygen, (TankPressure - air.Pressure) * air.Volume / (Atmospherics.R * air.Temperature));
        }

        if (!gas.IsConnected && _mobs.IsAlive(ent))
            _tanks.ConnectToInternals((tank, gas), ent);
    }
}
