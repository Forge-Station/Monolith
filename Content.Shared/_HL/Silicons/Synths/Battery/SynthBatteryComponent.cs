using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._HL.Silicons.Synths.Battery;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(Other = AccessPermissions.ReadWrite)]
public sealed partial class SynthBatteryComponent : Component
{
    /// <summary>
    /// Organ slot searched for the internal battery cell.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string OrganSlot = "battery";

    /// <summary>
    /// Battery prototype inserted automatically if the synth spawns without one.
    /// </summary>
    [DataField]
    public EntProtoId? StartingBattery = "PowerCellMedium";

    /// <summary>
    /// Battery charge drained per second while the synth is alive.
    /// </summary>
    [DataField]
    public float DrawRate = 0.4f;

    /// <summary>
    /// Battery charge gained per unit of nutriment metabolized.
    /// </summary>
    [DataField]
    public float NutrimentChargeMultiplier = 5f;

    [DataField]
    public LocId? BatteryLowText;

    [DataField]
    public LocId? BatteryDeadText;

    [DataField]
    public SoundSpecifier? BatteryLowSound;

    [DataField]
    public SoundSpecifier? BatteryDeadSound;

    /// <summary>
    /// Percentages of battery charge that play a low-power warning when crossed.
    /// </summary>
    [DataField]
    public List<float> WarningPercentages = new() { 20f, 10f };

    [DataField]
    public float UnpoweredWalkSpeedModifier = 0.5f;

    [DataField]
    public float UnpoweredSprintSpeedModifier = 0.5f;

    [DataField, AutoNetworkedField]
    public bool Unpowered;

    [ViewVariables(VVAccess.ReadWrite)]
    public bool StartingBatteryInserted;
}
