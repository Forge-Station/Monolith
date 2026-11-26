using Robust.Shared.Prototypes;

namespace Content.Shared._WF.AsteroidMining;

/// <summary>
/// Which tile asteroids worldgen swaps for single-rock clusters. A debris ID is its kind's prefix followed by a size
/// suffix; the cluster gets the size's rock count as its mass budget, so a swap keeps the ore a zone yields.
/// </summary>
[Prototype("wfAsteroidReplacement")]
public sealed partial class WFAsteroidReplacementPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    /// <summary>Debris ID prefix, such as NFAsteroidBasaltDebris, to the cluster that replaces it.</summary>
    [DataField(required: true)] public Dictionary<string, WFAsteroidReplacementKind> Kinds = new();

    /// <summary>Debris ID suffix, such as Massive, to the number of rocks that size of tile asteroid holds.</summary>
    [DataField(required: true)] public Dictionary<string, float> Sizes = new();
}

[DataDefinition]
public sealed partial class WFAsteroidReplacementKind
{
    [DataField(required: true)] public EntProtoId Cluster;

    /// <summary>
    /// Debris ID prefix of an ore-free tile asteroid, such as WFAsteroidBarrenDebris, that the debris becomes instead at
    /// <see cref="BarrenChance"/>, for the sizes it has. It keeps gas deposits and anomalies for kinds that carry them.
    /// </summary>
    [DataField] public string? Barren;
    [DataField] public float BarrenChance;
}
