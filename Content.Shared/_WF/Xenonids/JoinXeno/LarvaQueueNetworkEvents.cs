using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenonids.JoinXeno;

[Serializable, NetSerializable]
public sealed class LarvaQueueOfferEvent : EntityEventArgs
{
    public NetEntity? TargetEntity;
    public string TargetEntityName = string.Empty;
    public double ExpiresAt;
    public string HiveName = string.Empty;
    public string OfferType = string.Empty;
    public int QueuePosition;
}

[Serializable, NetSerializable]
public sealed class LarvaQueueAcceptOfferEvent : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class LarvaQueueDeclineOfferEvent : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class LarvaQueueFollowTargetEvent : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class LarvaQueueOfferExpiredEvent : EntityEventArgs
{
    public bool LarvaDied;
}
