namespace Content.Shared._Forge.Persistence;

/// <summary>
/// Marks a map entity for file-backed persistence between rounds.
/// </summary>
[RegisterComponent]
public sealed partial class PersistentMapComponent : Component
{
    /// <summary>
    /// Stable, filesystem-safe identifier used by the persistence manifest.
    /// </summary>
    [DataField(required: true)]
    public string Id = string.Empty;
}
