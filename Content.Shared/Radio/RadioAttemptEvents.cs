namespace Content.Shared.Radio;

/// <summary>
/// Cancel sending a radio message to one receiver.
/// </summary>
[ByRefEvent]
public record struct RadioReceiveAttemptEvent(RadioChannelPrototype Channel, EntityUid RadioSource, EntityUid RadioReceiver)
{
    public readonly RadioChannelPrototype Channel = Channel;
    public readonly EntityUid RadioSource = RadioSource;
    public readonly EntityUid RadioReceiver = RadioReceiver;
    public bool Cancelled = false;
}

/// <summary>
/// Cancel sending a radio message to every receiver.
/// </summary>
[ByRefEvent]
public record struct RadioSendAttemptEvent(RadioChannelPrototype Channel, EntityUid RadioSource, int Frequency)
{
    public readonly RadioChannelPrototype Channel = Channel;
    public readonly EntityUid RadioSource = RadioSource;
    public readonly int Frequency = Frequency;
    public bool Cancelled = false;
}
