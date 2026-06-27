using Content.Shared.Procedural.Distance;

namespace Content.Shared.Procedural.DungeonGenerators;

/// <summary>
/// Like <see cref="Content.Shared.Procedural.DungeonGenerators.NoiseDunGenLayer"/> except with maximum dimensions
/// </summary>
public sealed partial class NoiseDistanceDunGen : IDunGenLayer
{
    [DataField]
    public IDunGenDistance? DistanceConfig;

    [DataField]
    public Vector2i Size;

    [DataField]
    public Vector2i? MinSize;

    [DataField]
    public Vector2i? MaxSize;

    [DataField]
    public bool RandomizeSizeOrientation;

    [DataField(required: true)]
    public List<NoiseDunGenLayer> Layers = new();
}
