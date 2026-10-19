using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids;

[Serializable, NetSerializable]
public enum XenoVisualLayers : byte
{
    Base,
    Hide,
    Crest,
    Fortify,
    Ovipositor,
    Burrow
}
