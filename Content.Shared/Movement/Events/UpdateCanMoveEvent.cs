using Content.Shared.Movement.Components;

namespace Content.Shared.Movement.Events;

/// <summary>
///     Raised whenever <see cref="IMoverComponent.CanMove"/> needs to be updated. Cancel this event to prevent a
///     mover from moving.
/// </summary>
public sealed class UpdateCanMoveEvent : CancellableEntityEventArgs
{
    public UpdateCanMoveEvent(EntityUid uid)
    {
        Uid = uid;
    }

    public EntityUid Uid { get; }
}

/// <summary>
/// Event raised on an entity when their value of <see cref="InputMoverComponent.CanMove"/> is updated.
/// </summary>
[ByRefEvent]
public readonly record struct CanMoveUpdatedEvent(bool CanMove);

/// <summary>
/// Raised on a source entity when its effective movement entity changes.
/// </summary>
[ByRefEvent]
public readonly record struct EffectiveMoverChangedEvent(EntityUid OldMover, EntityUid NewMover);
