using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Tether;

/// <summary>Raised on both attach points once a rope is tied between them.</summary>
[ByRefEvent]
public readonly record struct RopeAttachedEvent(EntityUid Rope, EntityUid Other, ProtoId<RopeTypePrototype> RopeType);

/// <summary>Raised on both attach points when a rope stops being tied to them, for any reason.</summary>
[ByRefEvent]
public readonly record struct RopeDetachedEvent(EntityUid Rope, EntityUid Other, ProtoId<RopeTypePrototype> RopeType);

/// <summary>
/// Raised broadcast and on both ends just before a rope entity is removed. <see cref="Refunded"/>
/// is false for a snap and true for a deliberate untie.
/// </summary>
[ByRefEvent]
public readonly record struct RopeBrokenEvent(
    EntityUid Rope,
    EntityUid EndA,
    EntityUid EndB,
    ProtoId<RopeTypePrototype> RopeType,
    bool Refunded);

/// <summary>
/// Raised on an entity a rope coil was used on that is not itself an attach point. A handler may
/// supply or spawn one (power cord clamps do this) by setting <see cref="AttachPoint"/>.
/// </summary>
[ByRefEvent]
public record struct RopeCoilTargetAttemptEvent(
    EntityUid User,
    EntityUid Coil,
    EntityUid Target,
    ProtoId<RopeTypePrototype> RopeType)
{
    /// <summary>The attach point to use. Leave null to decline.</summary>
    public EntityUid? AttachPoint = null;

    /// <summary>Set by a handler that consumed the click without providing an attach point.</summary>
    public bool Handled = false;
}

/// <summary>
/// Raised on the attach point a loose end was carried from when the carry is dropped without ever
/// being tied off. Power cord clamps use this to clear away a clamp the first click bolted down.
/// </summary>
[ByRefEvent]
public readonly record struct RopeCarryCancelledEvent(EntityUid User, ProtoId<RopeTypePrototype> RopeType);

/// <summary>Untie doafter. The rope is identified explicitly because a point may hold several.</summary>
[Serializable, NetSerializable]
public sealed partial class RopeUntieDoAfterEvent : DoAfterEvent
{
    [DataField] public NetEntity Rope;

    public RopeUntieDoAfterEvent()
    {
    }

    public RopeUntieDoAfterEvent(NetEntity rope)
    {
        Rope = rope;
    }

    public override DoAfterEvent Clone() => new RopeUntieDoAfterEvent(Rope);

    public override bool IsDuplicate(DoAfterEvent other) => other is RopeUntieDoAfterEvent untie && untie.Rope == Rope;
}
