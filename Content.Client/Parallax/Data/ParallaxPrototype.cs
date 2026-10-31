using Robust.Shared.Prototypes;
using Content.Shared._Goobstation.Parallax; // Forge-Change "Goob - background meteors"

namespace Content.Client.Parallax.Data;

/// <summary>
/// Prototype data for a parallax.
/// </summary>
[Prototype]
public sealed partial class ParallaxPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Parallax layers.
    /// </summary>
    [DataField("layers")]
    public List<ParallaxLayerConfig> Layers { get; private set; } = new();

    /// <summary>
    /// Parallax layers, low-quality.
    /// </summary>
    [DataField("layersLQ")]
    public List<ParallaxLayerConfig> LayersLQ { get; private set; } = new();

    /// <summary>
    /// If low-quality layers don't exist for this parallax and high-quality should be used instead.
    /// </summary>
    [DataField("layersLQUseHQ")]
    public bool LayersLQUseHQ { get; private set; } = true;

    // Forge-Change "Goob - background meteors"
    /// <summary>
    /// Optional cosmetic meteor/asteroid field drawn over this parallax.
    /// </summary>
    [DataField]
    public MeteorParallaxConfig? Meteors { get; private set; }
}
