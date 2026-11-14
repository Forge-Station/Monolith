using Content.Shared._WF.Encounters;
using Robust.Shared.Network;

namespace Content.Server._WF.Encounters.Components;

/// <summary>The warrant an encounter has posted on the sector bounty board. Sits on the encounter's entity.</summary>
[RegisterComponent]
public sealed partial class WFEncounterBountyComponent : Component
{
    /// <summary>The board entry's id, if the board took it.</summary>
    public uint? Contract;

    /// <summary>Key of the ship the warrant is for.</summary>
    public string Ship = string.Empty;

    public WFEncounterBountyTarget Target;

    /// <summary>What the payout line calls the warrant.</summary>
    public string Title = string.Empty;

    /// <summary>The name the board shows: the wanted person's, or the ship's.</summary>
    public string Name = string.Empty;

    /// <summary>What the warrant started at.</summary>
    public int Base;

    /// <summary>What it pays now: <see cref="Base"/> and everything added since.</summary>
    public int Pool;

    /// <summary>The wanted crewman, for a warrant on a captain.</summary>
    public EntityUid? Fugitive;

    /// <summary>Every crewman marked as a target.</summary>
    public List<EntityUid> Crew = new();

    /// <summary>Players who have damaged a target in person.</summary>
    public HashSet<NetUserId> Fighters = new();

    /// <summary>Where the target was last seen, for a payout once the ship is gone.</summary>
    public System.Numerics.Vector2 LastSeen;

    public Robust.Shared.Map.MapId LastMap;

    public WFBountyState State;
}

public enum WFBountyState : byte
{
    /// <summary>On the board and unclaimed.</summary>
    Open,

    /// <summary>Paid, or found to have no one to pay.</summary>
    Claimed,

    /// <summary>The encounter ended without the target taken; taken off the board, nobody paid.</summary>
    Void,
}

/// <summary>Marks a crewman an encounter's warrant is for.</summary>
[RegisterComponent]
public sealed partial class WFEncounterBountyTargetComponent : Component
{
    public EntityUid Encounter;
}
