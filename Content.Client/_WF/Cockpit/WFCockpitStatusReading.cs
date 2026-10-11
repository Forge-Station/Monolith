using Content.Shared._NF.Shuttles.Events;
using Content.Shared._WF.ShipShields;
using Content.Shared._WF.Shuttles;
using Content.Shared.Shuttles.Systems;

namespace Content.Client._WF.Cockpit;

/// <summary>The severity and localized explanation of a live cockpit status lamp.</summary>
public readonly record struct WFCockpitStatusReading(WFCockpitLampState State, string Detail)
{
    /// <summary>Reports confirmed steering independently of destination selection.</summary>
    public static WFCockpitStatusReading Autopilot(bool? active) => active switch
    {
        true => new(WFCockpitLampState.Active, "wf-cockpit-status-autopilot-active"),
        false => new(WFCockpitLampState.Off, "wf-cockpit-status-autopilot-off"),
        _ => new(WFCockpitLampState.Unavailable, "wf-cockpit-status-autopilot-unavailable"),
    };

    /// <summary>Maps the actual FTL phase without treating unavailable telemetry, or a ship without a powered drive, as readiness.</summary>
    public static WFCockpitStatusReading Ftl(FTLState state, WFFtlDriveStatus drive) => state switch
    {
        FTLState.Available when drive == WFFtlDriveStatus.Missing => new(WFCockpitLampState.Fault, "shuttle-console-no-powered-ftl-drive"),
        FTLState.Available when drive == WFFtlDriveStatus.Unpowered => new(WFCockpitLampState.Fault, "shuttle-console-ftl-drive-unpowered"),
        FTLState.Available => new(WFCockpitLampState.Good, "wf-cockpit-status-ftl-ready"),
        FTLState.Starting => new(WFCockpitLampState.Caution, "wf-cockpit-status-ftl-starting"),
        FTLState.Travelling => new(WFCockpitLampState.Active, "wf-cockpit-status-ftl-travelling"),
        FTLState.Arriving => new(WFCockpitLampState.Caution, "wf-cockpit-status-ftl-arriving"),
        FTLState.Cooldown => new(WFCockpitLampState.Caution, "wf-cockpit-status-ftl-cooldown"),
        _ => new(WFCockpitLampState.Unavailable, "wf-cockpit-status-ftl-unavailable"),
    };

    /// <summary>Lights only the selected stabilization mode; station and absent data stay unavailable.</summary>
    public static WFCockpitStatusReading Dampening(InertiaDampeningMode? mode, bool parking)
    {
        if (mode is null or InertiaDampeningMode.Query or InertiaDampeningMode.Station)
            return new(WFCockpitLampState.Unavailable, "wf-cockpit-status-stabilizer-unavailable");
        if (parking)
            return mode == InertiaDampeningMode.Anchor
                ? new(WFCockpitLampState.Good, "wf-cockpit-status-park-on")
                : new(WFCockpitLampState.Off, "wf-cockpit-status-park-off");
        return mode == InertiaDampeningMode.Dampen
            ? new(WFCockpitLampState.Good, "wf-cockpit-status-damp-on")
            : new(WFCockpitLampState.Off, "wf-cockpit-status-damp-off");
    }

    /// <summary>Distinguishes an attached docking port from a disconnected ship or missing telemetry.</summary>
    public static WFCockpitStatusReading Docked(bool? connected) => connected switch
    {
        true => new(WFCockpitLampState.Good, "wf-cockpit-status-docked"),
        false => new(WFCockpitLampState.Off, "wf-cockpit-status-undocked"),
        _ => new(WFCockpitLampState.Unavailable, "wf-cockpit-status-dock-unavailable"),
    };

    /// <summary>Reports the field that actually exists, independently of the requested deployment switch.</summary>
    public static WFCockpitStatusReading Shield(WFShipShieldShuntState? state) => state switch
    {
        null or { Available: false } => new(WFCockpitLampState.Unavailable, "wf-cockpit-status-shield-unavailable"),
        { Active: true } => new(WFCockpitLampState.Good, "wf-cockpit-status-shield-online"),
        { Enabled: false } => new(WFCockpitLampState.Off, "wf-cockpit-status-shield-lowered"),
        _ => new(WFCockpitLampState.Caution, "wf-cockpit-status-shield-offline"),
    };
}

/// <summary>The common visual states of physical and digital cockpit lamps.</summary>
public enum WFCockpitLampState : byte
{
    Unavailable,
    Off,
    Good,
    Active,
    Caution,

    /// <summary>A fault the pilot must fix before the system works, such as a missing or unpowered FTL drive.</summary>
    Fault,
}
