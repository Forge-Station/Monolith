using Content.Server.Administration.Logs;
using Content.Server._WF.NpcVehicleCrew;
using Content.Shared._WF.MilitaryVehicles;
using Content.Shared._WF.NpcVehicleCrew;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Robust.Server.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.MilitaryVehicles;

/// <summary>
/// Military repair kits: make any vehicle whole, and claim an NPC crew's vehicle once no living crewman is aboard,
/// re-keyed to the user and marked salvaged.
/// </summary>
public sealed class WFVehicleCaptureSystem : EntitySystem
{
    /// <summary>The key handed over with a claimed vehicle.</summary>
    private static readonly EntProtoId VehicleKey = "RMCVehicleKey";

    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private HardpointSystem _hardpoints = default!;
    [Dependency] private VehicleLockSystem _locks = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WFVehicleSalvageSystem _salvage = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFMilitaryRepairKitComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<WFMilitaryRepairKitComponent, WFMilitaryRepairKitDoAfterEvent>(OnDoAfter);
    }

    private void OnAfterInteract(Entity<WFMilitaryRepairKitComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } vehicle || !HasComp<RMCVehicleComponent>(vehicle))
            return;

        args.Handled = true;
        if (!CanUse(vehicle, out var reason))
        {
            _popup.PopupEntity(Loc.GetString(reason, ("vehicle", vehicle)), vehicle, args.User);
            return;
        }

        if (HasComp<WFVehicleCrewComponent>(vehicle))
            _popup.PopupEntity(Loc.GetString("wf-military-repair-kit-claiming", ("vehicle", vehicle)), vehicle, args.User);

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.Delay, new WFMilitaryRepairKitDoAfterEvent(), ent, target: vehicle, used: ent)
        {
            BreakOnMove = true,
            NeedHand = true,
        });
    }

    private void OnDoAfter(Entity<WFMilitaryRepairKitComponent> ent, ref WFMilitaryRepairKitDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } vehicle)
            return;

        args.Handled = true;
        Apply(ent, vehicle, args.User);
    }

    /// <summary>Whether the kit can work on this vehicle, and the line saying why not.</summary>
    public bool CanUse(EntityUid vehicle, out string reason)
    {
        reason = string.Empty;
        if (TryComp<WFVehicleCrewComponent>(vehicle, out var crew))
        {
            if (!CrewAboard(vehicle, crew))
                return true;

            reason = "wf-military-repair-kit-crewed";
            return false;
        }

        if (_hardpoints.GetMissingIntegrity(vehicle, out _) <= 0f)
        {
            reason = "wf-military-repair-kit-intact";
            return false;
        }

        return true;
    }

    /// <summary>Whether a living crewman is still aboard: the crew hasn't bailed, or one never got out.</summary>
    public bool CrewAboard(EntityUid vehicle, WFVehicleCrewComponent crew)
    {
        var bailed = crew.Bailed;
        HashSet<EntityUid>? passengers = null;
        if (TryComp<VehicleInteriorComponent>(vehicle, out var interior))
            passengers = interior.Passengers;

        foreach (var member in Members(crew))
        {
            if (TerminatingOrDeleted(member) || _mobState.IsDead(member))
                continue;

            if (!bailed || passengers != null && passengers.Contains(member))
                return true;
        }

        return false;
    }

    private static IEnumerable<EntityUid> Members(WFVehicleCrewComponent crew)
    {
        var driver = crew.Driver;
        var gunner = crew.Gunner;
        var troopers = crew.Troopers;
        if (driver is { } d)
            yield return d;
        if (gunner is { } g)
            yield return g;
        foreach (var trooper in troopers)
            yield return trooper;
    }

    /// <summary>
    /// Makes the vehicle whole and spends the kit; a crew's vehicle is claimed as well. False, with the kit kept, when
    /// it can't.
    /// </summary>
    public bool Apply(Entity<WFMilitaryRepairKitComponent> ent, EntityUid vehicle, EntityUid? user)
    {
        if (!CanUse(vehicle, out var reason))
        {
            if (user != null)
                _popup.PopupEntity(Loc.GetString(reason, ("vehicle", vehicle)), vehicle, user.Value);
            return false;
        }

        _hardpoints.RestoreIntegrity(vehicle);
        if (ent.Comp.Sound != null)
            _audio.PlayPvs(ent.Comp.Sound, vehicle);

        if (HasComp<WFVehicleCrewComponent>(vehicle))
        {
            Claim(vehicle, user);
            if (user != null)
                _popup.PopupEntity(Loc.GetString("wf-military-repair-kit-claimed", ("vehicle", vehicle)), vehicle, user.Value);
        }
        else if (user != null)
        {
            _popup.PopupEntity(Loc.GetString("wf-military-repair-kit-done", ("vehicle", vehicle)), vehicle, user.Value);
        }

        QueueDel(ent);
        return true;
    }

    /// <summary>Takes a vehicle off its crew: no brain or autopilot, no faction, a new lock keyed to the user, salvage.</summary>
    private void Claim(EntityUid vehicle, EntityUid? user)
    {
        RemComp<WFVehicleCrewComponent>(vehicle);
        RemComp<WFVehicleAutopilotComponent>(vehicle);
        if (TryComp<NpcFactionMemberComponent>(vehicle, out var faction))
            _factions.ClearFactions((vehicle, faction));

        var keyId = _locks.Rekey(vehicle);
        var key = Spawn(VehicleKey, Transform(user ?? vehicle).Coordinates);
        _locks.BindKey((key, EnsureComp<VehicleKeyComponent>(key)), keyId, vehicle);
        if (user != null)
            _hands.PickupOrDrop(user.Value, key);

        _salvage.MarkSalvaged(vehicle);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(user):actor} claimed NPC vehicle {ToPrettyString(vehicle)}");
    }
}
