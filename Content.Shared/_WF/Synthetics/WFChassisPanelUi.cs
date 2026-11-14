using Content.Shared._Shitmed.Targeting;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Synthetics;

/// <summary>The chassis panel: a synthetic body's own view of its parts and components, with the controls for them.</summary>
[Serializable, NetSerializable]
public enum WFChassisPanelUiKey : byte
{
    Key,
}

/// <summary>One body part on the panel. Names are loc keys; the client localizes them.</summary>
[Serializable, NetSerializable]
public sealed class WFChassisPartEntry
{
    /// <summary>The part, or null when the slot is empty (the limb is off).</summary>
    public NetEntity? Part;

    /// <summary>Where it sits, for the label and the ordering.</summary>
    public TargetBodyPart Slot;

    /// <summary>Loc key for the part's name (wolfmed-synthetic-part-*).</summary>
    public string Name = string.Empty;

    public bool Attached;

    /// <summary>Isolated by the panel: no servos, no sensors.</summary>
    public bool Isolated;

    /// <summary>Working at all (attached, enabled, not isolated, not disabled by damage).</summary>
    public bool Working;

    /// <summary>Put back on by hand and still loose.</summary>
    public bool Loose;

    /// <summary>Structural integrity, 0 to 1.</summary>
    public float Integrity = 1f;

    /// <summary>This part's share of the body's sensor load, 0 to 1 of the soft cap.</summary>
    public float Pain;

    /// <summary>Loc keys of the faults the readout would list for this part (wolfmed-synthetic-line-*).</summary>
    public List<string> Faults = new();

    /// <summary>The panel may detach it (a limb, attached, not the head).</summary>
    public bool CanDetach;
}

/// <summary>One component slot on the panel: an organ, the core or the power cell.</summary>
[Serializable, NetSerializable]
public sealed class WFChassisOrganEntry
{
    /// <summary>The installed component, or null when the slot is empty.</summary>
    public NetEntity? Organ;

    /// <summary>The organ slot id (eyes, posbrain, pump, battery, ...).</summary>
    public string Slot = string.Empty;

    /// <summary>The part that holds the slot.</summary>
    public TargetBodyPart Holder;

    /// <summary>The component's entity name, already a display string.</summary>
    public string Name = string.Empty;

    /// <summary>Health fraction 0 to 1, or -1 when the component has no health (a power cell shows charge here).</summary>
    public float Health = -1f;

    /// <summary>Loc key for the health band (wf-chassis-band-*), or empty.</summary>
    public string Band = string.Empty;

    /// <summary>Pulling it stops the chassis (core, pump) or kills the mind (core).</summary>
    public bool Vital;

    /// <summary>It can be disconnected from the panel right now (the bay is open and it is present).</summary>
    public bool CanDisconnect;
}

/// <summary>Everything the panel shows. Rebuilt by the server while the panel is open.</summary>
[Serializable, NetSerializable]
public sealed class WFChassisPanelState : BoundUserInterfaceState
{
    public List<WFChassisPartEntry> Parts = new();
    public List<WFChassisOrganEntry> Organs = new();

    public bool BayOpen;
    public bool Overclocked;

    /// <summary>Charge 0 to 1, or -1 with no cell.</summary>
    public float Power = -1f;

    /// <summary>Hydraulic fluid 0 to 1.</summary>
    public float Fluid = 1f;

    /// <summary>Damage-sensor headroom 0 to 1, as the HUD's SENSOR gauge.</summary>
    public float Sensors = 1f;

    /// <summary>Core temperature in kelvin, or -1.</summary>
    public float CoreTemperature = -1f;

    /// <summary>The radiation collector's charge, 0 to 1, or -1 with no working collector.</summary>
    public float RadCollector = -1f;

    /// <summary>Rads a second reaching the chassis right now.</summary>
    public float Radiation;

    /// <summary>The chassis is shut down (no power or no pump).</summary>
    public bool Shutdown;

    /// <summary>Loc key of a one-line status for the top of the panel, or empty.</summary>
    public string Status = string.Empty;
}

/// <summary>Cut a part's servos and sensors, or restore them.</summary>
[Serializable, NetSerializable]
public sealed class WFChassisIsolatePartMessage(NetEntity part, bool isolate) : BoundUserInterfaceMessage
{
    public readonly NetEntity Part = part;
    public readonly bool Isolate = isolate;
}

/// <summary>Release a limb's coupling so it comes off in one piece.</summary>
[Serializable, NetSerializable]
public sealed class WFChassisDetachPartMessage(NetEntity part) : BoundUserInterfaceMessage
{
    public readonly NetEntity Part = part;
}

/// <summary>Open or close the chassis bay.</summary>
[Serializable, NetSerializable]
public sealed class WFChassisToggleBayMessage : BoundUserInterfaceMessage;

/// <summary>Disconnect an installed component; it ends up in the actor's hand or on the floor.</summary>
[Serializable, NetSerializable]
public sealed class WFChassisDisconnectOrganMessage(NetEntity organ) : BoundUserInterfaceMessage
{
    public readonly NetEntity Organ = organ;
}

/// <summary>Reboot: a few seconds down, then the sensor load and any short-circuit lockup are cleared.</summary>
[Serializable, NetSerializable]
public sealed class WFChassisRebootMessage : BoundUserInterfaceMessage;

/// <summary>Overclock the servos, or run them at rating again.</summary>
[Serializable, NetSerializable]
public sealed class WFChassisOverclockMessage(bool on) : BoundUserInterfaceMessage
{
    public readonly bool On = on;
}
