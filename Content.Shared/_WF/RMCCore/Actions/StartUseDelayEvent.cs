namespace Content.Shared._WF.RMCCore.Actions;

/// <summary>Raised on an action when its use delay starts; handlers can move the cooldown window.</summary>
[ByRefEvent]
public struct StartUseDelayEvent
{
    public TimeSpan Delay;
    public TimeSpan Start;
    public TimeSpan End;
}
