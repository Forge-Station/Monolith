using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._HL.Silicons.Synths.Battery;
using Content.Server.PowerCell;
using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Synthetics;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Hud;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Power.Components;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Synthetics;

public sealed partial class WFChassisPanelSystem
{
    /// <summary>The cell's organ slot id; an IPC chassis carries its cell in an item slot and is listed under it too.</summary>
    public const string BatterySlot = "battery";

    /// <summary>Every part slot the panel lists, in its order, with the readout part it names.</summary>
    private static readonly (string Slot, TargetBodyPart Target)[] PartSlots =
    {
        ("head", TargetBodyPart.Head),
        ("torso", TargetBodyPart.Torso),
        ("left arm", TargetBodyPart.LeftArm),
        ("right arm", TargetBodyPart.RightArm),
        ("left hand", TargetBodyPart.LeftHand),
        ("right hand", TargetBodyPart.RightHand),
        ("left leg", TargetBodyPart.LeftLeg),
        ("right leg", TargetBodyPart.RightLeg),
        ("left foot", TargetBodyPart.LeftFoot),
        ("right foot", TargetBodyPart.RightFoot),
    };

    /// <summary>Organ slots whose loss stops the chassis or ends the mind.</summary>
    private static readonly HashSet<string> VitalSlots = new() { "brain", "posbrain", "pump", "heart" };

    [Dependency] private BodyPartFunctionalitySystem _functionality = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private PowerCellSystem _powerCell = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SynthBatterySystem _synthBattery = default!;
    [Dependency] private WFRadCollectorSystem _radCollector = default!;

    /// <summary>Everything the panel shows for this body, read fresh.</summary>
    public WFChassisPanelState BuildState(EntityUid body)
    {
        var state = new WFChassisPanelState();
        var chassis = CompOrNull<WFChassisComponent>(body);
        var hud = CompOrNull<WolfmedSyntheticHudComponent>(body);
        var softCap = CompOrNull<PainComponent>(body)?.SoftPainCap ?? FixedPoint2.Zero;

        var attached = new Dictionary<TargetBodyPart, Entity<BodyPartComponent>>();
        foreach (var (part, bodyPart) in _body.GetBodyChildren(body))
        {
            if (_body.GetTargetBodyPart(bodyPart) is { } target)
                attached.TryAdd(target, (part, bodyPart));
        }

        var slots = TryComp(body, out BodyComponent? bodyComp) &&
                    _prototypes.TryIndex(bodyComp.Prototype, out var prototype)
            ? prototype.Slots.Keys.ToHashSet()
            : null;

        foreach (var (slot, target) in PartSlots)
        {
            if (slots != null ? !slots.Contains(slot) : !attached.ContainsKey(target))
                continue;

            // An empty slot is null, not a default struct: GetValueOrDefault would hand PartEntry a part with no component.
            state.Parts.Add(PartEntry(target, attached.TryGetValue(target, out var present) ? present : null, hud, softCap));
        }

        foreach (var (target, part) in attached.OrderBy(pair => Array.FindIndex(PartSlots, s => s.Target == pair.Key)))
        {
            foreach (var slot in part.Comp.Organs.Keys)
                state.Organs.Add(OrganEntry(part, target, slot, chassis));
        }

        if (state.Organs.All(organ => organ.Slot != BatterySlot))
            state.Organs.Add(SlotCellEntry(body, chassis));

        state.BayOpen = chassis?.BayOpen ?? false;
        state.Overclocked = chassis?.Overclocked ?? false;
        state.Shutdown = _shutdown.IsShutDown(body);
        state.Power = hud?.Power ?? (TryGetCell(body, out var cell) ? ChargeFraction(cell.Value.Comp) : -1f);
        state.Fluid = hud?.Fluid ?? 1f;
        state.Sensors = hud?.Sensors ?? 1f;
        state.CoreTemperature = hud?.CoreTemperature ?? -1f;
        state.RadCollector = _radCollector.GetCollector(body) is { } collector && collector.Comp1.Capacity > 0f
            ? Math.Clamp(collector.Comp1.Charge / collector.Comp1.Capacity, 0f, 1f)
            : -1f;
        state.Radiation = _radCollector.GetExposure(body);
        state.Status = state.Shutdown ? "wf-chassis-status-shutdown"
            : HasComp<WFChassisRebootComponent>(body) ? "wf-chassis-status-reboot"
            : HasComp<WFLowPowerComponent>(body) ? "wf-chassis-status-low-power"
            : state.BayOpen ? "wf-chassis-status-bay-open"
            : state.Overclocked ? "wf-chassis-status-overclocked"
            : "wf-chassis-status-nominal";

        return state;
    }

    private WFChassisPartEntry PartEntry(
        TargetBodyPart target,
        Entity<BodyPartComponent>? part,
        WolfmedSyntheticHudComponent? hud,
        FixedPoint2 softCap)
    {
        var entry = new WFChassisPartEntry
        {
            Slot = target,
            Name = WolfmedSyntheticHudLineSystem.PartKey(target),
        };

        if (hud != null)
        {
            foreach (var fault in hud.Faults)
            {
                if (fault.Part == target)
                    entry.Faults.Add(fault.Line);
            }
        }

        if (part is not { } attached)
            return entry;

        entry.Part = GetNetEntity(attached);
        entry.Attached = true;
        // An arm low power has cut is offline, not isolated: the panel offers no restore for it.
        entry.Isolated = _isolation.IsUserIsolated(attached);
        entry.Loose = HasComp<WFLooseLimbComponent>(attached);
        entry.Working = attached.Comp.Enabled && !entry.Isolated &&
                        _functionality.GetState((attached.Owner, (WoundableComponent?) null)) != BodyPartFunctionalityState.Disabled;
        entry.Integrity = _hud.PartIntegrity(attached);
        entry.Pain = softCap > FixedPoint2.Zero && TryComp(attached, out PainComponent? pain)
            ? Math.Clamp(pain.Value.Float() / softCap.Float(), 0f, 1f)
            : 0f;
        entry.CanDetach = WFChassisIsolationSystem.CanIsolate(attached.Comp);
        return entry;
    }

    private WFChassisOrganEntry OrganEntry(
        Entity<BodyPartComponent> part,
        TargetBodyPart holder,
        string slot,
        WFChassisComponent? chassis)
    {
        var entry = new WFChassisOrganEntry
        {
            Slot = slot,
            Holder = holder,
            Vital = VitalSlots.Contains(slot),
        };

        if (!_containers.TryGetContainer(part, SharedBodySystem.GetOrganContainerId(slot), out var container) ||
            container.ContainedEntities.Count == 0)
            return entry;

        FillInstalled(entry, container.ContainedEntities[0], chassis);
        return entry;
    }

    /// <summary>The battery row for a chassis whose cell sits in an item slot rather than an organ slot.</summary>
    private WFChassisOrganEntry SlotCellEntry(EntityUid body, WFChassisComponent? chassis)
    {
        var entry = new WFChassisOrganEntry { Slot = BatterySlot, Holder = TargetBodyPart.Torso };
        if (_powerCell.TryGetBatteryFromSlot(body, out var cell, out _))
            FillInstalled(entry, cell.Value, chassis);

        return entry;
    }

    private void FillInstalled(WFChassisOrganEntry entry, EntityUid installed, WFChassisComponent? chassis)
    {
        entry.Organ = GetNetEntity(installed);
        entry.Name = Name(installed);
        entry.CanDisconnect = chassis?.BayOpen ?? false;

        if (TryComp(installed, out BatteryComponent? battery))
        {
            entry.Health = ChargeFraction(battery);
            entry.Band = "wf-chassis-band-charge";
        }
        else if (TryComp(installed, out WolfmedOrganComponent? organ))
        {
            entry.Health = organ.Fraction;
            entry.Band = organ.Band switch
            {
                WolfmedOrganBand.Failed => "wf-chassis-band-failed",
                WolfmedOrganBand.Impaired => "wf-chassis-band-impaired",
                _ => "wf-chassis-band-normal",
            };
        }
    }

    /// <summary>The chassis's power cell: a synth's battery organ, an IPC's own battery, or the cell in its slot.</summary>
    public bool TryGetCell(EntityUid body, [NotNullWhen(true)] out Entity<BatteryComponent>? cell)
    {
        if (_synthBattery.TryGetBattery(body, out cell))
            return true;

        if (HasComp<SiliconComponent>(body) && TryComp(body, out BatteryComponent? own))
        {
            cell = (body, own);
            return true;
        }

        if (_powerCell.TryGetBatteryFromSlot(body, out var slotted, out var battery))
        {
            cell = (slotted.Value, battery);
            return true;
        }

        cell = null;
        return false;
    }

    private static float ChargeFraction(BatteryComponent battery) =>
        battery.MaxCharge > 0f ? Math.Clamp(battery.CurrentCharge / battery.MaxCharge, 0f, 1f) : 0f;
}
