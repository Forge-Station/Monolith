using Robust.Shared.Serialization;

namespace Content.Shared.GameTicking;

[Serializable, NetSerializable]
public sealed class TickerFactionCountEvent : EntityEventArgs
{
    public bool IsRoundStarted { get; }
    public int BluforReady { get; }
    public int RedforReady { get; }
    public int TotalReady { get; }
    public int BluforAlive { get; }
    public int RedforAlive { get; }

    public TickerFactionCountEvent(bool isRoundStarted, int bluforReady, int redforReady, int totalReady, int bluforAlive, int redforAlive)
    {
        IsRoundStarted = isRoundStarted;
        BluforReady = bluforReady;
        RedforReady = redforReady;
        TotalReady = totalReady;
        BluforAlive = bluforAlive;
        RedforAlive = redforAlive;
    }
}
