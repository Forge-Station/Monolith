using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Shipyard;

[Serializable, NetSerializable]
public enum HangarVesselState : byte
{
    Deployed,
    InHangar,
}

[Serializable, NetSerializable]
public sealed class HangarVesselEntry
{
    public Guid VesselGuid;
    public string VesselProtoId = string.Empty;
    public string CustomName = string.Empty;
    public HangarVesselState State;

    public HangarVesselEntry()
    {
    }

    public HangarVesselEntry(Guid vesselGuid, string vesselProtoId, string customName, HangarVesselState state)
    {
        VesselGuid = vesselGuid;
        VesselProtoId = vesselProtoId;
        CustomName = customName;
        State = state;
    }
}
