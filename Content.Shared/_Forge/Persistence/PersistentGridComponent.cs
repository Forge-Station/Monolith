namespace Content.Shared._Forge.Persistence;

/// <summary>
/// Marks an individual grid for file-backed persistence between rounds.
/// </summary>
[RegisterComponent]
public sealed partial class PersistentGridComponent : Component
{
    /// <summary>
    /// Stable, filesystem-safe identifier used by the persistence manifest.
    /// </summary>
    [DataField(required: true)]
    public string Id = string.Empty;

    /// <summary>
    /// Logical sector key used to restore grids to the correct map.
    /// </summary>
    [DataField]
    public string MapKey = "default";
}
