using Robust.Shared.Serialization;

namespace Content.Shared._WF.Cockpit;

/// <summary>Updates the autopilot lamp without replacing the current map or flight controls.</summary>
[Serializable, NetSerializable]
public sealed class WFCockpitAutopilotUpdateMessage(bool? active) : BoundUserInterfaceMessage
{
    /// <summary>Null denotes a console without an available autopilot.</summary>
    public bool? Active = active;
}
