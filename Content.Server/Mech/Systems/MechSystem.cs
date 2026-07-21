using System.Linq;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Server.Mech.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Emp; // Monolith
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Mech;
using Content.Shared.Mech.Components;
using Content.Shared.Mech.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components; // Frontier
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC.Components; // Frontier
using Content.Shared.Popups;
using Content.Shared.Power.Components;
using Content.Shared.PowerCell.Components; // Frontier
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Tools;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Content.Shared.Vehicle.Components;
using Content.Shared.Verbs;
using Content.Shared.Wires;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.Mech.Systems;

/// <inheritdoc/>
public sealed partial class MechSystem : SharedMechSystem
{
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private ContainerSystem _container = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedToolSystem _toolSystem = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;

    private static readonly ProtoId<ToolQualityPrototype> PryingQuality = "Prying";

    [SubscribeLocalEvent]
    private void OnMechCanMoveEvent(Entity<MechComponent> ent, ref VehicleCanRunEvent args)
    {
        if (ent.Comp.Broken || ent.Comp.Integrity <= 0 || ent.Comp.Energy <= 0)
            args = args with { CanRun = false };
    }

    [SubscribeLocalEvent]
    private void OnMechRefreshMovementSpeed(Entity<MechComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.CriticalPowerState)
            args.ModifySpeed(ent.Comp.CriticalPowerStateSpeedPenalty);
    }

    [SubscribeLocalEvent]
    private void OnInteractUsing(EntityUid uid, MechComponent component, InteractUsingEvent args)
    {
        if (TryComp<WiresPanelComponent>(uid, out var panel) && !panel.Open)
            return;

        if (component.BatterySlot.ContainedEntity == null && TryComp<BatteryComponent>(args.Used, out var battery))
        {
            InsertBattery(uid, args.Used, component, battery);
            return;
        }

        if (_toolSystem.HasQuality(args.Used, PryingQuality) && component.BatterySlot.ContainedEntity != null)
        {
            var doAfterEventArgs = new DoAfterArgs(EntityManager, args.User, component.BatteryRemovalDelay,
                new RemoveBatteryEvent(), uid, target: uid, used: args.Used)
            {
                BreakOnMove = true,
                MultiplyDelay = false, // Goobstation
            };

            _doAfter.TryStartDoAfter(doAfterEventArgs);
        }
    }

    [SubscribeLocalEvent]
    private void OnPowerCellChanged(Entity<MechComponent> ent, ref PowerCellChangedEvent args)
    {
        if (!TryComp<BatteryComponent>(ent.Comp.BatterySlot.ContainedEntity, out var battery))
            return;

        var criticalState = battery.CurrentCharge / battery.MaxCharge <= 0.05f;
        if (criticalState == ent.Comp.CriticalPowerState)
            return;

        ent.Comp.CriticalPowerState = criticalState;
        Dirty(ent);
        _movementSpeed.RefreshMovementSpeedModifiers(ent);
    }

    [SubscribeLocalEvent]
    private void OnInsertBattery(EntityUid uid, MechComponent component, EntInsertedIntoContainerMessage args)
    {
        if (args.Container != component.BatterySlot || !TryComp<BatteryComponent>(args.Entity, out var battery))
            return;

        component.Energy = _battery.GetCharge((args.Entity, battery));
        component.MaxEnergy = battery.MaxCharge;
        component.CriticalPowerState = battery.CurrentCharge / battery.MaxCharge <= 0.05f; // Mono
        Dirty(uid, component);
        Vehicle.RefreshCanRun(uid);
        _movementSpeed.RefreshMovementSpeedModifiers(uid); // Mono
    }

    [SubscribeLocalEvent]
    private void OnRemoveBattery(EntityUid uid, MechComponent component, RemoveBatteryEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        RemoveBattery(uid, component);
        _movementSpeed.RefreshMovementSpeedModifiers(uid); // Mono
        args.Handled = true;
    }

    [SubscribeLocalEvent]
    private void OnMapInit(EntityUid uid, MechComponent component, MapInitEvent args)
    {
        var xform = Transform(uid);
        foreach (var equipment in component.StartingEquipment)
        {
            var ent = Spawn(equipment, xform.Coordinates);
            InsertEquipment(uid, ent, component);
        }

        component.Integrity = component.MaxIntegrity;
        component.Energy = component.MaxEnergy;

        Vehicle.RefreshCanRun(uid);
        Dirty(uid, component);
    }

    [SubscribeLocalEvent]
    private void OnRemoveEquipmentMessage(EntityUid uid, MechComponent component, MechEquipmentRemoveMessage args)
    {
        var equip = GetEntity(args.Equipment);

        if (!Exists(equip) || Deleted(equip))
            return;

        if (!component.EquipmentContainer.ContainedEntities.Contains(equip))
            return;

        RemoveEquipment(uid, equip, component);
    }

    [SubscribeLocalEvent]
    private void OnOpenUi(EntityUid uid, MechComponent component, MechOpenUiEvent args)
    {
        args.Handled = true;
        ToggleMechUi(uid, component);
    }

    [SubscribeLocalEvent]
    private void OnOpenRadar(EntityUid uid, MechComponent component, MechOpenRadarEvent args)
    {
        var pilot = GetEntity(args.Pilot);
        if (!TryComp<ActorComponent>(pilot, out var actor))
            return;

        _ui.TryToggleUi(uid, RadarConsoleUiKey.Key, actor.PlayerSession);
    }

    [SubscribeLocalEvent]
    private void OnToolUseAttempt(Entity<VehicleOperatorComponent> ent, ref ToolUserAttemptUseEvent args)
    {
        if (ent.Comp.Vehicle is { } vehicle && args.Target == vehicle)
            args.Cancelled = true;
    }

    [SubscribeLocalEvent]
    private void OnAlternativeVerb(EntityUid uid, MechComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || component.Broken)
            return;

        if (CanInsert(uid, args.User, component))
        {
            var enterVerb = new AlternativeVerb
            {
                Text = Loc.GetString("mech-verb-enter"),
                Act = () =>
                {
                    var doAfterEventArgs = new DoAfterArgs(EntityManager, args.User, component.EntryDelay, new MechEntryEvent(), uid, target: uid)
                    {
                        BreakOnMove = true,
                    };

                    _doAfter.TryStartDoAfter(doAfterEventArgs);
                }
            };
            var openUiVerb = new AlternativeVerb
            {
                Act = () => ToggleMechUi(uid, component, args.User),
                Text = Loc.GetString("mech-ui-open-verb")
            };
            args.Verbs.Add(enterVerb);
            args.Verbs.Add(openUiVerb);
        }
        else if (Vehicle.HasOperator(uid))
        {
            var operatorUid = Vehicle.GetOperatorOrNull(uid);
            var ejectVerb = new AlternativeVerb
            {
                Text = Loc.GetString("mech-verb-exit"),
                Priority = 1,
                Act = () =>
                {
                    if (args.User == uid || args.User == operatorUid)
                    {
                        TryEject(uid, component);
                        return;
                    }

                    var doAfterEventArgs = new DoAfterArgs(EntityManager, args.User, component.ExitDelay, new MechExitEvent(), uid, target: uid)
                    {
                        BreakOnMove = true,
                    };
                    _popup.PopupEntity(Loc.GetString("mech-eject-pilot-alert", ("item", uid), ("user", args.User)), uid, PopupType.Large);

                    _doAfter.TryStartDoAfter(doAfterEventArgs);
                }
            };
            args.Verbs.Add(ejectVerb);
        }
    }

    [SubscribeLocalEvent]
    private void OnMechEntry(EntityUid uid, MechComponent component, MechEntryEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        if (!Vehicle.CanOperate(uid, args.User))
        {
            _popup.PopupEntity(Loc.GetString("mech-no-enter", ("item", uid)), args.User);
            return;
        }

        // Frontier - Make AI Attack mechs based on user.
        if (TryComp<MobStateComponent>(args.User, out var _))
            EnsureComp<MobStateComponent>(uid);
        if (TryComp<NpcFactionMemberComponent>(args.User, out var faction))
        {
            var factionMech = EnsureComp<NpcFactionMemberComponent>(uid);
            if (faction.Factions != null)
                factionMech.Factions = faction.Factions;
        }

        TryInsert(uid, args.User, component);
        args.Handled = true;
    }

    [SubscribeLocalEvent]
    private void OnMechExit(EntityUid uid, MechComponent component, MechExitEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        if (!TryEject(uid, component))
            return;

        args.Handled = true;
    }

    [SubscribeLocalEvent]
    private void OnDamageChanged(EntityUid uid, MechComponent component, DamageChangedEvent args)
    {
        var integrity = component.MaxIntegrity - _damageable.GetTotalDamage((uid, args.Damageable));
        SetIntegrity(uid, integrity, component);

        if (Vehicle.TryGetOperator(uid, out var operatorEnt) &&
            TryComp<MobStateComponent>(operatorEnt, out var state) &&
            state.CurrentState != MobState.Alive)
        {
            TryEject(uid, component);
        }
    }

    [SubscribeLocalEvent]
    private void OnEmpAttempt(EntityUid uid, MechComponent comp, EmpAttemptEvent args) // Monolith
    {
        if (TryComp<BatteryComponent>(comp.BatterySlot.ContainedEntity, out var battery))
        {
            var maxCharge = battery.MaxCharge;
            var currentCharge = battery.CurrentCharge;
            var chargeDelta = maxCharge / 2;

            if (chargeDelta > currentCharge)
                chargeDelta = currentCharge;

            TryChangeEnergy(uid, -chargeDelta, comp);
        }
    }

    private void ToggleMechUi(EntityUid uid, MechComponent? component = null, EntityUid? user = null)
    {
        if (!Resolve(uid, ref component))
            return;
        user ??= Vehicle.GetOperatorOrNull(uid);
        if (user == null)
            return;

        if (!TryComp<ActorComponent>(user, out var actor))
            return;

        _ui.TryToggleUi(uid, MechUiKey.Key, actor.PlayerSession);
        UpdateUserInterface(uid, component);
    }

    [SubscribeLocalEvent]
    private void RelayGrabberUiMessage(EntityUid uid, MechComponent component, ref MechGrabberEjectMessage args)
    {
        ReceiveEquipmentUiMesssages(component, args);
    }

    [SubscribeLocalEvent]
    private void RelaySoundboardUiMessage(EntityUid uid, MechComponent component, ref MechSoundboardPlayMessage args)
    {
        ReceiveEquipmentUiMesssages(component, args);
    }

    private void ReceiveEquipmentUiMesssages<T>(MechComponent component, T args) where T : MechEquipmentUiMessage
    {
        var ev = new MechEquipmentUiMessageRelayEvent(args);
        var allEquipment = new List<EntityUid>(component.EquipmentContainer.ContainedEntities);
        var argEquip = GetEntity(args.Equipment);

        foreach (var equipment in allEquipment)
        {
            if (argEquip == equipment)
                RaiseLocalEvent(equipment, ev);
        }
    }

    public override void UpdateUserInterface(EntityUid uid, MechComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        base.UpdateUserInterface(uid, component);

        var ev = new MechEquipmentUiStateReadyEvent();
        foreach (var ent in component.EquipmentContainer.ContainedEntities)
        {
            RaiseLocalEvent(ent, ev);
        }

        var state = new MechBoundUiState
        {
            EquipmentStates = ev.States
        };
        _ui.SetUiState(uid, MechUiKey.Key, state);
    }

    public override void BreakMech(EntityUid uid, MechComponent? component = null)
    {
        base.BreakMech(uid, component);

        _ui.CloseUi(uid, MechUiKey.Key);
        Vehicle.RefreshCanRun(uid);
    }

    public override bool TryChangeEnergy(EntityUid uid, FixedPoint2 delta, MechComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return false;

        var battery = component.BatterySlot.ContainedEntity;
        if (battery == null)
            return false;

        if (!TryComp<BatteryComponent>(battery, out var batteryComp))
            return false;

        if (_battery.GetCharge((battery.Value, batteryComp)) + delta.Float() < 0)
            return false;

        if (!base.TryChangeEnergy(uid, delta, component))
            return false;

        _battery.SetCharge((battery.Value, batteryComp), _battery.GetCharge((battery.Value, batteryComp)) + delta.Float());
        var charge = _battery.GetCharge((battery.Value, batteryComp));
        if (charge != component.Energy)
        {
            Log.Debug($"Battery charge was not equal to mech charge. Battery {charge}. Mech {component.Energy}");
            component.Energy = charge;
            Dirty(uid, component);
        }

        Vehicle.RefreshCanRun(uid);
        return true;
    }

    public void InsertBattery(EntityUid uid, EntityUid toInsert, MechComponent? component = null, BatteryComponent? battery = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        if (!Resolve(toInsert, ref battery, false))
            return;

        _container.Insert(toInsert, component.BatterySlot);
        component.Energy = _battery.GetCharge((toInsert, battery));
        component.MaxEnergy = battery.MaxCharge;

        Vehicle.RefreshCanRun(uid);

        Dirty(uid, component);
        UpdateUserInterface(uid, component);
    }

    public void RemoveBattery(EntityUid uid, MechComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        _container.EmptyContainer(component.BatterySlot);
        component.Energy = 0;
        component.MaxEnergy = 0;
        component.CriticalPowerState = true; // Mono

        Vehicle.RefreshCanRun(uid);

        Dirty(uid, component);
        UpdateUserInterface(uid, component);
    }

    #region Atmos Handling
    [SubscribeLocalEvent]
    private void OnInhale(Entity<VehicleOperatorComponent> ent, ref InhaleLocationEvent args)
    {
        if (ent.Comp.Vehicle is not { } vehicle ||
            !TryComp<MechComponent>(vehicle, out var mech) ||
            !TryComp<MechAirComponent>(vehicle, out var mechAir))
        {
            return;
        }

        if (mech.Airtight)
            args.Gas = mechAir.Air;
    }

    [SubscribeLocalEvent]
    private void OnExhale(Entity<VehicleOperatorComponent> ent, ref ExhaleLocationEvent args)
    {
        if (ent.Comp.Vehicle is not { } vehicle ||
            !TryComp<MechComponent>(vehicle, out var mech) ||
            !TryComp<MechAirComponent>(vehicle, out var mechAir))
        {
            return;
        }

        if (mech.Airtight)
            args.Gas = mechAir.Air;
    }

    [SubscribeLocalEvent]
    private void OnExpose(Entity<VehicleOperatorComponent> ent, ref AtmosExposedGetAirEvent args)
    {
        if (args.Handled || ent.Comp.Vehicle is not { } vehicle)
            return;

        if (!TryComp(vehicle, out MechComponent? mech))
            return;

        if (mech.Airtight && TryComp(vehicle, out MechAirComponent? air))
        {
            args.Handled = true;
            args.Gas = air.Air;
            return;
        }

        args.Gas = _atmosphere.GetContainingMixture(vehicle, excite: args.Excite);
        args.Handled = true;
    }

    [SubscribeLocalEvent]
    private void OnGetFilterAir(EntityUid uid, MechAirComponent comp, ref GetFilterAirEvent args)
    {
        if (args.Air != null)
            return;

        if (!TryComp<MechComponent>(uid, out var mech) || !mech.Airtight)
            return;

        args.Air = comp.Air;
    }
    #endregion
}
