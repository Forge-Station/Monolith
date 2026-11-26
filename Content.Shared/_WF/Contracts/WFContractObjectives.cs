using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Contracts;

/// <summary>Carry one parcel to each of several stations in a single run.</summary>
public sealed partial class WFContractMailRun : WFContractObjective
{
    [DataField(required: true)]
    public EntProtoId Package;

    /// <summary>How many stations, rolled between the two.</summary>
    [DataField]
    public Vector2i Stops = new(3, 4);

    /// <summary>Pay added per stop.</summary>
    [DataField]
    public int PerStop;

    /// <summary>Pay added per kilometre of the whole route, board to last stop.</summary>
    [DataField]
    public int PerKilometre;
}

/// <summary>Fly a crewmate within range of each of several points in open space, logging each.</summary>
public sealed partial class WFContractSweep : WFContractObjective
{
    /// <summary>How many points, rolled between the two.</summary>
    [DataField]
    public Vector2i Points = new(3, 4);

    /// <summary>Metres from a point that count as logging it.</summary>
    [DataField]
    public float Range = 150f;

    /// <summary>Range from the contractor's station the points are placed at.</summary>
    [DataField]
    public float MinDistance = 2000f;

    [DataField]
    public float MaxDistance = 5000f;

    /// <summary>Pay added per point.</summary>
    [DataField]
    public int PerPoint;
}

/// <summary>Destroy every target placed aboard a wreck, such as carp nests.</summary>
public sealed partial class WFContractClear : WFContractObjective
{
    [DataField(required: true)]
    public List<ResPath> Wrecks = new();

    /// <summary>What must be destroyed.</summary>
    [DataField(required: true)]
    public WFContractSpawn Targets = default!;

    /// <summary>Hostile mobs placed aboard besides the targets.</summary>
    [DataField]
    public List<WFContractSpawn> Infestation = new();

    [DataField]
    public float MinDistance = 3000f;

    [DataField]
    public float MaxDistance = 5000f;

    [DataField]
    public float StationClearance = 2500f;
}

/// <summary>Repair a derelict beacon: weld it whole and fit a power cell. It jumps away a while after.</summary>
public sealed partial class WFContractRepair : WFContractObjective
{
    [DataField(required: true)]
    public List<ResPath> Wrecks = new();

    /// <summary>The broken device placed aboard.</summary>
    [DataField]
    public EntProtoId Device = "WFContractNavBeacon";

    /// <summary>Hostile mobs placed aboard.</summary>
    [DataField]
    public List<WFContractSpawn> Infestation = new();

    /// <summary>How long after the repair the derelict jumps away, once no one is aboard.</summary>
    [DataField]
    public TimeSpan Departure = TimeSpan.FromMinutes(10);

    [DataField]
    public float MinDistance = 3000f;

    [DataField]
    public float MaxDistance = 5000f;

    [DataField]
    public float StationClearance = 2500f;
}

/// <summary>Stand by a derelict terminal while it downloads, holding off waves of hostiles.</summary>
public sealed partial class WFContractDownload : WFContractObjective
{
    [DataField(required: true)]
    public List<ResPath> Wrecks = new();

    [DataField]
    public EntProtoId Terminal = "WFContractDataTerminal";

    /// <summary>Time a crewmate must spend within range of the terminal.</summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromMinutes(3);

    /// <summary>Metres from the terminal that keep the download running.</summary>
    [DataField]
    public float Range = 3f;

    /// <summary>Spawned aboard once the download starts, and again each <see cref="WaveInterval"/>.</summary>
    [DataField]
    public List<WFContractSpawn> Waves = new();

    [DataField]
    public TimeSpan WaveInterval = TimeSpan.FromSeconds(50);

    [DataField]
    public float MinDistance = 3000f;

    [DataField]
    public float MaxDistance = 5000f;

    [DataField]
    public float StationClearance = 2500f;
}

/// <summary>Board a stopped ship and inspect its marked cargo.</summary>
public sealed partial class WFContractInspect : WFContractObjective
{
    [DataField(required: true)]
    public List<ResPath> Wrecks = new();

    /// <summary>The cargo to inspect, hidden aboard.</summary>
    [DataField]
    public EntProtoId Cargo = "WFContractSmuggledCargo";

    /// <summary>Its crew, hostile once boarded.</summary>
    [DataField]
    public List<WFContractSpawn> Crew = new();

    [DataField]
    public float MinDistance = 3000f;

    [DataField]
    public float MaxDistance = 5000f;

    [DataField]
    public float StationClearance = 2500f;
}

/// <summary>A salvage operation on one battle wreck: scan its anomalies, recover an item and tow the hull home.</summary>
public sealed partial class WFContractOperation : WFContractObjective
{
    [DataField(required: true)]
    public List<ResPath> Wrecks = new();

    /// <summary>The item to recover and bring aboard the contractor's station.</summary>
    [DataField(required: true)]
    public EntProtoId Item;

    [DataField(required: true)]
    public List<EntProtoId> Anomalies = new();

    [DataField]
    public Vector2i Count = new(1, 2);

    [DataField]
    public List<WFContractSpawn> Infestation = new();

    /// <summary>Metres between the hull and the station that count as towed home.</summary>
    [DataField]
    public float Range = 150f;

    [DataField]
    public float MinDistance = 3000f;

    [DataField]
    public float MaxDistance = 4500f;

    [DataField]
    public float StationClearance = 2500f;
}
