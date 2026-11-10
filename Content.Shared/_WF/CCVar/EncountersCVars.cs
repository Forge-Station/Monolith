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

    /// <summary>Shortest wait between freight runs, which keep their own schedule, in seconds.</summary>
    public static readonly CVarDef<float> FreightIntervalMin =
        CVarDef.Create("wf.encounters.freight_interval_min", 600f, CVar.SERVERONLY);

    /// <summary>Longest wait between freight runs, in seconds.</summary>
    public static readonly CVarDef<float> FreightIntervalMax =
        CVarDef.Create("wf.encounters.freight_interval_max", 1200f, CVar.SERVERONLY);

    /// <summary>The scheduler starts no freight run while this many are unresolved.</summary>
    public static readonly CVarDef<int> FreightMaxActive =
        CVarDef.Create("wf.encounters.freight_max_active", 4, CVar.SERVERONLY);

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

    /// <summary>Seconds a resolved encounter's ship stays after the last player has left it or undocked from it.</summary>
    public static readonly CVarDef<float> CleanupGrace =
        CVarDef.Create("wf.encounters.cleanup_grace", 300f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds after its last fight with players that an encounter's ship is kept: its encounter doesn't run out or
    /// fly off, and its hull isn't removed, so the fight can finish and the wreck be stripped.
    /// </summary>
    public static readonly CVarDef<float> CombatGrace =
        CVarDef.Create("wf.encounters.combat_grace", 600f, CVar.SERVERONLY);

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
    /// <summary>
    /// Whether encounter ships are simulated while no player is near: a point on the sector map with no grid, spawned
    /// as a real ship once a player comes within <see cref="MaterializeRange"/> or plots a jump near it.
    /// </summary>
    public static readonly CVarDef<bool> Simulate =
        CVarDef.Create("wf.encounters.simulate", true, CVar.SERVERONLY);

    /// <summary>How near, in metres, a player brings a simulated ship to spawn it; past radar range so none pops in on a scope.</summary>
    public static readonly CVarDef<float> MaterializeRange =
        CVarDef.Create("wf.encounters.materialize_range", 4000f, CVar.SERVERONLY);

    /// <summary>How far, in metres, every player must be from an untouched real ship for it to go back to simulation.</summary>
    public static readonly CVarDef<float> DematerializeRange =
        CVarDef.Create("wf.encounters.dematerialize_range", 6000f, CVar.SERVERONLY);

    /// <summary>Seconds an untouched real ship must be left that far alone before it goes back to simulation.</summary>
    public static readonly CVarDef<float> DematerializeDelay =
        CVarDef.Create("wf.encounters.dematerialize_delay", 120f, CVar.SERVERONLY);

    /// <summary>Seconds between one ship spawning or despawning for simulation and the next, so a crowd of them doesn't stall a tick.</summary>
    public static readonly CVarDef<float> MaterializeInterval =
        CVarDef.Create("wf.encounters.materialize_interval", 1.5f, CVar.SERVERONLY);

    /// <summary>Seconds a simulated fight between two even ships takes, on average, before one of them is lost.</summary>
    public static readonly CVarDef<float> DuelMeanSeconds =
        CVarDef.Create("wf.encounters.duel_mean_seconds", 120f, CVar.SERVERONLY);
}
