namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>When a body may next be told its wounds are closing from heat, and how often it has been. Server only.</summary>
[RegisterComponent]
public sealed partial class WolfmedCauteryAnnounceComponent : Component
{
    [ViewVariables]
    public TimeSpan Next;

    /// <summary>Lines told so far. Tests count spam with it.</summary>
    [ViewVariables]
    public int Count;
}
