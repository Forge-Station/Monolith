namespace Content.Server._WF.Hailing;

/// <summary>
/// Marks the grid of a hailing test ship: a console and a pilot that hail on command, answer and echo.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class HailTestShipComponent : Component
{
    [ViewVariables]
    public EntityUid Console;

    [ViewVariables]
    public EntityUid Operator;

    /// <summary>The ship the pilot has been told to hail and hasn't reached yet.</summary>
    [ViewVariables]
    public EntityUid? Target;

    /// <summary>When the pilot starts trying, and when they stop.</summary>
    [ViewVariables]
    public TimeSpan HailAt;

    [ViewVariables]
    public TimeSpan GiveUpAt;

    /// <summary>When the console last had a hail on it. The ship is removed some minutes after.</summary>
    [ViewVariables]
    public TimeSpan IdleSince;

    /// <summary>The pilot has said their piece on this call.</summary>
    [ViewVariables]
    public bool Greeted;

    /// <summary>The first line of this call the pilot has not read yet.</summary>
    [ViewVariables]
    public int NextLine;

    /// <summary>The last thing the other end said, and when the pilot answers it.</summary>
    [ViewVariables]
    public string Heard = string.Empty;

    [ViewVariables]
    public TimeSpan? ReplyAt;
}
