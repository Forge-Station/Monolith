using Robust.Shared.Serialization;

namespace Content.Shared._WF.Bonfires;

/// <summary>Appearance keys of a fuelled fire.</summary>
[Serializable, NetSerializable]
public enum WFBonfireVisuals : byte
{
    /// <summary>Whether the fire is lit.</summary>
    Lit,
}
