using Content.Shared._WF.Encounters;

namespace Content.Server._WF.Encounters.Components;

/// <summary>What an extorter is doing.</summary>
public enum WFExtortPhase : byte
{
    /// <summary>Flying its wander legs until the next toll run is due.</summary>
    Roam,

    /// <summary>Closing on its mark.</summary>
    Close,

    /// <summary>Beside its mark, about to hail it or waiting to hail it again.</summary>
    Hail,

    /// <summary>Its hail is ringing aboard the mark.</summary>
    Calling,

    /// <summary>The mark answered and has the toll in front of it.</summary>
    Demand,

    /// <summary>Boarding the mark as a hunting ship does.</summary>
    Board,
}

/// <summary>An extorter's round of tolls: its mark, how far it has got with it and the ships it leaves alone.</summary>
public sealed class WFExtortState
{
    public WFEncounterExtort Config = new();

    public WFExtortPhase Phase;

    /// <summary>When it entered <see cref="Phase"/>.</summary>
    public TimeSpan PhaseSince;

    /// <summary>The ship it is after.</summary>
    public EntityUid? Mark;

    /// <summary>The toll named to the current mark, which rises with the cargo aboard.</summary>
    public int? Toll;

    /// <summary>Hails the mark has had: those that rang aboard, and those that could not ring with it alongside.</summary>
    public int Attempts;

    /// <summary>Since when the mark's line has been unable to ring.</summary>
    public TimeSpan? DeadSince;

    /// <summary>When it next picks a mark.</summary>
    public TimeSpan NextMark;

    /// <summary>When it may next hail its mark.</summary>
    public TimeSpan NextHail;

    /// <summary>When a mark that has not picked an answer is taken to have refused.</summary>
    public TimeSpan DemandEnds;

    /// <summary>When it closes a channel it has said its last line on.</summary>
    public TimeSpan? CloseAt;

    /// <summary>The console and crewman its hails go through.</summary>
    public EntityUid? Console;

    public EntityUid? Operator;

    /// <summary>Ships it leaves alone, and until when: those that paid, those it raided and those it could not catch.</summary>
    public Dictionary<EntityUid, TimeSpan> Spared = new();

    /// <summary>Tolls taken, and their sum.</summary>
    public int Tolls;

    public int Collected;

    /// <summary>Raids it has started.</summary>
    public int Raids;
}
