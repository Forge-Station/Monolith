using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Genetics;

/// <summary>
/// Runtime state of a single gene on a genome.
/// Completion is the SS13-style structural enzyme fill (0-100).
/// </summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class GeneState
{
    [DataField]
    public bool Active;

    [DataField]
    public bool Identified;

    [DataField]
    public int Completion;

    /// <summary>
    /// Component registrations this gene actually added, so removal does not strip traits the mob already had.
    /// </summary>
    [DataField]
    public List<string> AppliedComponents = new();

    [DataField]
    public int SequenceLength = 4;
}
