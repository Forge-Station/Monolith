namespace Content.Server._WF.Remade.Counterplay;

/// <summary>Raised on a grid before corruption grows onto one of its tiles. Cancel it to keep the tile clear.</summary>
[ByRefEvent]
public record struct WFCorruptionGrowAttemptEvent(Vector2i Tile)
{
    public bool Cancelled;
}

/// <summary>Raised on a Monolith when its resonance reaches the Crescendo and it roots as the Prime.</summary>
public sealed class WFMonolithRootedEvent : EntityEventArgs;
