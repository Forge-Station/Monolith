using Content.Shared._WF.Strangers;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Strangers;

/// <summary>How a stranger goes about the people he sees.</summary>
public enum WFStrangerBehaviour : byte
{
    /// <summary>Walks up to whoever he sees and wants a word.</summary>
    Approach,

    /// <summary>Keeps his distance and follows, aboard their ship too. Never starts a conversation.</summary>
    Stalk,

    /// <summary>Holed up somewhere, such as a vehicle he's taken: goes for anyone he sees.</summary>
    Squat,
}

/// <summary>A wandering oddball: someone who walks up to players and talks at them, or just follows them about.</summary>
[RegisterComponent, Access(typeof(WFStrangerSystem))]
public sealed partial class WFStrangerComponent : Component
{
    [DataField(required: true)]
    public ProtoId<WFStrangerDialoguePrototype> Dialogue;

    [DataField]
    public WFStrangerBehaviour Behaviour = WFStrangerBehaviour.Approach;

    /// <summary>How far off he notices someone.</summary>
    [DataField]
    public float SightRange = 14f;

    /// <summary>How close he comes before he hails.</summary>
    [DataField]
    public float TalkRange = 1.8f;

    /// <summary>How close a stalker lets himself get, and how far he lets them pull ahead.</summary>
    [DataField]
    public float StalkNear = 5f;

    [DataField]
    public float StalkFar = 9f;

    /// <summary>How long a stalker waits once his mark is aboard a ship before he turns up aboard it too.</summary>
    [DataField]
    public TimeSpan BoardDelay = TimeSpan.FromSeconds(25);

    /// <summary>Things a stalker mutters now and then.</summary>
    [DataField]
    public List<LocId> Mutters = new();

    [DataField]
    public TimeSpan MutterInterval = TimeSpan.FromSeconds(45);

    /// <summary>Who he's after or talking to.</summary>
    [ViewVariables]
    public EntityUid? Target;

    /// <summary>The node he's on with <see cref="Target"/>; null until they talk.</summary>
    [ViewVariables]
    public string? Node;

    /// <summary>Whether he has hailed the target and is waiting on an answer.</summary>
    [ViewVariables]
    public bool Hailed;

    /// <summary>People he's done with, and until when he leaves them be.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Done = new();

    /// <summary>When a stalker's mark boarded the ship he isn't on.</summary>
    [ViewVariables]
    public TimeSpan? MarkAboardSince;

    [ViewVariables]
    public TimeSpan NextMutter;

    [ViewVariables]
    public TimeSpan HailedAt;

    /// <summary>How far a roamer walks in one go.</summary>
    [DataField]
    public float WanderRange = 20f;

    /// <summary>Whether he has turned on someone and his own brain is fighting.</summary>
    [ViewVariables]
    public bool Hostile;

    /// <summary>When a hostile stranger with nobody left to fight calms down.</summary>
    [ViewVariables]
    public TimeSpan? CalmAt;

    /// <summary>The camp or house he lives at, if any; null for a roamer.</summary>
    [ViewVariables]
    public EntityUid? Home;

    /// <summary>When he next picks somewhere to wander to.</summary>
    [ViewVariables]
    public TimeSpan NextWander;

    /// <summary>While he walks a leg of his wander: when he gives up getting there. Null when he isn't wandering.</summary>
    [ViewVariables]
    public TimeSpan? WanderUntil;

    /// <summary>How long he lingers where a leg of his wander ends, before he sets off again.</summary>
    [DataField]
    public TimeSpan LingerMin = TimeSpan.FromSeconds(30);

    [DataField]
    public TimeSpan LingerMax = TimeSpan.FromSeconds(90);

    /// <summary>Visitors he has already greeted on his way over, and when.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Greeted = new();
}

/// <summary>
/// Where strangers live, such as a crazy camp or a house. Those who live here wander about it and go out to meet whoever
/// they see, one of them at a time, and walk home after. Visitors are welcome: it's only the talk going badly that turns
/// them.
/// </summary>
[RegisterComponent]
public sealed partial class WFStrangerHomeComponent : Component
{
    /// <summary>The home itself: they potter about within it.</summary>
    [DataField]
    public float Radius = 7f;

    /// <summary>How far out they go to meet visitors; they won't chase anyone beyond twice this from home.</summary>
    [DataField]
    public float WarnRadius = 15f;

    /// <summary>What one says, once, on setting off to meet a visitor.</summary>
    [DataField]
    public List<LocId> Greetings = new();
}
