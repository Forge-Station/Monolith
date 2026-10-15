using Content.Shared.Radio;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Drydock;

/// <summary>
/// Drydock repair and refit console. Its berth is the set of cranes linked to its source port with a multitool.
/// </summary>
[RegisterComponent]
public sealed partial class WFDrydockConsoleComponent : Component
{
    /// <summary>Source port the console links its cranes on.</summary>
    public const string CranePort = "WFDrydockCraneSender";

    /// <summary>Sink port a crane takes its console's link on.</summary>
    public const string CraneReceiverPort = "WFDrydockCraneReceiver";

    /// <summary>Name the berth goes by in messages. Empty: the console's entity name.</summary>
    [DataField]
    public string Berth = string.Empty;

    /// <summary>Tiles of guide lights outside the bay.</summary>
    [DataField]
    public float ApproachLength = 16f;

    /// <summary>Seconds the berth lights stay on after they are switched on.</summary>
    [DataField]
    public float BerthLightsTime = 120f;

    /// <summary>When the berth lights go out, or null while they are off. Runtime only.</summary>
    public TimeSpan? BerthLightsUntil;

    /// <summary>Last console state pushed to viewers, as a key. Runtime only.</summary>
    public string? LastPushed;

    /// <summary>Least fee per repair job, never refunded.</summary>
    [DataField]
    public int MinFee = 1500;

    /// <summary>Repair fee as a share of the ship's value (its shipyard price, else its full rebuild price).</summary>
    [DataField]
    public float FeeFraction = 0.025f;

    /// <summary>Price per missing hull tile.</summary>
    [DataField]
    public int TilePrice = 120;

    /// <summary>Multiple of an entity's estimated prototype price charged to rebuild it.</summary>
    [DataField]
    public float EntityPriceFactor = 2f;

    /// <summary>
    /// Share of an entity's construction material value charged to rebuild it, when more than its price rule. Below 1,
    /// so a plastitanium hull (priced far under its sheets) doesn't hit the repair cap at once.
    /// </summary>
    [DataField]
    public float MaterialPriceFactor = 0.35f;

    /// <summary>Labor added to the price of rebuilding each entity.</summary>
    [DataField]
    public int EntityLabor = 60;

    /// <summary>
    /// Price per point of damage healed, for parts without a destruction threshold. Parts with one cost their rebuild
    /// price times the share of their health lost.
    /// </summary>
    [DataField]
    public float DamagePrice = 1f;

    /// <summary>Most a repair's work may cost, as a share of the ship's shipyard price; the fee comes on top.</summary>
    [DataField]
    public float CapFraction = 0.5f;

    /// <summary>
    /// Most a repair's work may cost, as a share of its full rebuild price, for ships not bought at a shipyard. A full
    /// rebuild runs about 1.3 to 1.9 times a ship's shipyard price.
    /// </summary>
    [DataField]
    public float SnapshotCapFraction = 0.4f;

    /// <summary>Seconds one crane needs to restore a hull tile.</summary>
    [DataField]
    public float TileWorkTime = 2f;

    /// <summary>Seconds one crane needs to restore a missing entity.</summary>
    [DataField]
    public float EntityWorkTime = 8f;

    /// <summary>Seconds one crane needs to heal a damaged part.</summary>
    [DataField]
    public float HealWorkTime = 3f;

    /// <summary>Seconds one crane needs to refit a part by one tier.</summary>
    [DataField]
    public float UpgradeWorkTime = 30f;

    /// <summary>
    /// Longest repair in seconds at full power, crane deploy and travel included; longer work is sped up to fit.
    /// </summary>
    [DataField]
    public float MaxDuration = 360f;

    /// <summary>Longest refit in seconds at full power, as <see cref="MaxDuration"/> is for repairs.</summary>
    [DataField]
    public float MaxRefitDuration = 120f;

    /// <summary>Longest hull break in seconds at full power, as <see cref="MaxDuration"/> is for repairs.</summary>
    [DataField]
    public float MaxBreakDuration = 600f;

    /// <summary>Seconds one crane needs to strip an entity off a hull it breaks.</summary>
    [DataField]
    public float BreakEntityWorkTime = 0.45f;

    /// <summary>Seconds one crane needs to lift a stripped hull tile.</summary>
    [DataField]
    public float BreakTileWorkTime = 0.1f;

    /// <summary>Radio channel a finished or aborted job is announced on, or null for none.</summary>
    [DataField]
    public ProtoId<RadioChannelPrototype>? NoticeChannel;

    /// <summary>Seconds every berth crane may stay unpowered during a job before it is aborted and refunded.</summary>
    [DataField]
    public float PowerAbortTime = 60f;

    /// <summary>Seconds a crane keeps trying for an out-of-reach item before it is skipped and refunded.</summary>
    [DataField]
    public float ReachTimeout = 10f;

    /// <summary>Tiles of slack when deciding whether a crane can reach a point.</summary>
    [DataField]
    public float ReachTolerance = 1f;

    /// <summary>Seconds between sparks at the item being worked on.</summary>
    [DataField]
    public float WorkSparkInterval = 0.35f;

    /// <summary>Seconds between tool sounds at the item being worked on.</summary>
    [DataField]
    public float WorkSoundInterval = 1.3f;

    /// <summary>Seconds between machinery sounds elsewhere on the ship during a job.</summary>
    [DataField]
    public float IndustrySoundInterval = 2.2f;

    /// <summary>Fastest a ship may move relative to the drydock, in m/s, for work to start.</summary>
    [DataField]
    public float MaxStartSpeed = 1f;

    /// <summary>Seconds between random sparks elsewhere on the ship during a job.</summary>
    [DataField]
    public float AmbientSparkInterval = 1.5f;

    /// <summary>Spark effect played where work is applied.</summary>
    [DataField]
    public EntProtoId SparkEffect = "EffectSparks";

    /// <summary>Tool sounds while a crane restores a hull tile: welding and grinding.</summary>
    [DataField]
    public SoundSpecifier HullWorkSound = new SoundCollectionSpecifier("WFDrydockHullWork");

    /// <summary>Tool sounds while a crane rebuilds a missing part: bolt drivers, ratchets and welding.</summary>
    [DataField]
    public SoundSpecifier PartWorkSound = new SoundCollectionSpecifier("WFDrydockPartWork");

    /// <summary>Tool sounds while a crane patches a damaged part: welding, grinding and hammering.</summary>
    [DataField]
    public SoundSpecifier DamageWorkSound = new SoundCollectionSpecifier("WFDrydockDamageWork");

    /// <summary>Tool sounds while a crane refits a machine: drills, ratchets and screwdrivers.</summary>
    [DataField]
    public SoundSpecifier RefitWorkSound = new SoundCollectionSpecifier("WFDrydockRefitWork");

    /// <summary>Machinery clanks, presses and grinders at random spots on the ship during a job.</summary>
    [DataField]
    public SoundSpecifier IndustrySound = new SoundCollectionSpecifier("WFDrydockIndustry", AudioParams.Default.WithVolume(-4f));

    /// <summary>Hydraulics played when a crane starts moving.</summary>
    [DataField]
    public SoundSpecifier CraneMoveSound = new SoundCollectionSpecifier("WFDrydockCraneMove", AudioParams.Default.WithVolume(-3f));

    /// <summary>Played when a crane's tool head arrives at its item.</summary>
    [DataField]
    public SoundSpecifier CraneArriveSound = new SoundCollectionSpecifier("MetalThud", AudioParams.Default.WithVolume(-4f));

    /// <summary>Sound played when a request is refused.</summary>
    [DataField]
    public SoundSpecifier DenySound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");

    /// <summary>Sound played when a job finishes.</summary>
    [DataField]
    public SoundSpecifier ConfirmSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

    /// <summary>Refit lines this console sells and restores.</summary>
    [DataField]
    public List<ProtoId<WFShipRefitPrototype>> Refits = new();

    /// <summary>
    /// Share of a refit's price charged to bring a unit the ship already paid for back to its tier, as after a rebuild.
    /// </summary>
    [DataField]
    public float RefitRepairShare = 0.2f;
}
