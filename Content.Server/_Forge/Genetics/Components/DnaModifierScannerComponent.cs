using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Genetics.Components;

[RegisterComponent]
public sealed partial class DnaModifierScannerComponent : Component
{
    public const string ScannerPort = "GeneticsScannerReceiver";
    public const string ContainerId = "scanner-bodyContainer";
    public const string MutagenSolution = "mutagen";

    public ContainerSlot BodyContainer = default!;

    [ViewVariables]
    public EntityUid? ConnectedConsole;

    [DataField]
    public ProtoId<ReagentPrototype> MutagenReagent = "UnstableMutagen";

    [DataField]
    public FixedPoint2 IrradiateCost = 10;

    /// <summary>
    /// Mutagen spent each time a single codon on the selected strand is pulsed.
    /// </summary>
    [DataField]
    public FixedPoint2 PulseCost = 2;
}
