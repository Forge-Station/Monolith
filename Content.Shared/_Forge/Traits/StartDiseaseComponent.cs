using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Traits;

[RegisterComponent]
public sealed partial class StartDiseaseComponent : Component
{
    /// <summary>
    /// Disease granted on spawn.
    /// </summary>
    [DataField(required: true)]
    public string Disease = string.Empty;

    /// <summary>
    /// Initial disease severity.
    /// </summary>
    [DataField]
    public float Severity = 10f;
}
