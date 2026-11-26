namespace Content.Shared.Movement.Pulling.Events;

/// <summary>
/// Event raised directed BOTH at the puller and pulled entity when a pull starts.
/// </summary>
public sealed class PullStartedMessage(EntityUid pullerUid, EntityUid pullableUid) : PullMessage(pullerUid, pullableUid);

/// <summary>
/// Raised on the same entities as <see cref="PullStartedMessage"/> after the one subscriber of that event has run.
/// </summary>
public sealed class PullStartedAfterEvent(EntityUid pullerUid) : EntityEventArgs
{
    public EntityUid PullerUid { get; } = pullerUid;
}
