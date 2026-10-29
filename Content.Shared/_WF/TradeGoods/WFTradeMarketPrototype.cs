using Content.Shared._Mono.Company;
using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.TradeGoods;

/// <summary>Tuning for chute markets: pressure, floors, haul distance, goods groups and sector modifiers.</summary>
[Prototype("wfTradeMarket")]
public sealed partial class WFTradeMarketPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Pressure each sale adds to its good at that market.</summary>
    [DataField]
    public float PerSale = 0.035f;

    /// <summary>Per-good pressure per sale, for goods that print cheaply.</summary>
    [DataField]
    public Dictionary<EntProtoId, float> PerSaleOverrides = new();

    /// <summary>Time for pressure to halve.</summary>
    [DataField]
    public TimeSpan HalfLife = TimeSpan.FromMinutes(25);

    /// <summary>Lowest price as a fraction of base, unless the good has a floor price.</summary>
    [DataField]
    public float Floor = 0.2f;

    /// <summary>Per-good lowest price, replacing the fractional floor.</summary>
    [DataField]
    public Dictionary<EntProtoId, int> FloorPrices = new();

    /// <summary>Pressure one console trading crate adds at the station it is sold at.</summary>
    [DataField]
    public float TradeCratePerSale = 0.05f;

    /// <summary>Lowest share of its pallet price a console trading crate keeps at a glutted station.</summary>
    [DataField]
    public float TradeCrateFloor = 0.3f;

    /// <summary>Scarcity one trade good bought from a station's vendor adds there, raising what its vendors charge.</summary>
    [DataField]
    public float BuyPerPurchase = 0.03f;

    /// <summary>Most scarcity can raise a vendor's price, as a fraction of its base.</summary>
    [DataField]
    public float BuyScarcityCap = 0.6f;

    /// <summary>Cap on the product of the modifiers that raise a price.</summary>
    [DataField]
    public float ModifierCap = 2.5f;

    /// <summary>How far a crate must be hauled from where it was made before a chute takes it, in metres.</summary>
    [DataField]
    public float MinHaulDistance = 3000f;

    /// <summary>A quote at or above this factor is highlighted in the Market app.</summary>
    [DataField]
    public float Highlight = 1.4f;

    /// <summary>Named lists of goods that modifiers, shortages and rush orders apply to.</summary>
    [DataField]
    public Dictionary<string, List<EntProtoId>> Groups = new();

    /// <summary>Chutes that pay full price for sealed freight.</summary>
    [DataField]
    public List<EntProtoId> HotCargoChutes = new();

    /// <summary>Chutes that take sealed freight only, refusing any crate without a manifest seal.</summary>
    [DataField]
    public List<EntProtoId> SealedOnlyChutes = new();

    /// <summary>Contraband goods: open chutes buy them, but a sale may get the seller flagged by customs.</summary>
    [DataField]
    public List<EntProtoId> ContrabandGoods = new();

    /// <summary>Chance one contraband sale at an open chute gets the seller flagged.</summary>
    [DataField]
    public float ContrabandWarrantChance = 0.05f;

    /// <summary>The warrant posted on a flagged seller.</summary>
    [DataField]
    public int ContrabandWarrant = 10000;

    /// <summary>After a flag, how long before the same seller can be flagged again.</summary>
    [DataField]
    public TimeSpan ContrabandWarrantCooldown = TimeSpan.FromMinutes(30);

    /// <summary>The radio channel security hears a flagged sale on.</summary>
    [DataField]
    public ProtoId<RadioChannelPrototype> ContrabandAlertChannel = "Nfsd";

    /// <summary>Chutes the Market app lists under its Underground tab instead of the open exchange.</summary>
    [DataField]
    public List<EntProtoId> HiddenChutes = new();

    /// <summary>Companies whose members see the hidden markets.</summary>
    [DataField]
    public List<ProtoId<CompanyPrototype>> UndergroundCompanies = new();

    /// <summary>The factions the Market app groups stations under, in order, by the chutes they run.</summary>
    [DataField]
    public List<WFTradeMarketFaction> MarketFactions = new();

    /// <summary>The group for a station whose chute no faction lists.</summary>
    [DataField]
    public LocId DefaultMarketFaction = "wf-market-faction-independent";

    [DataField]
    public Color DefaultMarketFactionColor = Color.FromHex("#9aa7b1");

    /// <summary>Navy chutes that pay war demand while the war is hot.</summary>
    [DataField]
    public List<EntProtoId> WarDemandChutes = new();

    [DataField]
    public List<string> BlockadeGroups = new();

    [DataField]
    public float BlockadeFactor = 1.8f;

    [DataField]
    public float ReliefFactor = 1.5f;

    [DataField]
    public List<string> WarGroups = new();

    [DataField]
    public float WarFactor = 1.5f;

    [DataField]
    public List<string> RaidGroups = new();

    [DataField]
    public float RaidFactor = 1.5f;

    [DataField]
    public TimeSpan RaidWindow = TimeSpan.FromMinutes(15);

    /// <summary>Spesos split between the ships that break a blockade attempt by traffic.</summary>
    [DataField]
    public int ReliefBounty = 6000;

    /// <summary>What a lawful chute pays for sealed freight, as a fraction.</summary>
    [DataField]
    public float HotCargoLawful = 0.6f;

    /// <summary>Bonus for delivering sealed freight to its own destination.</summary>
    [DataField]
    public float SalvageBonus = 1.25f;

    /// <summary>How long between one shortage or boom and the next.</summary>
    [DataField]
    public Vector2i ShortageMinutes = new(25, 40);

    /// <summary>How long between one rush order and the next.</summary>
    [DataField]
    public Vector2i RushMinutes = new(20, 35);

    /// <summary>How long between one priority consignment and the next.</summary>
    [DataField]
    public Vector2i ConsignmentMinutes = new(30, 45);

    /// <summary>Consignment crates to spawn, with their weights.</summary>
    [DataField]
    public Dictionary<EntProtoId, float> Consignments = new();

    /// <summary>The navies: whose chutes are whose, and who they're at war with.</summary>
    [DataField]
    public List<WFTradeNavy> Navies = new();

    /// <summary>Groups a navy pays its own people faction credits for while the war is hot.</summary>
    [DataField]
    public List<string> WarEffortGroups = new();

    /// <summary>Faction credits a war-effort crate earns.</summary>
    [DataField]
    public int WarEffortCredits = 2;

    /// <summary>What a navy pays the enemy's people for war goods while the war is hot.</summary>
    [DataField]
    public float DefectorFactor = 1.3f;

    /// <summary>The warrant a navy that posts them puts on one of its own who sells war goods to the enemy.</summary>
    [DataField]
    public int DefectorWarrant = 10000;
}

/// <summary>A navy's chutes and people, for war-effort pay and sales to the enemy.</summary>
[DataDefinition]
public sealed partial class WFTradeNavy
{
    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public List<EntProtoId> Chutes = new();

    [DataField]
    public List<ProtoId<CompanyPrototype>> Companies = new();

    [DataField]
    public List<ProtoId<CompanyPrototype>> Enemies = new();

    /// <summary>The faction credit its own people are paid in.</summary>
    [DataField]
    public EntProtoId? CreditEntity;

    /// <summary>Whether it posts a warrant on one of its own who sells war goods to the enemy.</summary>
    [DataField]
    public bool PostsDefectorWarrants;
}

/// <summary>A faction the Market app groups stations under: its name, colour and the chutes its stations run.</summary>
[DataDefinition]
public sealed partial class WFTradeMarketFaction
{
    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public Color Color = Color.Gray;

    [DataField]
    public List<EntProtoId> Chutes = new();
}

/// <summary>A shortage at one station, or a boom across the sector when <see cref="SectorWide"/> is set.</summary>
[Prototype("wfTradeShortage")]
public sealed partial class WFTradeShortagePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public float Weight = 1f;

    /// <summary>The goods group it raises.</summary>
    [DataField(required: true)]
    public string Group = string.Empty;

    /// <summary>Chute prototypes whose stations it can hit; empty for any.</summary>
    [DataField]
    public List<EntProtoId> Chutes = new();

    [DataField]
    public float MinFactor = 1.5f;

    [DataField]
    public float MaxFactor = 2.5f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromMinutes(30);

    /// <summary>Raises the group at every station instead of one.</summary>
    [DataField]
    public bool SectorWide;

    /// <summary>Name of the goods for announcements.</summary>
    [DataField(required: true)]
    public LocId Goods;
}

/// <summary>A rush order: a station wants a number of crates of a group, fast, at a premium.</summary>
[Prototype("wfTradeRush")]
public sealed partial class WFTradeRushPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public float Weight = 1f;

    [DataField(required: true)]
    public string Group = string.Empty;

    /// <summary>Chute prototypes whose stations can post it; empty for any.</summary>
    [DataField]
    public List<EntProtoId> Chutes = new();

    [DataField]
    public Vector2i Count = new(4, 8);

    [DataField]
    public Vector2i Minutes = new(15, 25);

    [DataField]
    public float MinFactor = 1.75f;

    [DataField]
    public float MaxFactor = 2.25f;

    [DataField(required: true)]
    public LocId Goods;
}
