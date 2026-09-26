using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Genetics.Components;

[RegisterComponent]
public sealed partial class GeneticsConsoleComponent : Component
{
    public const string ScannerPort = "GeneticsScannerSender";
    public const string ServerPort = "GeneticsServerSender";

    [DataField]
    public float MaxDistance = 4f;

    [ViewVariables]
    public EntityUid? Scanner;

    /// <summary>
    /// DNA servers linked from this console with a multitool or network configurator.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> Servers = new();

    [ViewVariables]
    public bool ScannerInRange = true;

    [DataField]
    public EntProtoId InjectorPrototype = "DnaInjector";
}
