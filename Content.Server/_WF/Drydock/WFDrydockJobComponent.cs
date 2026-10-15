using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.ViewVariables;

namespace Content.Server._WF.Drydock;

/// <summary>
/// One berth crane working one item at a time during a drydock job.
/// </summary>
public sealed class WFDrydockJobSlot
{
    /// <summary>Crane this slot drives, or null when the berth has none.</summary>
    public EntityUid? Crane;

    /// <summary>Item in progress, or null when idle.</summary>
    public WFDrydockWorkItem? Item;

    /// <summary>Time the crane reaches the item and work begins.</summary>
    public TimeSpan WorkFrom;

    /// <summary>Whether the arrival at the item was announced.</summary>
    public bool Arrived;

    /// <summary>Time the item in progress finishes.</summary>
    public TimeSpan Ends;

    /// <summary>Time of the next spark on the item in progress.</summary>
    public TimeSpan NextSpark;

    /// <summary>Time of the next tool sound on the item in progress.</summary>
    public TimeSpan NextSound;

    /// <summary>Ship-local position of the crane's tool head, for picking the nearest next item.</summary>
    public Vector2 ToolPos;

    /// <summary>Whether the item in progress is out of this slot's crane reach.</summary>
    public bool Unreachable;

    /// <summary>Whether the crane is stalled for lack of power, set each tick.</summary>
    public bool Stalled;

    /// <summary>Time an unreachable item is given up on.</summary>
    public TimeSpan GiveUpAt;
}

/// <summary>
/// Runtime state of a drydock job, stored on the console while work runs. Not saved.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFDrydockJobComponent : Component
{
    /// <summary>Ship being worked on.</summary>
    [ViewVariables]
    public EntityUid Ship;

    /// <summary>Player who paid for the job; refunded for skipped and unfinished items.</summary>
    [ViewVariables]
    public EntityUid? Payer;

    /// <summary>Items not started yet.</summary>
    public List<WFDrydockWorkItem> Pending = new();

    /// <summary>One slot per berth crane, at least one.</summary>
    public List<WFDrydockJobSlot> Slots = new();

    /// <summary>Items in the job.</summary>
    [ViewVariables]
    public int Total;

    /// <summary>Items applied so far.</summary>
    [ViewVariables]
    public int Done;

    /// <summary>Items skipped because no crane could reach them.</summary>
    [ViewVariables]
    public int Skipped;

    /// <summary>Credits refunded for skipped items.</summary>
    [ViewVariables]
    public int SkippedRefund;

    /// <summary>Whether the job was paid in materials, so refunds return materials instead of credits.</summary>
    [ViewVariables]
    public bool PaidInMaterials;

    /// <summary>Refit lines the job's refit items bring up, to the highest tier among them.</summary>
    public Dictionary<string, int> Refits = new();

    /// <summary>Materials returned for skipped items of a material job.</summary>
    public Dictionary<string, int> SkippedMaterials = new();

    /// <summary>Parts returned for skipped items of a material job.</summary>
    public Dictionary<string, int> SkippedParts = new();

    /// <summary>Time the cranes finish deploying over the ship and work starts.</summary>
    [ViewVariables]
    public TimeSpan WorkStarts;

    /// <summary>Seconds every berth crane has been stalled without power in a row.</summary>
    public float Unpowered;

    /// <summary>Lowest power ratio among the job's cranes, 0 to 1.</summary>
    public float Power = 1f;

    /// <summary>Whether the cranes have finished deploying.</summary>
    [ViewVariables]
    public bool Started;

    /// <summary>Time the job must finish by at full power; lost power pushes it back.</summary>
    [ViewVariables]
    public TimeSpan Deadline;

    /// <summary>Estimated time the job finishes, given the queue and slots.</summary>
    [ViewVariables]
    public TimeSpan EstimatedEnd;

    /// <summary>Time of the next ambient spark.</summary>
    public TimeSpan NextSpark;

    /// <summary>Time of the next machinery sound elsewhere on the ship.</summary>
    public TimeSpan NextIndustry;

    /// <summary>Time the ship is next checked for still sitting in the berth.</summary>
    public TimeSpan NextBerthCheck;

    /// <summary>Time the cranes are next re-aimed at their items, which drift with the ship.</summary>
    public TimeSpan NextAim;

    /// <summary>Ship-local tile centers for ambient sparks and machinery sounds.</summary>
    public List<Vector2> SparkTiles = new();

    /// <summary>Whether the job breaks the ship up instead of repairing it.</summary>
    [ViewVariables]
    public bool Break;

    /// <summary>Ship's name, kept for the notice once a broken hull is gone.</summary>
    public string ShipName = string.Empty;

    /// <summary>Grid the drydock works from, reported as attacking a ship it breaks.</summary>
    public EntityUid? Attacker;

    /// <summary>Materials a break job has recovered so far, before its caps.</summary>
    public Dictionary<string, int> Recovered = new();

    /// <summary>Tiles a break job has lifted, which set its caps.</summary>
    [ViewVariables]
    public int Lifted;

    /// <summary>Materials a break job has credited to its cranes so far.</summary>
    public Dictionary<string, int> Yield = new();

    /// <summary>Whether a break job lifted the ship's last tile.</summary>
    [ViewVariables]
    public bool HullGone;
}
