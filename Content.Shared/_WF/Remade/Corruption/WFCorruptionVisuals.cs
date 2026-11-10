using Robust.Shared.Serialization;

namespace Content.Shared._WF.Remade.Corruption;

/// <summary>Appearance data for corruption and the growth on walls.</summary>
[Serializable, NetSerializable]
public enum WFCorruptionVisuals : byte
{
    /// <summary>
    /// Floor corruption: which of the eight neighbours join up, as an 8-bit mask (N 1, S 2, E 4, W 8, then NE 16, SE 32,
    /// SW 64, NW 128, a corner counting only when both sides next to it are set). Wall growth: which of N, S, E, W have
    /// corruption beside the wall, bits 1, 2, 4, 8.
    /// </summary>
    Mask,
}

/// <summary>Picks a layer's state from <see cref="WFCorruptionVisuals.Mask"/> on the client.</summary>
[RegisterComponent]
public sealed partial class WFCorruptionVisualsComponent : Component
{
    /// <summary>The sprite layer to set, by its map key.</summary>
    [DataField]
    public string Layer = "base";

    /// <summary>The state is this followed by the mask.</summary>
    [DataField]
    public string Prefix = "corruption-";

    /// <summary>When a floor tile joins up on all eight sides it picks one of these at random instead, so a carpet doesn't repeat. Empty for none.</summary>
    [DataField]
    public List<string> FullStates = new();
}
