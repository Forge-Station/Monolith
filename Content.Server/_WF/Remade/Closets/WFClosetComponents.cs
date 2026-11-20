namespace Content.Server._WF.Remade.Closets;

/// <summary>A Remade shut in a locker, waiting for someone to open it or stand beside it.</summary>
[RegisterComponent]
public sealed partial class WFRemadeClosetLurkerComponent : Component
{
    [ViewVariables]
    public EntityUid Locker;

    /// <summary>When it next breathes or scrapes inside, if anyone is near enough to hear.</summary>
    [ViewVariables]
    public TimeSpan NextRattle;

    /// <summary>When it gives up waiting and is gone, if nobody is near.</summary>
    [ViewVariables]
    public TimeSpan GiveUpAt;
}

/// <summary>A locker with a Remade inside. Opening it lets the Remade out.</summary>
[RegisterComponent]
public sealed partial class WFRemadeLurkedLockerComponent : Component
{
    [ViewVariables]
    public EntityUid Lurker;
}

/// <summary>A locker someone hiding inside has made noise in. The Remade can find them, and tear it open, until it wears off.</summary>
[RegisterComponent]
public sealed partial class WFRemadeGiveawayComponent : Component
{
    [ViewVariables]
    public TimeSpan Until;
}
