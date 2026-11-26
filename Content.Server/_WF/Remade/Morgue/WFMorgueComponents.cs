namespace Content.Server._WF.Remade.Morgue;

/// <summary>A body in a morgue tray, body bag, cryo pod or stasis bed on a ship the Monolith reaches. It turns on a short timer, with no corruption under it.</summary>
[RegisterComponent]
public sealed partial class WFMorgueTurningComponent : Component
{
    /// <summary>The Monolith that is turning it.</summary>
    [ViewVariables]
    public EntityUid Stone;

    /// <summary>The tray, bag, pod or bed it lies in.</summary>
    [ViewVariables]
    public EntityUid Holder;

    [ViewVariables]
    public TimeSpan TurnsAt;

    [ViewVariables]
    public TimeSpan NextKnock;

    /// <summary>When it climbs out, once its tray has burst open.</summary>
    [ViewVariables]
    public TimeSpan? RisesAt;
}

/// <summary>An Infector sent to a loaded tray. It pries the tray open when it gets there.</summary>
[RegisterComponent]
public sealed partial class WFMorgueCallComponent : Component
{
    [ViewVariables]
    public EntityUid Holder;

    [ViewVariables]
    public TimeSpan GiveUpAt;
}
