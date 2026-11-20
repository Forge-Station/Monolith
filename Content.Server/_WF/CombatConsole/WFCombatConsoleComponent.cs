namespace Content.Server._WF.CombatConsole;

/// <summary>Stores console-local weapon groups and automatic countermeasure settings.</summary>
[RegisterComponent]
public sealed partial class WFCombatConsoleComponent : Component
{
    /// <summary>Saved group memberships retained across temporary disconnections.</summary>
    [DataField] public Dictionary<int, HashSet<EntityUid>> Groups = new();
    /// <summary>Whether this console keeps automatic countermeasures armed.</summary>
    [DataField] public bool Automatic;
    /// <summary>Eligible locks counted during the most recent scan.</summary>
    public int Threats;
    /// <summary>Earliest periodic snapshot after the last authoritative update.</summary>
    public TimeSpan NextTelemetry;
    /// <summary>Earliest periodic rebuild of radar and weapon metadata.</summary>
    public TimeSpan NextRadarTelemetry;
    /// <summary>Throttle failed cockpit discovery while retaining automatic recovery.</summary>
    public TimeSpan NextDiscovery;
}

/// <summary>Identifies a flare launcher and shares its burst lockout between consoles.</summary>
[RegisterComponent]
public sealed partial class WFFlareLauncherComponent : Component
{
    /// <summary>Earliest allowed dispense after the latest actual shot.</summary>
    public TimeSpan NextBurst;
}
