using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Genetics.Components;

[RegisterComponent]
public sealed partial class GeneticsConsoleComponent : Component
{
    public const string ScannerPort = "GeneticsScannerSender";

    [DataField]
    public float MaxDistance = 4f;

    [ViewVariables]
    public EntityUid? Scanner;

    [ViewVariables]
    public bool ScannerInRange = true;

    [DataField]
    public EntProtoId InjectorPrototype = "DnaInjector";
}
