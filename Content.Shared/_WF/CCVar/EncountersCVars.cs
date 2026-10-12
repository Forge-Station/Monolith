using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>Settings for random encounters.</summary>
[CVarDefs]
public sealed class EncountersCVars
{
    /// <summary>Whether the scheduler starts encounters on its own. Admins can always start one.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.encounters.enabled", false, CVar.SERVERONLY);

    /// <summary>The storyteller preset in force; the lobby vote sets it each round.</summary>
    public static readonly CVarDef<string> Preset =
        CVarDef.Create("wf.encounters.preset", "WFEncounterPresetStandard", CVar.SERVERONLY);

    /// <summary>Whether players vote for the preset in the lobby each round.</summary>
    public static readonly CVarDef<bool> Vote =
        CVarDef.Create("wf.encounters.vote", true, CVar.SERVERONLY);

    /// <summary>The most spesos encounter rewards pay one player in an hour.</summary>
    public static readonly CVarDef<int> PayoutHourlyCap =
        CVarDef.Create("wf.encounters.payout_hourly_cap", 120000, CVar.SERVERONLY);

    /// <summary>Shortest wait between scheduled encounters, in seconds.</summary>
    public static readonly CVarDef<float> IntervalMin =
        CVarDef.Create("wf.encounters.interval_min", 900f, CVar.SERVERONLY);

    /// <summary>Longest wait between scheduled encounters, in seconds.</summary>
    public static readonly CVarDef<float> IntervalMax =
        CVarDef.Create("wf.encounters.interval_max", 1800f, CVar.SERVERONLY);

    /// <summary>The scheduler starts nothing while this many encounters are unresolved.</summary>
    public static readonly CVarDef<int> MaxActive =
        CVarDef.Create("wf.encounters.max_active", 2, CVar.SERVERONLY);

    /// <summary>A resolved encounter's ships are removed once no player is within this distance.</summary>
    public static readonly CVarDef<float> CleanupRange =
        CVarDef.Create("wf.encounters.cleanup_range", 2500f, CVar.SERVERONLY);

    /// <summary>Seconds a resolved encounter's ship must be left alone before it is removed.</summary>
    public static readonly CVarDef<float> CleanupDelay =
        CVarDef.Create("wf.encounters.cleanup_delay", 300f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds after an encounter resolves before its ships are removed however near players are; only players
    /// aboard or docked with a ship keep it longer. A trader that should jump out waits this long for its customers.
    /// </summary>
    public static readonly CVarDef<float> CleanupLinger =
        CVarDef.Create("wf.encounters.cleanup_linger", 1200f, CVar.SERVERONLY);

    /// <summary>
    /// Stations no encounter is placed at or routed to, and that open-space encounters keep clear of: a comma-separated
    /// list of name fragments, matched against the grid, station and station id names. For places meant to stay secret.
    /// A grid whose IFF hides it or its label counts as hidden too.
    /// </summary>
    public static readonly CVarDef<string> HiddenStations =
        CVarDef.Create("wf.encounters.hidden_stations", "Helios", CVar.SERVERONLY);

    /// <summary>How far, in metres, open-space encounters keep from a hidden station.</summary>
    public static readonly CVarDef<float> HiddenClearance =
        CVarDef.Create("wf.encounters.hidden_clearance", 6000f, CVar.SERVERONLY);
}
