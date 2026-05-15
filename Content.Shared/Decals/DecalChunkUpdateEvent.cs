using Robust.Shared.Serialization;
using static Content.Shared.Decals.DecalGridComponent;

namespace Content.Shared.Decals
{
    [Serializable, NetSerializable]
    public sealed class DecalChunkDelta
    {
        public Dictionary<uint, NetDecalData> Upserts = new();
        public List<uint> RemovedDecals = new();
        public bool ResetChunk;
    }

    [Serializable, NetSerializable]
    public sealed class NetDecalData
    {
        public ushort RelX;
        public ushort RelY;
        public ushort PrototypeNetId;
        public Color? Color;
        public Angle Angle;
        public int ZIndex;
        public bool Cleanable;
    }

    [Serializable, NetSerializable]
    public sealed class DecalChunkUpdateEvent : EntityEventArgs
    {
        public Dictionary<NetEntity, Dictionary<Vector2i, DecalChunkDelta>> Data = new();
        public Dictionary<NetEntity, HashSet<Vector2i>> RemovedChunks = new();
    }
}
