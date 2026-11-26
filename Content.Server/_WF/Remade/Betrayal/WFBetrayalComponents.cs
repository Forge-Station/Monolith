using Content.Shared.Medical.SuitSensors;

namespace Content.Server._WF.Remade.Betrayal;

/// <summary>Raised on a Remade, and broadcast, as a corpse bursts into it. The corpse is still whole.</summary>
[ByRefEvent]
public readonly record struct WFRemadeRisenEvent(EntityUid Remade, EntityUid Corpse);

/// <summary>The last few things a player said in chat, kept in memory only.</summary>
[RegisterComponent]
public sealed partial class WFLastWordsComponent : Component
{
    [ViewVariables]
    public List<string> Lines = new();
}

/// <summary>A Remade that was a crewman, and sometimes calls out what they last said, in their name.</summary>
[RegisterComponent]
public sealed partial class WFCallerComponent : Component
{
    [ViewVariables]
    public string Name = string.Empty;

    [ViewVariables]
    public List<string> Lines = new();

    [ViewVariables]
    public TimeSpan NextCall;

    /// <summary>Lines called so far. Tests read it.</summary>
    [ViewVariables]
    public int Calls;

    [ViewVariables]
    public string? LastLine;
}

/// <summary>A dead crewman's headset, which now and then pleads over their channels in their name, a few times and then no more.</summary>
[RegisterComponent]
public sealed partial class WFLureComponent : Component
{
    [ViewVariables]
    public string Name = string.Empty;

    /// <summary>Where they died, as they would say it.</summary>
    [ViewVariables]
    public string Area = string.Empty;

    [ViewVariables]
    public int Remaining;

    [ViewVariables]
    public TimeSpan Next;

    /// <summary>Pleas sent so far and the last one. Tests read them.</summary>
    [ViewVariables]
    public int Sent;

    [ViewVariables]
    public string? LastMessage;

    [ViewVariables]
    public string? LastChannel;
}

/// <summary>A Remade that was a crewman with their suit sensor on, which still reads alive on the crew monitor.</summary>
[RegisterComponent]
public sealed partial class WFSensorGhostComponent : Component
{
    /// <summary>The dropped suit's sensor, which carries the packets.</summary>
    [ViewVariables]
    public EntityUid Sensor;

    /// <summary>The reading as the sensor last gave it, whole, with its name, job and department.</summary>
    [ViewVariables]
    public SuitSensorStatus Snapshot = default!;

    [ViewVariables]
    public bool HasCoordinates;

    [ViewVariables]
    public bool HasVitals;

    [ViewVariables]
    public TimeSpan Next;

    /// <summary>Readings sent to a crew monitor server. Tests read it.</summary>
    [ViewVariables]
    public int Sent;
}

/// <summary>A corpse a stone has moved when nobody looked.</summary>
[RegisterComponent]
public sealed partial class WFRestlessCorpseComponent : Component
{
    [ViewVariables]
    public TimeSpan Next;

    [ViewVariables]
    public int Moves;
}

/// <summary>What a stone has done to one player's mind, and when it may do more.</summary>
[RegisterComponent]
public sealed partial class WFBetrayalSubjectComponent : Component
{
    [ViewVariables]
    public TimeSpan NextKnock;

    [ViewVariables]
    public TimeSpan NextScrape;

    [ViewVariables]
    public TimeSpan NextBreath;

    [ViewVariables]
    public TimeSpan NextSteps;

    [ViewVariables]
    public TimeSpan NextFlicker;

    [ViewVariables]
    public TimeSpan NextDrop;

    [ViewVariables]
    public TimeSpan NextBleed;

    [ViewVariables]
    public TimeSpan NextTwitch;

    [ViewVariables]
    public TimeSpan NextAnnouncement;

    [ViewVariables]
    public TimeSpan NextMessage;

    [ViewVariables]
    public TimeSpan NextScore;

    /// <summary>The layers of the score last sent.</summary>
    [ViewVariables]
    public int ScoreLayers;

    /// <summary>Drips of blood still to fall.</summary>
    [ViewVariables]
    public int Drips;

    [ViewVariables]
    public TimeSpan NextDrip;

    /// <summary>Counts of each effect sent. Tests read them.</summary>
    [ViewVariables]
    public int Sounds;

    [ViewVariables]
    public int Flickers;

    [ViewVariables]
    public int Drops;

    [ViewVariables]
    public int Bleeds;

    [ViewVariables]
    public int Twitches;

    [ViewVariables]
    public int Announcements;

    [ViewVariables]
    public int Messages;

    [ViewVariables]
    public string? LastAnnouncement;
}
