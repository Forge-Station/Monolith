namespace Content.Server._WF.NpcCrew.Components;

/// <summary>A crewman sent to take someone alive: stun them, cuff them and drag them to a chair.</summary>
[RegisterComponent]
public sealed partial class WFCrewGrabberComponent : Component
{
    [ViewVariables]
    public EntityUid Target;

    /// <summary>The chair they end up on.</summary>
    [ViewVariables]
    public EntityUid Chair;

    /// <summary>Baton swings at the target so far, for those a baton won't put down.</summary>
    [ViewVariables]
    public int Swings;

    /// <summary>When the crewman may next try to cuff or buckle, so a running attempt isn't started over.</summary>
    [ViewVariables]
    public TimeSpan NextTry;
}
