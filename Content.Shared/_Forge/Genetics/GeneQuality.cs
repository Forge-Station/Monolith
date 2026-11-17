using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Genetics;

[Serializable, NetSerializable]
public enum GeneQuality : byte
{
    Neutral = 0,
    Good = 1,
    Bad = 2,
}
