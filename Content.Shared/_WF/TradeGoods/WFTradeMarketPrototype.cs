using Content.Shared._Mono.Company;
using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.TradeGoods;

/// <summary>Tuning for station markets: the ledger, the price curve, floors, haul distance, goods groups and sector modifiers.</summary>
[Prototype("wfTradeMarket")]
public sealed partial class WFTradeMarketPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Time for console trading crate pressure to halve, and for a station outside the sector economy to settle back to its target.</summary>
    [DataField]
    public TimeSpan HalfLife = TimeSpan.FromMinutes(50);

    /// <summary>Lowest price as a fraction of base, unless the good has a floor price.</summary>
    [DataField]
    public float Floor = 0.2f;

    /// <summary>Per-good lowest price, replacing the fractional floor.</summary>
    [DataField]
    public Dictionary<EntProtoId, int> FloorPrices = new();

    /// <summary>Pressure one console trading crate adds at the station it is sold at.</summary>
    [DataField]
    public float TradeCratePerSale = 0.025f;

    /// <summary>Lowest share of its pallet price a console trading crate keeps at a glutted station.</summary>
    [DataField]
    public float TradeCrateFloor = 0.3f;

    /// <summary>The single-unit stacks that trade through station stock: what pallets and market consoles price by supply.</summary>
    [DataField]
    public List<EntProtoId> MaterialGoods = new();

    /// <summary>The everyday materials stations open with; freight manifests list what they bring.</summary>
    [DataField]
    public List<EntProtoId> SupplyMaterials = new();

    /// <summary>Crates of a good a station wants to hold: level 1 on the price curve.</summary>
    [DataField]
    public float StockTarget = 120f;

    /// <summary>Most crates of a good a station can hold.</summary>
    [DataField]
    public int VendorStock = 200;

    /// <summary>Lowest share of its target a station can open with for a good, before the sector settles.</summary>
    [DataField]
    public float VendorStockStartMin = 0.25f;

    /// <summary>Highest share of its target a station can open with for a good, before the sector settles.</summary>
    [DataField]
    public float VendorStockStartMax = 1f;

    /// <summary>Hours of simulated trade run at round start, so stations open as they would stand mid-round.</summary>
    [DataField]
    public float OpeningHours = 8f;

    /// <summary>Time between economy ticks.</summary>
    [DataField]
    public TimeSpan EconomyInterval = TimeSpan.FromMinutes(5);

    /// <summary>Crates of each good on its list a station uses per hour, per population.</summary>
    [DataField]
    public float UseRate = 2.4f;

    /// <summary>Crates of input an industry uses per crate it makes.</summary>
    [DataField]
    public float InputUse = 0.5f;

    /// <summary>Share of its rate an industry keeps with its input gone, so a broken chain can restart.</summary>
    [DataField]
    public float MinOutput = 0.25f;

    /// <summary>Share of a station's stock over target that goes into the sector's pool per hour.</summary>
    [DataField]
    public float ExportRate = 1f;

    /// <summary>Crates per hour reaching the sector's pool of each good nothing in the sector makes.</summary>
    [DataField]
    public float OutsideSupply = 14f;

    /// <summary>Extra output an industry gets with materials to work from, as a share of its rate.</summary>
    [DataField]
    public float MaterialBoost = 0.5f;

    /// <summary>Spesos of materials each extra crate takes.</summary>
    [DataField]
    public float MaterialCrateValue = 1000f;

    /// <summary>Level a blockaded station's imports fall toward as they are hoarded.</summary>
    [DataField]
    public float BlockadeFloor = 0.25f;

    /// <summary>Time for a blockaded station to lose half the distance down to the floor.</summary>
    [DataField]
    public TimeSpan BlockadeHalfLife = TimeSpan.FromMinutes(15);

    /// <summary>Time for prices to close half the gap to a station's stock.</summary>
    [DataField]
    public TimeSpan PriceSmoothing = TimeSpan.FromMinutes(2);

    /// <summary>Share of its listed price a chute pays at level 1.</summary>
    [DataField]
    public float ChuteTableRate = 0.9f;

    /// <summary>What empty shelves add to a price, as a fraction of base. The curve is quadratic, so small dips barely show.</summary>
    [DataField]
    public float ScarcityPremium = 1.5f;

    /// <summary>What a price loses per level over target.</summary>
    [DataField]
    public float GlutSlope = 1.2f;

    /// <summary>Levels over target at which a price stops falling.</summary>
    [DataField]
    public float GlutSpan = 0.5f;

    /// <summary>Most the stock curve and events together can raise a price, as a multiple of base.</summary>
    [DataField]
    public float PriceCap = 3f;

    /// <summary>Level below which a good is scarce: chutes take it off their table, and it can become a reported shortage.</summary>
    [DataField]
    public float ScarceLevel = 0.3f;

    /// <summary>Level a reported shortage has to recover to before it ends.</summary>
    [DataField]
    public float ShortageClearLevel = 0.45f;

    /// <summary>How long a good stays scarce before it can be reported as a shortage.</summary>
    [DataField]
    public TimeSpan ShortageDelay = TimeSpan.FromMinutes(15);

    /// <summary>Share of the target of each material on its manifest that a freighter's stop adds.</summary>
    [DataField]
    public float FreightRestock = 0.5f;

    /// <summary>Crates of owed stock each crate in a freighter's hold can deliver: its hold is bulk cargo, not a shelf's worth of crates.</summary>
    [DataField]
    public int FreightCrateStock = 25;

    /// <summary>Spesos of one material a station holds at normal prices.</summary>
    [DataField]
    public float MaterialStockTarget = 10000f;

    /// <summary>Time for a station to use up half of the materials it holds.</summary>
    [DataField]
    public TimeSpan MaterialHalfLife = TimeSpan.FromHours(3);

    /// <summary>Stock, as a multiple of the target, at which prices stop falling.</summary>
    [DataField]
    public float MaterialGlut = 3f;

    /// <summary>What a pallet pays for a material a station has none of, as a share of its appraised value.</summary>
    [DataField]
    public float MaterialPayMax = 1.2f;

    /// <summary>What a pallet pays for a material at a glutted station.</summary>
    [DataField]
    public float MaterialPayMin = 0.5f;

    /// <summary>What a console charges for a material a station is nearly out of, before the console's own modifier.</summary>
    [DataField]
    public float MaterialChargeMax = 1.5f;

    /// <summary>What a console charges for a material at a glutted station.</summary>
    [DataField]
    public float MaterialChargeMin = 0.8f;

    /// <summary>Shortest wait before a purchase or sale moves a console's material prices.</summary>
    [DataField]
    public TimeSpan PriceDelayMin = TimeSpan.FromMinutes(3);

    /// <summary>Longest wait before a purchase or sale moves a console's material prices.</summary>
    [DataField]
    public TimeSpan PriceDelayMax = TimeSpan.FromMinutes(4);

    /// <summary>Cap on the product of the modifiers that raise a price.</summary>
    [DataField]
    public float ModifierCap = 2.5f;

    /// <summary>
    /// How far a crate must be hauled from where it was made to earn haul rewards (freight bonus, war-effort credits, traffic), and
    /// how far a priority delivery's destination sits from its origin, in metres. Chutes still take nearer crates.
    /// </summary>
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
    public float BlockadeFactor = 1.25f;

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

    /// <summary>How long between one bulk order and the next.</summary>
    [DataField]
    public Vector2i BulkMinutes = new(30, 60);

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

/// <summary>An industry a station can run: it turns one good into others, and runs slower as its input runs out.</summary>
[Prototype("wfTradeIndustry")]
public sealed partial class WFTradeIndustryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>The good it uses, or none.</summary>
    [DataField]
    public EntProtoId? Input;

    [DataField(required: true)]
    public List<EntProtoId> Outputs = new();

    /// <summary>Crates of each output the whole sector makes per hour, shared between the stations running it.</summary>
    [DataField(required: true)]
    public float Rate;

    /// <summary>Materials it can work from a station's stock to make more, first listed first.</summary>
    [DataField]
    public List<EntProtoId> Materials = new();
}

/// <summary>Something a station's or the sector's stock of a good keeps running.</summary>
[Prototype("wfTradeService")]
public sealed partial class WFTradeServicePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>The good it runs on, whichever is lowest.</summary>
    [DataField(required: true)]
    public List<EntProtoId> Goods = new();

    /// <summary>Name of the goods for announcements and alerts.</summary>
    [DataField(required: true)]
    public LocId GoodsName;

    /// <summary>The sector's mean level at which it runs in full. Stations sit under target as a rule, so this is under 1.</summary>
    [DataField]
    public float Healthy = 0.6f;

    /// <summary>The share of normal it keeps running at with nothing left.</summary>
    [DataField]
    public float Minimum = 0.25f;
}

/// <summary>What a station does in the sector economy, whichever machines its map places.</summary>
[Prototype("wfTradeStation")]
public sealed partial class WFTradeStationPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The POI or game map ids that take this role.</summary>
    [DataField(required: true)]
    public List<string> Stations = new();

    [DataField]
    public List<ProtoId<WFTradeIndustryPrototype>> Industries = new();

    /// <summary>Scales what it uses.</summary>
    [DataField]
    public float Population = 1f;

    /// <summary>Goods it consumes directly, on top of its industries' inputs.</summary>
    [DataField]
    public List<EntProtoId> Uses = new();

    /// <summary>Services it runs, which get dearer as their goods run low.</summary>
    [DataField]
    public List<ProtoId<WFTradeServicePrototype>> Services = new();
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

    /// <summary>
    /// A big order for a full hold: posted on <see cref="WFTradeMarketPrototype.BulkMinutes"/>, not with rush orders,
    /// clearing its goods' sale pressure at the station, and its crates add none.
    /// </summary>
    [DataField]
    public bool Bulk;

    [DataField(required: true)]
    public LocId Goods;
}
