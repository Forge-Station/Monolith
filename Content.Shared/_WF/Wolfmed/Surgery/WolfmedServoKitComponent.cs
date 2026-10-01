using Content.Shared._Shitmed.Medical.Surgery.Tools;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Surgery;

/// <summary>
/// Wire to replace an actuator's run with. The surgery tool for the servo step, which is the only thing
/// that clears servo damage: welding the panel shut does nothing for what is behind it.
/// </summary>
/// <remarks>
/// Sits on the cable coil, so the tool a mechanic already carries is the one the step wants.
/// </remarks>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedServoKitComponent : Component, ISurgeryToolComponent
{
    public string ToolName => Loc.GetString("wolfmed-surgery-tool-cable-coil");

    public bool? Used { get; set; }

    [DataField]
    public float Speed { get; set; } = 1f;
}
