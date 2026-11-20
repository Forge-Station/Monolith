using Robust.Shared.Map;

namespace Content.Server._WF.Remade.Director;

/// <summary>What one of the Remade is doing at the Director's bidding.</summary>
public enum WFRemadeMode : byte
{
    /// <summary>Goes where it last heard or sensed the crew.</summary>
    Hunt,

    /// <summary>Follows a crewman out of sight until it commits.</summary>
    Stalk,

    /// <summary>Hurt and unseen: making for a vent to get away.</summary>
    Retreat,

    /// <summary>In the vents. Not in the world.</summary>
    Hidden,

    /// <summary>An Infector sent to raise a growth. It is left alone until the job is done.</summary>
    Build,

    /// <summary>One of a Monolith's garrison: it holds near the stone and the Director's hunting doesn't move it.</summary>
    Guard,
}

/// <summary>The Director's hold on one Remade: what it knows, where it is going and what it is up to.</summary>
[RegisterComponent]
public sealed partial class WFRemadeBehaviourComponent : Component
{
    [ViewVariables]
    public WFRemadeMode Mode;

    /// <summary>What it follows (a stalker) or runs to (a retreat, a vent).</summary>
    [ViewVariables]
    public EntityUid? Target;

    /// <summary>Where it last heard or sensed the crew.</summary>
    [ViewVariables]
    public EntityCoordinates? Known;

    [ViewVariables]
    public TimeSpan KnownAt;

    /// <summary>When it next senses the crew's whereabouts, faintly.</summary>
    [ViewVariables]
    public TimeSpan NextDrift;

    /// <summary>Drifts in a row that have found nobody. Each sharpens the next.</summary>
    [ViewVariables]
    public int Misses;

    [ViewVariables]
    public TimeSpan ModeUntil;

    /// <summary>Times it has fled the crew.</summary>
    [ViewVariables]
    public int Retreats;

    /// <summary>What it does when it comes out of a vent.</summary>
    [ViewVariables]
    public WFRemadeMode AfterVent;

    /// <summary>The vent it came out of, last.</summary>
    [ViewVariables]
    public EntityUid? ArrivalVent;

    /// <summary>Sight radii a stalker was given, kept to put back.</summary>
    [ViewVariables]
    public (float Vision, float Aggro)? Sight;

    /// <summary>The first Remade the crew see: it slips into a vent once seen instead of fighting.</summary>
    [ViewVariables]
    public bool Sighting;
}

/// <summary>A Remade moving through the vents, hidden inside its Monolith until it bursts out.</summary>
[RegisterComponent]
public sealed partial class WFRemadeVentTripComponent : Component
{
    /// <summary>When it passes each intermediate vent.</summary>
    [ViewVariables]
    public List<(EntityUid Vent, TimeSpan At)> Hops = new();

    [ViewVariables]
    public int Next;

    [ViewVariables]
    public TimeSpan ExitAt;

    [ViewVariables]
    public EntityUid? ExitVent;

    [ViewVariables]
    public EntityCoordinates ExitAtCoordinates;

    /// <summary>Times it has held off bursting out because a crewman could see the vent.</summary>
    [ViewVariables]
    public int Waited;

    /// <summary>The Monolith holding it.</summary>
    [ViewVariables]
    public EntityUid Holder;

    /// <summary>It waits in the exit vent for a crewman to pass instead of bursting out when it gets there.</summary>
    [ViewVariables]
    public bool Ambush;
}

/// <summary>A Remade lying in wait inside a vent, held by its Monolith, until a crewman walks by.</summary>
[RegisterComponent]
public sealed partial class WFRemadeVentAmbushComponent : Component
{
    [ViewVariables]
    public EntityUid Vent;

    /// <summary>Where the vent is, kept in case it is destroyed while the Remade waits.</summary>
    [ViewVariables]
    public EntityCoordinates At;

    [ViewVariables]
    public EntityUid Holder;

    /// <summary>It gives up and comes out hunting after this.</summary>
    [ViewVariables]
    public TimeSpan Until;

    /// <summary>When it bursts out, once the vent has told the crewman something is in it.</summary>
    [ViewVariables]
    public TimeSpan? BurstAt;

    [ViewVariables]
    public EntityUid? Target;
}

/// <summary>A Remade lying still among the dead, until someone comes near or it is hurt.</summary>
[RegisterComponent]
public sealed partial class WFRemadePlayingDeadComponent : Component
{
    /// <summary>How close a living crewman may come before it rises.</summary>
    [DataField]
    public float WakeRadius = 3.5f;
}

/// <summary>A crewman's recent fright. It rises with damage, Remade in sight and dead friends, and bleeds off with time.</summary>
[RegisterComponent]
public sealed partial class WFRemadeStressComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite)]
    public float Stress;

    /// <summary>No wave is sent at them before this, below Rapture.</summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan NoWaveUntil;
}

/// <summary>A door the Director has bolted. It lets go by itself.</summary>
[RegisterComponent]
public sealed partial class WFRemadeLockdownDoorComponent : Component
{
    [ViewVariables]
    public TimeSpan ReleaseAt;
}

/// <summary>Writing in blood on the floor. Examining it shows the words.</summary>
[RegisterComponent]
public sealed partial class WFRemadeScrawlComponent : Component
{
    /// <summary>Fluent id of the words.</summary>
    [ViewVariables]
    public string Text = "wf-remade-scrawl-1";
}
