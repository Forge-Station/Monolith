using Content.Shared._WF.Contracts;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Contracts;

/// <summary>One contract, from offer to its end. Everything the holder was told is fixed when it is posted.</summary>
public sealed class WFContract
{
    public int Id;
    public ProtoId<WFContractPrototype> Prototype;
    public WFContractStatus Status = WFContractStatus.Offered;

    public int Reward;
    public int Deposit;
    public TimeSpan Duration;

    /// <summary>When an offer leaves the board, then when an accepted contract runs out.</summary>
    public TimeSpan Deadline;

    /// <summary>The contractor's station, where it is home.</summary>
    public EntityUid Home;

    /// <summary>Courier pickup station; null for the board.</summary>
    public EntityUid? Origin;

    /// <summary>Delivery station; null for the board.</summary>
    public EntityUid? Destination;

    /// <summary>Packages to carry.</summary>
    public int Count = 1;

    /// <summary>Supply lines: index into the objective's goods, and how many.</summary>
    public List<(int Line, int Count)> Goods = new();

    /// <summary>The encounter a bounty is on.</summary>
    public ProtoId<Shared._WF.Encounters.WFEncounterPrototype>? Encounter;

    /// <summary>The wreck a recovery or survey loads.</summary>
    public Robust.Shared.Utility.ResPath? Wreck;

    /// <summary>Where a bounty or recovery target is placed; fixed at accepting.</summary>
    public MapCoordinates? Site;

    public NetUserId? User;
    public string Character = string.Empty;

    /// <summary>The holder's company when they accepted it, for telling allies from meddlers.</summary>
    public string? Company;
    public TimeSpan Accepted;
    public TimeSpan Ended;

    /// <summary>Packages, crate or recovered item in play.</summary>
    public List<EntityUid> Items = new();

    public int Delivered;
    public EntityUid? EncounterEntity;
    public EntityUid? WreckGrid;

    /// <summary>Anomalies a survey placed, and those scanned so far.</summary>
    public List<EntityUid> Anomalies = new();
    public HashSet<EntityUid> Scanned = new();

    /// <summary>Further derelicts loaded beside <see cref="WreckGrid"/>.</summary>
    public List<EntityUid> ExtraWrecks = new();

    /// <summary>A mail run's stations, in order, and the parcel bound for each.</summary>
    public List<EntityUid> Stops = new();
    public Dictionary<EntityUid, EntityUid> ParcelStops = new();

    /// <summary>A sweep's points and the ones logged.</summary>
    public List<MapCoordinates> Points = new();
    public HashSet<int> Logged = new();

    /// <summary>What a clearing contract must destroy, or the device a repair, download or inspection is about.</summary>
    public List<EntityUid> Targets = new();

    /// <summary>Download time banked so far, whether it has started, and when the next wave comes.</summary>
    public TimeSpan Progress;
    public bool Started;
    public TimeSpan NextWave;

    /// <summary>Parts of a salvage operation done.</summary>
    public bool Recovered;
    public bool Towed;

    /// <summary>The player whose crew alone may see and take this offer: the next link of a chain.</summary>
    public NetUserId? ReservedFor;

    /// <summary>Courier distance in metres, for the pay and the board.</summary>
    public float Distance;

    /// <summary>Radar waypoints placed for the holder's crew, by ship; null where the crew deleted it.</summary>
    public Dictionary<EntityUid, int?> Markers = new();

    /// <summary>Target ships already labelled with who holds the contract.</summary>
    public HashSet<EntityUid> Claimed = new();
}

/// <summary>A character: the player and the name they play under.</summary>
public readonly record struct WFContractHolder(NetUserId User, string Character);
