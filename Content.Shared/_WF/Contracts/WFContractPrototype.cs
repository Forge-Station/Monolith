using Content.Shared._Mono.Company;
using Content.Shared._WF.Encounters;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Contracts;

/// <summary>Who posts contracts on the board: a station or company, with the stations it works from.</summary>
[Prototype("wfContractor")]
public sealed partial class WFContractorPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>One line on who they are, shown on the board's reputation tab.</summary>
    [DataField(required: true)]
    public LocId Description;

    /// <summary>Who the work is really for, such as a faction or company, shown with every contract it posts.</summary>
    [DataField(required: true)]
    public LocId Employer;

    [DataField]
    public Color Color = Color.White;

    /// <summary>Game maps it works from; the first in the round is home.</summary>
    [DataField(required: true)]
    public List<string> Stations = new();

    /// <summary>How many offers it keeps on the board at once.</summary>
    [DataField]
    public int Offers = 3;

    /// <summary>Order on the board's reputation tab.</summary>
    [DataField]
    public int Order;

    /// <summary>Which boards post its offers, by <c>WFContractBoardComponent.Board</c>.</summary>
    [DataField]
    public string Board = WFContractBoards.Central;

    /// <summary>Only members of these companies may take its contracts; empty for anyone.</summary>
    [DataField]
    public List<ProtoId<CompanyPrototype>> Companies = new();

    /// <summary>Standing points it drifts toward neutral each round a character plays, good or bad.</summary>
    [DataField]
    public int Drift = 1;
}

public static class WFContractBoards
{
    /// <summary>The public board at Caelestinus Central.</summary>
    public const string Central = "Central";
}

/// <summary>A contract template; offers rolled from it fix their pay and ends when posted.</summary>
[Prototype("wfContract")]
public sealed partial class WFContractPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<WFContractorPrototype> Contractor;

    [DataField(required: true)]
    public WFContractCategory Category;

    /// <summary>Risk from 1 (anyone in a shuttle) to 5 (a crewed, armed ship); higher tiers need standing with the contractor.</summary>
    [DataField]
    public int Tier = 1;

    /// <summary>Takes $contractor, $destination, $origin, $count, $goods and $target.</summary>
    [DataField(required: true)]
    public LocId Title;

    /// <summary>Same arguments as <see cref="Title"/>.</summary>
    [DataField(required: true)]
    public LocId Briefing;

    /// <summary>Base pay, rolled between the two; objectives may add to it.</summary>
    [DataField(required: true)]
    public Vector2i Reward;

    /// <summary>Collateral taken on accepting, returned with the pay. Lost on failure or abandoning.</summary>
    [DataField]
    public int Deposit;

    /// <summary>Minutes to finish it once accepted.</summary>
    [DataField]
    public float Minutes = 45f;

    [DataField]
    public float Weight = 1f;

    /// <summary>Fewest players in the round before it is offered.</summary>
    [DataField]
    public int MinPlayers;

    /// <summary>Crew members in the round the holder's crew needs to take it; its pay is sized for that many.</summary>
    [DataField]
    public int MinCrew = 1;

    /// <summary>Share of the pay added for each crewmate in the round past <see cref="MinCrew"/>, up to four.</summary>
    [DataField]
    public float CrewBonus;

    [DataField(required: true)]
    public WFContractObjective Objective = default!;

    /// <summary>Standing lost with other contractors on completing it, for work against their interests.</summary>
    [DataField]
    public Dictionary<ProtoId<WFContractorPrototype>, int> Harms = new();

    /// <summary>Faction credits handed to the holder on completing it, on top of the pay.</summary>
    [DataField]
    public int Credits;

    /// <summary>The stacked entity <see cref="Credits"/> are paid in.</summary>
    [DataField]
    public EntProtoId? CreditEntity;

    /// <summary>Items the taker must be carrying to accept it, such as a tow kit.</summary>
    [DataField]
    public List<WFContractRequirement> Requires = new();

    /// <summary>Game presets it is never offered in.</summary>
    [DataField]
    public List<string> ExcludePresets = new();

    /// <summary>A contract offered only to the crew that finished this one, as the next link of a chain.</summary>
    [DataField]
    public ProtoId<WFContractPrototype>? FollowUp;
}

/// <summary>An item the taker must carry: an entity prototype or one of its children, or anything with a tag.</summary>
[DataDefinition, Serializable]
public sealed partial class WFContractRequirement
{
    [DataField]
    public EntProtoId? Entity;

    [DataField]
    public ProtoId<TagPrototype>? Tag;

    /// <summary>What the board calls it.</summary>
    [DataField(required: true)]
    public LocId Name;
}

public enum WFContractCategory : byte
{
    /// <summary>Hand-carried parcels between stations.</summary>
    Delivery,

    /// <summary>Crates that need a ship's hold.</summary>
    Hauling,

    /// <summary>Goods the contractor wants bought, made or found, packed in a contract crate.</summary>
    Supply,

    /// <summary>Raw ore packed in a contract crate.</summary>
    Mining,

    /// <summary>A ship to be destroyed.</summary>
    Bounty,

    /// <summary>Something to recover from a wreck.</summary>
    Salvage,

    /// <summary>An NPC ship to see safely to its destination.</summary>
    Escort,

    /// <summary>Anomalies to survey aboard a derelict.</summary>
    Science,

    /// <summary>Surface work. Reserved: no objective uses it yet.</summary>
    Planetary,
}

/// <summary>Where a contract sends its holder, for the board's location line.</summary>
public enum WFContractSite : byte
{
    Station,
    Space,

    /// <summary>Reserved for planetary contracts.</summary>
    Planet,
}

/// <summary>What a contract asks for. Each kind has its own handling in the server's contract system.</summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class WFContractObjective;

public enum WFContractEnd : byte
{
    /// <summary>The contractor's own station.</summary>
    Contractor,

    /// <summary>The contract board at Caelestinus Central.</summary>
    Board,

    /// <summary>Another contractor's station, from <see cref="WFContractCourier.Destinations"/> or any.</summary>
    Other,
}

/// <summary>Carry sealed packages from one end to the other.</summary>
public sealed partial class WFContractCourier : WFContractObjective
{
    [DataField(required: true)]
    public EntProtoId Package;

    /// <summary>How many packages, rolled between the two.</summary>
    [DataField]
    public Vector2i Count = new(1, 1);

    [DataField]
    public WFContractEnd From = WFContractEnd.Board;

    [DataField]
    public WFContractEnd To = WFContractEnd.Contractor;

    /// <summary>For an end of <see cref="WFContractEnd.Other"/>: the contractors whose stations it may be. Empty for any.</summary>
    [DataField]
    public List<ProtoId<WFContractorPrototype>> Destinations = new();

    /// <summary>Pay added per package.</summary>
    [DataField]
    public int PerPackage;

    /// <summary>Pay added per kilometre between the two ends.</summary>
    [DataField]
    public int PerKilometre;
}

/// <summary>Fill a contract crate with goods and leave it closed on the contractor's station.</summary>
public sealed partial class WFContractSupply : WFContractObjective
{
    [DataField(required: true)]
    public List<WFContractGoods> Goods = new();

    /// <summary>Lines of <see cref="Goods"/> picked per offer, at random; zero for all.</summary>
    [DataField]
    public int Pick;

    [DataField]
    public EntProtoId Crate = "WFContractCrate";

    /// <summary>Pay added as a multiple of the goods' market value, so the work beats selling them.</summary>
    [DataField]
    public float Premium = 1.25f;

    /// <summary>Most the goods add to the pay; unset, the top base pay.</summary>
    [DataField]
    public int? GoodsCap;

    /// <summary>Pay added per kilometre from the board to the station.</summary>
    [DataField]
    public int PerKilometre;
}

/// <summary>A supply line: a stack, entity, tag or component, and how many.</summary>
[DataDefinition, Serializable]
public sealed partial class WFContractGoods
{
    [DataField]
    public ProtoId<StackPrototype>? Stack;

    [DataField]
    public EntProtoId? Entity;

    [DataField]
    public ProtoId<TagPrototype>? Tag;

    /// <summary>Any entity with this component, by its registered name, such as AnomalyCore.</summary>
    [DataField]
    public string? Component;

    /// <summary>What the manifest calls it; left out, the stack's or entity's name.</summary>
    [DataField]
    public LocId? Name;

    [DataField]
    public Vector2i Count = new(1, 1);

    /// <summary>Market value of one, for a tag line or to override the estimate.</summary>
    [DataField]
    public int? Value;
}

/// <summary>Destroy an encounter's ships.</summary>
public sealed partial class WFContractBounty : WFContractObjective
{
    [DataField(required: true)]
    public List<ProtoId<WFEncounterPrototype>> Encounters = new();

    /// <summary>Range from the contractor's station the target is placed at.</summary>
    [DataField]
    public float MinDistance = 3000f;

    [DataField]
    public float MaxDistance = 5000f;

    /// <summary>How far the target must be from every station, the contractor's included.</summary>
    [DataField]
    public float StationClearance = 2500f;
}

/// <summary>Find a wreck and bring back an item from it.</summary>
public sealed partial class WFContractRecovery : WFContractObjective
{
    [DataField(required: true)]
    public List<ResPath> Wrecks = new();

    [DataField(required: true)]
    public EntProtoId Item;

    [DataField]
    public float MinDistance = 3000f;

    [DataField]
    public float MaxDistance = 5000f;

    /// <summary>How far the target must be from every station, the contractor's included.</summary>
    [DataField]
    public float StationClearance = 2500f;

    /// <summary>Where the item goes. <see cref="WFContractEnd.Other"/> is not used here.</summary>
    [DataField]
    public WFContractEnd To = WFContractEnd.Contractor;

    /// <summary>Hostile mobs placed aboard the wreck.</summary>
    [DataField]
    public List<WFContractSpawn> Infestation = new();

    /// <summary>Pay added per unlocked node, for a xenoarchaeology artifact brought in analysed.</summary>
    [DataField]
    public int PerUnlockedNode;
}

/// <summary>Scan every anomaly aboard a derelict with an anomaly scanner before any of them collapses.</summary>
public sealed partial class WFContractSurvey : WFContractObjective
{
    [DataField(required: true)]
    public List<ResPath> Wrecks = new();

    /// <summary>Anomalies to pick from, one roll per anomaly placed.</summary>
    [DataField(required: true)]
    public List<EntProtoId> Anomalies = new();

    /// <summary>How many anomalies, rolled between the two.</summary>
    [DataField]
    public Vector2i Count = new(1, 1);

    /// <summary>Pay added per anomaly.</summary>
    [DataField]
    public int PerAnomaly;

    /// <summary>Hostile mobs placed aboard the derelict.</summary>
    [DataField]
    public List<WFContractSpawn> Infestation = new();

    [DataField]
    public float MinDistance = 3000f;

    [DataField]
    public float MaxDistance = 5000f;

    /// <summary>How far the derelict must be from every station, the contractor's included.</summary>
    [DataField]
    public float StationClearance = 2500f;

    /// <summary>Derelicts loaded side by side, the anomalies spread across them.</summary>
    [DataField]
    public int Derelicts = 1;
}

/// <summary>Tow a derelict to within range of the contractor's station; it fails if the hull is destroyed.</summary>
public sealed partial class WFContractTow : WFContractObjective
{
    [DataField(required: true)]
    public List<ResPath> Wrecks = new();

    /// <summary>Metres between the derelict's hull and the station's that count as delivered.</summary>
    [DataField]
    public float Range = 150f;

    /// <summary>Hostile mobs placed aboard the derelict.</summary>
    [DataField]
    public List<WFContractSpawn> Infestation = new();

    [DataField]
    public float MinDistance = 2500f;

    [DataField]
    public float MaxDistance = 4000f;

    /// <summary>How far the derelict must be from every station, the contractor's included.</summary>
    [DataField]
    public float StationClearance = 2000f;
}

/// <summary>Entities placed on random free tiles of a contract's wreck.</summary>
[DataDefinition, Serializable]
public sealed partial class WFContractSpawn
{
    [DataField(required: true)]
    public EntProtoId Entity;

    /// <summary>How many, rolled between the two.</summary>
    [DataField]
    public Vector2i Count = new(1, 1);
}

/// <summary>Escort an encounter ship to another station; done when it has flown its route.</summary>
public sealed partial class WFContractEscort : WFContractObjective
{
    /// <summary>Encounters to start, one picked per offer. Each needs a ship that flies the route.</summary>
    [DataField(required: true)]
    public List<ProtoId<WFEncounterPrototype>> Encounters = new();

    /// <summary>Key of the ship to protect.</summary>
    [DataField]
    public string Ship = "freighter";

    /// <summary>Contractors whose stations it may be bound for; empty for any.</summary>
    [DataField]
    public List<ProtoId<WFContractorPrototype>> Destinations = new();

    /// <summary>How far out from the contractor's station it starts.</summary>
    [DataField]
    public float MinDistance = 1200f;

    [DataField]
    public float MaxDistance = 2000f;

    /// <summary>Pay added per kilometre of the run.</summary>
    [DataField]
    public int PerKilometre;
}

/// <summary>Companies on one side: they may help each other's contracts.</summary>
[Prototype("wfContractFaction")]
public sealed partial class WFContractFactionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<ProtoId<CompanyPrototype>> Companies = new();
}
