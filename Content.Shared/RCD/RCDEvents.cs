using Content.Shared.Atmos.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.RCD;

[Serializable, NetSerializable]
public sealed class RCDSystemMessage : BoundUserInterfaceMessage
{
    public ProtoId<RCDPrototype> ProtoId;

    public RCDSystemMessage(ProtoId<RCDPrototype> protoId)
    {
        ProtoId = protoId;
    }
}

[Serializable, NetSerializable]
public sealed class RCDConstructionGhostRotationEvent : EntityEventArgs
{
    public readonly NetEntity NetEntity;
    public readonly Direction Direction;

    public RCDConstructionGhostRotationEvent(NetEntity netEntity, Direction direction)
    {
        NetEntity = netEntity;
        Direction = direction;
    }
}

#region Forge-Change

[Serializable, NetSerializable]
public sealed class RCDConstructionGhostFlipEvent : EntityEventArgs
{
    public readonly NetEntity NetEntity;
    public readonly bool UseMirrorPrototype;

    public RCDConstructionGhostFlipEvent(NetEntity netEntity, bool useMirrorPrototype)
    {
        NetEntity = netEntity;
        UseMirrorPrototype = useMirrorPrototype;
    }
}

[Serializable, NetSerializable]
public sealed class RCDConstructionGhostLayerEvent : EntityEventArgs
{
    public readonly NetEntity NetEntity;
    public readonly Angle EyeRotation;
    public readonly string? ConstructionEntity;
    public readonly AtmosPipeLayer PipeLayer;

    public RCDConstructionGhostLayerEvent(
        NetEntity netEntity,
        Angle eyeRotation,
        string? constructionEntity,
        AtmosPipeLayer pipeLayer = AtmosPipeLayer.Primary)
    {
        NetEntity = netEntity;
        EyeRotation = eyeRotation;
        ConstructionEntity = constructionEntity;
        PipeLayer = pipeLayer;
    }
}

#endregion

[Serializable, NetSerializable]
public enum RcdUiKey : byte
{
    Key
}
