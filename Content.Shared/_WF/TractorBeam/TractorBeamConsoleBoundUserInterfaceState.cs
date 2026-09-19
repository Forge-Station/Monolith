using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.TractorBeam;

[Serializable, NetSerializable]
public enum TractorBeamUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum TractorBeamPinStatus : byte
{
    NoTarget,
    WaitingForBeam,
    StopSource,
    StopTarget,
    InsufficientPower,
    Ready,
    AlreadyPinned,
}

[Serializable, NetSerializable]
public sealed class TractorBeamConsoleMessage : BoundUserInterfaceMessage
{
    public NetEntity Emitter;
    public NetEntity? Target;
    public bool Pulling;
    public bool LockInPlace;
    public float? DesiredRange;

    public TractorBeamConsoleMessage(NetEntity emitter, NetEntity? target, bool pulling = false, bool lockInPlace = false,
        float? desiredRange = null)
    {
        Emitter = emitter;
        Target = target;
        Pulling = pulling;
        LockInPlace = lockInPlace;
        DesiredRange = desiredRange;
    }
}

[Serializable, NetSerializable]
public sealed class TractorBeamConsoleBoundUserInterfaceState : BoundUserInterfaceState
{
    public bool Connected;
    public float Range;
    public TractorBeamEmitterEntry[] Emitters;
    public TractorBeamTargetEntry[] Targets;

    public TractorBeamConsoleBoundUserInterfaceState(bool connected, float range,
        TractorBeamEmitterEntry[] emitters, TractorBeamTargetEntry[] targets)
    {
        Connected = connected;
        Range = range;
        Emitters = emitters;
        Targets = targets;
    }
}

[Serializable, NetSerializable]
public readonly struct TractorBeamEmitterEntry
{
    public readonly NetEntity Entity;
    public readonly string Name;
    public readonly NetEntity? Target;
    public readonly bool Powered;
    /// <summary>Electrical load from beam length, translation, and rotational restraint, from zero to one.</summary>
    public readonly float Strain;
    public readonly float RequestedPower;
    public readonly float ReceivedPower;
    public readonly float Range;
    public readonly bool Pulling;
    public readonly bool LockedInPlace;
    public readonly bool CanLockInPlace;
    /// <summary>Dish position relative to the source grid's center, in grid coordinates.</summary>
    public readonly Vector2 Position;
    /// <summary>Dish forward unit vector in source grid coordinates.</summary>
    public readonly Vector2 Direction;
    public readonly float ConeHalfAngle;
    public readonly float CurrentDistance;
    public readonly float HoldDistance;
    public readonly float MinimumDistance;
    public readonly float? DesiredRange;
    public readonly bool Active;
    public readonly TractorBeamPinStatus PinStatus;
    public readonly float CooldownRemaining;

    public TractorBeamEmitterEntry(NetEntity entity, string name, NetEntity? target, bool powered,
        float strain, float requestedPower, float receivedPower, float range, bool pulling = false,
        bool lockedInPlace = false, bool canLockInPlace = false, Vector2 position = default,
        Vector2 direction = default, float coneHalfAngle = TractorBeamOperatingCone.DefaultHalfAngle,
        float currentDistance = 0, float holdDistance = 0, float minimumDistance = 0, float? desiredRange = null,
        bool active = false, TractorBeamPinStatus pinStatus = TractorBeamPinStatus.NoTarget, float cooldownRemaining = 0)
    {
        Entity = entity;
        Name = name;
        Target = target;
        Powered = powered;
        Strain = strain;
        RequestedPower = requestedPower;
        ReceivedPower = receivedPower;
        Range = range;
        Pulling = pulling;
        LockedInPlace = lockedInPlace;
        CanLockInPlace = canLockInPlace;
        Position = position;
        Direction = direction == Vector2.Zero ? Vector2.UnitY : direction;
        ConeHalfAngle = coneHalfAngle;
        CurrentDistance = currentDistance;
        HoldDistance = holdDistance;
        MinimumDistance = minimumDistance;
        DesiredRange = desiredRange;
        Active = active;
        PinStatus = pinStatus;
        CooldownRemaining = cooldownRemaining;
    }
}

[Serializable, NetSerializable]
public readonly struct TractorBeamTargetEntry
{
    public readonly NetEntity Entity;
    public readonly string Name;
    /// <summary>Position relative to the emitting grid's center, in that grid's orientation.</summary>
    public readonly Vector2 Position;
    public readonly float Mass;
    public readonly bool Shielded;

    public TractorBeamTargetEntry(NetEntity entity, string name, Vector2 position, float mass, bool shielded = false)
    {
        Entity = entity;
        Name = name;
        Position = position;
        Mass = mass;
        Shielded = shielded;
    }
}
