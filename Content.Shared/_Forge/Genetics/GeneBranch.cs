using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Genetics;

[Serializable, NetSerializable]
public enum GeneBranch : byte
{
    Physical = 0,
    Vision = 1,
    Metabolism = 2,
    Neural = 3,
    Adaptation = 4,
    Morphology = 5,
}
