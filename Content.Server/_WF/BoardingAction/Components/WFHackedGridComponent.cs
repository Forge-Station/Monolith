namespace Content.Server._WF.BoardingAction.Components;

/// <summary>On a hull with a hacking cable in it.</summary>
[RegisterComponent]
public sealed partial class WFHackedGridComponent : Component
{
    /// <summary>The hacking guns on this hull and the ship each is on.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, EntityUid> Hackers = new();

    /// <summary>The ships whose lockdown has landed here: this hull's guns can't bear on them.</summary>
    [ViewVariables]
    public HashSet<EntityUid> LockedOut = new();

    /// <summary>This hull's thrusters when the hack began.</summary>
    [ViewVariables]
    public List<EntityUid> Engines = new();

    /// <summary>How many of <see cref="Engines"/> have started arcing.</summary>
    [ViewVariables]
    public int EnginesArcing;

    /// <summary>The arcs running across the engines until the last cable is gone.</summary>
    [ViewVariables]
    public List<EntityUid> Arcs = new();
}

/// <summary>On a ship with a hacking cable out: none of its ship guns can fire.</summary>
[RegisterComponent]
public sealed partial class WFHackSourceGridComponent : Component
{
    /// <summary>This ship's guns with a cable out.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Guns = new();
}
