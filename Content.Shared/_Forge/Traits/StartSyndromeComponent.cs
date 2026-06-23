using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Traits;

[RegisterComponent]
public sealed partial class StartSyndromeComponent : Component
{
    /// <summary>
    /// Syndrome granted on spawn.
    /// </summary>
    [DataField(required: true)]
    public string Syndrome = string.Empty;

    /// <summary>
    /// Initial disease severity.
    /// </summary>
    [DataField]
    public float Severity = 10f;
}
