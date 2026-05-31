using Content.Shared.Atmos;

namespace Content.Shared._Funkystation.Atmos.Components;

[RegisterComponent]
public sealed partial class CrystallizerComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    public string? SelectedRecipeId { get; set; }

    [ViewVariables(VVAccess.ReadWrite)]
    public float GasInput { get; set; }

    [DataField]
    public string InletName = "inlet";

    [DataField]
    public string RegulatorName = "regulator";

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public GasMixture CrystallizerGasMixture { get; set; } = new();

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public float ProgressBar { get; set; }

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public float QualityLoss { get; set; }

    [ViewVariables]
    [DataField]
    public float TotalRecipeMoles { get; set; }
}
