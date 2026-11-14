using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.Rest;

[Serializable, NetSerializable]
public enum XenoRestState : byte
{
    NotResting,
    Resting
}
