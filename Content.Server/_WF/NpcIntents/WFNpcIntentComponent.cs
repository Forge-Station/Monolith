using Content.Shared._WF.NpcIntents;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcIntents;

/// <summary>Where an NPC is in an engagement.</summary>
public enum WFNpcIntentPhase : byte
{
    None,
    Demanding,
    Warning,
    Fighting,
    Taking,
    Leaving,
}

/// <summary>An NPC with a say in how a fight ends: it rolls an endpoint from its set when it picks a target.</summary>
[RegisterComponent]
public sealed partial class WFNpcIntentComponent : Component
{
    /// <summary>The weighted endpoints this NPC rolls from.</summary>
    [DataField(required: true)]
    public ProtoId<WFNpcIntentSetPrototype> Set;

    /// <summary>Whether its brain has the hold branch (<c>WFIntentHold</c>), so it can hold someone up.</summary>
    [DataField]
    public bool Holds;

    /// <summary>Steals and fights only when hit while aboard a ship that is not its own.</summary>
    [DataField]
    public bool Boarder;

    /// <summary>Whether a hold-up or warning is aimed only at players.</summary>
    [DataField]
    public bool PlayersOnly = true;

    /// <summary>Tiles it looks for someone to hold up or warn.</summary>
    [DataField]
    public float SightRange = 9f;

    /// <summary>Seconds it walks off after an endpoint.</summary>
    [DataField]
    public float LeaveSeconds = 12f;

    /// <summary>Tiles it walks off.</summary>
    [DataField]
    public float LeaveDistance = 20f;

    /// <summary>Tiles from the victim it must be to take anything.</summary>
    [DataField]
    public float TakeRange = 2.2f;

    /// <summary>Seconds it gives itself to reach a downed victim.</summary>
    [DataField]
    public float TakeSeconds = 8f;

    /// <summary>The endpoint it rolled.</summary>
    [ViewVariables]
    public WFNpcIntentKind? Active;

    [ViewVariables]
    public WFNpcIntentPhase Phase;

    [ViewVariables]
    public EntityUid? Target;

    /// <summary>The one who speaks and takes; the rest of a gang only follow its lead.</summary>
    [ViewVariables]
    public bool Follower;

    /// <summary>When the current phase runs out.</summary>
    [ViewVariables]
    public TimeSpan PhaseEnd;

    /// <summary>When the engagement started.</summary>
    [ViewVariables]
    public TimeSpan Started;

    /// <summary>What a hold-up asks for, and what has been paid.</summary>
    [ViewVariables]
    public int Demand;

    [ViewVariables]
    public int Paid;

    /// <summary>Whether the second warning shot has been fired.</summary>
    [ViewVariables]
    public bool SecondShot;

    /// <summary>Where it walks off to, so only its own walk is cancelled.</summary>
    [ViewVariables]
    public EntityCoordinates? LeaveTo;

    /// <summary>When it last saw its target; a fight it can no longer find ends.</summary>
    [ViewVariables]
    public TimeSpan LastSeen;

    /// <summary>What it has taken off its victim: items, organs and limbs.</summary>
    [ViewVariables]
    public List<EntityUid> Took = new();

    /// <summary>The thalers among what it took.</summary>
    [ViewVariables]
    public int TookCash;

    /// <summary>Players it leaves alone, until when.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Cooldowns = new();
}
