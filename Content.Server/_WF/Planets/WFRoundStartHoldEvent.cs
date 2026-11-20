namespace Content.Server._WF.Planets;

/// <summary>Raised broadcast each time the ticker goes to start a round; a handler sets <see cref="Hold"/> to wait instead.</summary>
[ByRefEvent]
public record struct WFRoundStartHoldEvent(bool Force)
{
    public bool Hold;
}
