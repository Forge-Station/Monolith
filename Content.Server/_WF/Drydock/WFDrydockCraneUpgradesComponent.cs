using Content.Shared.Construction.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

/// <summary>
/// Machine part upgrades for a buildable drydock crane. Each part type sets one axis by its tier: manipulators its speed,
/// matter bins its reach, capacitors its efficiency. Speed and reach multiply the working draw.
/// </summary>
/// <remarks>Every list holds one entry per tier, from stock parts up. A part's tier is how many of <see cref="Ratings"/> its average rating meets.</remarks>
[RegisterComponent]
public sealed partial class WFDrydockCraneUpgradesComponent : Component
{
    /// <summary>Part ratings that reach tiers 2, 3 and 4.</summary>
    [DataField]
    public List<float> Ratings = new() { 3f, 4f, 6f };

    /// <summary>Part type that sets the speed tier.</summary>
    [DataField]
    public ProtoId<MachinePartPrototype> SpeedPart = "Manipulator";

    /// <summary>Part type that sets the reach tier.</summary>
    [DataField]
    public ProtoId<MachinePartPrototype> ReachPart = "MatterBin";

    /// <summary>Part type that sets the efficiency tier.</summary>
    [DataField]
    public ProtoId<MachinePartPrototype> EfficiencyPart = "Capacitor";

    /// <summary>Working draw at stock parts, in watts.</summary>
    [DataField]
    public float WorkingLoad = 6000f;

    /// <summary>Work and motion speed multiplier per speed tier.</summary>
    [DataField]
    public List<float> Speed = new() { 1f };

    /// <summary>Working draw multiplier per speed tier.</summary>
    [DataField]
    public List<float> SpeedLoad = new() { 1f };

    /// <summary>Arm and boom per reach tier.</summary>
    [DataField]
    public List<WFDrydockCraneReach> Reach = new();

    /// <summary>Working draw multiplier per reach tier.</summary>
    [DataField]
    public List<float> ReachLoad = new() { 1f };

    /// <summary>Working draw multiplier per efficiency tier.</summary>
    [DataField]
    public List<float> Efficiency = new() { 1f };

    /// <summary>Speed tier from the installed parts, 1 to 4.</summary>
    [ViewVariables]
    public int SpeedTier = 1;

    /// <summary>Reach tier from the installed parts, 1 to 4.</summary>
    [ViewVariables]
    public int ReachTier = 1;

    /// <summary>Efficiency tier from the installed parts, 1 to 4.</summary>
    [ViewVariables]
    public int EfficiencyTier = 1;
}

/// <summary>A crane's arm length and boom sections at one reach tier.</summary>
[DataDefinition]
public sealed partial class WFDrydockCraneReach
{
    /// <summary>Longest arm, in tiles.</summary>
    [DataField(required: true)]
    public float MaxArm;

    /// <summary>Number of nested boom sections.</summary>
    [DataField(required: true)]
    public int Sections;

    /// <summary>Length of one boom section, in tiles.</summary>
    [DataField(required: true)]
    public int SectionLength;
}
