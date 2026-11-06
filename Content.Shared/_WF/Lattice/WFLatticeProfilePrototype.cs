using Content.Shared._WF.Announcements;
using System.Numerics;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Lattice;

/// <summary>How a sector's Lattice is laid out and what each band of its field does.</summary>
[Prototype("wfLatticeProfile")]
public sealed partial class WFLatticeProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Primary anchors spawned on a sector map that has none, at the corners of a regular polygon.</summary>
    [DataField]
    public int AnchorCount = 6;

    /// <summary>Distance of each primary anchor from the map's centre, in metres.</summary>
    [DataField]
    public float AnchorRadius = 3000f;

    /// <summary>Angle of the first primary anchor, in degrees anticlockwise from east.</summary>
    [DataField]
    public float AnchorAngle = 30f;

    /// <summary>How far from an anchor its beams to its neighbours start: about the edge of its station's shield, in metres.</summary>
    [DataField]
    public float BeamOffset = 22f;

    /// <summary>What is spawned for each primary anchor.</summary>
    [DataField]
    public EntProtoId AnchorPrototype = "WFLatticeAnchorMarker";

    /// <summary>Build each primary anchor as a small shielded station, rather than spawning <see cref="AnchorPrototype"/> loose.</summary>
    [DataField]
    public bool AnchorStations = true;

    /// <summary>Radius of an anchor station's deck, in tiles.</summary>
    [DataField]
    public int StationRadius = 5;

    [DataField]
    public string StationTile = "FloorReinforced";

    /// <summary>The anchor itself, at the station's centre: one per anchor in turn, wrapping round.</summary>
    [DataField]
    public List<EntProtoId> CorePrototypes = new()
    {
        "WFLatticeAnchorCoreAlpha",
        "WFLatticeAnchorCoreBeta",
        "WFLatticeAnchorCoreGamma",
        "WFLatticeAnchorCoreDelta",
        "WFLatticeAnchorCoreEpsilon",
        "WFLatticeAnchorCoreZeta",
    };

    /// <summary>Colour of the anchor shields' Ward-like field.</summary>
    [DataField]
    public Color ShieldTint = Color.FromHex("#b040e0");

    /// <summary>The shield generator that guards the station.</summary>
    [DataField]
    public EntProtoId ShieldPrototype = "WFLatticeAnchorShield";

    /// <summary>What comes out to defend an anchor under attack.</summary>
    [DataField]
    public EntProtoId DefenderPrototype = "MonoDroneSpawnerT1";

    /// <summary>Seconds between an anchor's defences, and how far off its drones arrive, in metres.</summary>
    [DataField]
    public float DefenceCooldown = 300f;

    [DataField]
    public float DefenceDistance = 300f;

    /// <summary>Grid file each anchor station is loaded from; its core is swapped for that anchor's own. Built in code if empty.</summary>
    [DataField]
    public string? StationMap = "/Maps/_WF/Lattice/anchor_station.yml";

    /// <summary>Hits from one ship before the station says it is under fire. It fights back only once its shield is down.</summary>
    [DataField]
    public float StrikeLimit = 10f;

    /// <summary>Seconds after which a ship's earlier hits are forgiven.</summary>
    [DataField]
    public float StrikeWindow = 60f;

    /// <summary>Seconds a ship stays an attacker after its last hit; the station only shoots at it while the shield is down.</summary>
    [DataField]
    public float HostileSeconds = 90f;

    /// <summary>Furthest the station's lances reach for a hostile grid, in metres.</summary>
    [DataField]
    public float DefenceRange = 3000f;

    /// <summary>Seconds between the station's alerts.</summary>
    [DataField]
    public float AlertCooldown = 120f;

    /// <summary>DEFCON score for each primary anchor lost.</summary>
    [DataField]
    public float BreachDefcon = 25f;

    /// <summary>Distance at which one anchor's hold falls to half, in metres.</summary>
    [DataField]
    public float Reach = 4000f;

    /// <summary>The most Drift a ship or person can hold.</summary>
    [DataField]
    public float DriftMax = 100f;

    /// <summary>Seconds between each player's hallucination rolls.</summary>
    [DataField]
    public float EffectInterval = 20f;

    /// <summary>Chance of a hallucination per roll at no Drift and at full Drift.</summary>
    [DataField]
    public Vector2 EffectChance = new(0.15f, 0.75f);

    /// <summary>What can be seen and heard, and from how much Drift.</summary>
    [DataField]
    public List<WFLatticeEffect> Effects = new();

    /// <summary>Least band where crewed ships suffer the Coda's hazards: hunters, hull rot and failing systems.</summary>
    [DataField]
    public int HazardBand = 4;

    /// <summary>Seconds a ship can sit in the Coda before hunters can find it.</summary>
    [DataField]
    public float HunterGrace = 180f;

    /// <summary>Seconds of exposure past the grace over which the hunters' chance per roll climbs to its most.</summary>
    [DataField]
    public float HunterRamp = 900f;

    /// <summary>Most chance per roll (once a minute) that a hunter pack boards.</summary>
    [DataField]
    public float HunterMaxChance = 0.6f;

    /// <summary>Seconds after a pack boards before another can.</summary>
    [DataField]
    public float HunterCooldown = 300f;

    /// <summary>Hunters in a pack: least and most.</summary>
    [DataField]
    public Vector2i HunterPack = new(2, 4);

    /// <summary>Most living hunters aboard one ship.</summary>
    [DataField]
    public int HunterCap = 8;

    /// <summary>What hunts.</summary>
    [DataField]
    public List<EntProtoId> HunterMobs = new();

    /// <summary>Structures the Unmooring eats on an unsheltered ship each rot tick (every 20 s), at the edge and far out.</summary>
    [DataField]
    public Vector2i RotTargets = new(2, 6);

    /// <summary>Structural damage per structure per rot tick.</summary>
    [DataField]
    public float RotDamage = 12f;

    /// <summary>Chance per failure roll (every 40 s) that something aboard gives out.</summary>
    [DataField]
    public float FailureChance = 0.45f;

    /// <summary>How long a failure lasts, in seconds: least and most.</summary>
    [DataField]
    public Vector2 FailureSeconds = new(8f, 20f);

    /// <summary>Least band in which the dead rise as Remade, unless sheltered.</summary>
    [DataField]
    public int RiseBand = 4;

    /// <summary>Seconds a body lies dead there before it starts to turn.</summary>
    [DataField]
    public float RiseDelay = 60f;

    /// <summary>Least band in which a ship's small animals can be taken.</summary>
    [DataField]
    public int TakeBand = 4;

    /// <summary>Chance per minute that a ship there loses one, at the band's edge; it doubles far out.</summary>
    [DataField]
    public float TakeChance = 0.04f;

    /// <summary>Heaviest animal that can be taken, in kilograms.</summary>
    [DataField]
    public float TakeMaxMass = 30f;

    /// <summary>How much faster animals are taken for each primary anchor lost: 0.5 is half again per anchor.</summary>
    [DataField]
    public float TakeLostScale = 0.5f;

    /// <summary>Remade a wreck in the Deep fills with when it becomes a lost ship.</summary>
    [DataField]
    public List<EntProtoId> LostShipMobs = new();

    /// <summary>Seconds between chances of a wreck becoming a lost ship.</summary>
    [DataField]
    public float LostShipInterval = 600f;

    /// <summary>Chance each interval with the Lattice whole, and what each lost primary anchor adds to it.</summary>
    [DataField]
    public float LostShipChance = 0.1f;

    [DataField]
    public float LostShipChancePerAnchor = 0.12f;

    /// <summary>The innermost band a lost ship can appear in.</summary>
    [DataField]
    public int LostShipBand = 4;

    /// <summary>How many Remade a lost ship holds, least and most.</summary>
    [DataField]
    public Vector2i LostShipCount = new(2, 4);

    /// <summary>No wreck turns with a player closer than this, in metres.</summary>
    [DataField]
    public float LostShipUnwatched = 600f;

    /// <summary>Clear floor tiles a wreck needs to hold its Remade.</summary>
    [DataField]
    public int LostShipMinFloor = 12;

    /// <summary>What a rock's exotic matter drops as.</summary>
    [DataField]
    public EntProtoId ExoticPrototype = "WFMaterialExoticMatter1";

    /// <summary>Use the map's drone rings as the band edges after the first, when there is one ring per edge.</summary>
    [DataField]
    public bool DroneRingEdges = true;

    /// <summary>Bands, innermost first. The first band's edge is ignored.</summary>
    [DataField(required: true)]
    public List<WFLatticeBand> Bands = new();
}

/// <summary>One band of the field.</summary>
[DataDefinition]
public sealed partial class WFLatticeBand
{
    /// <summary>Fluent ID of the band's name.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Fluent ID of the message shown on entering the band. Takes <c>$band</c>.</summary>
    [DataField(required: true)]
    public LocId Message;

    /// <summary>Inner edge for an intact Lattice, in metres from the centre. Breaches move it inward.</summary>
    [DataField]
    public float Edge;

    /// <summary>Drift gained per second here; negative fades it.</summary>
    [DataField]
    public float Drift;

    /// <summary>Drift rate multiplier grows as e^(this × depth), depth running 0 to 1 past the band's edge.</summary>
    [DataField]
    public float DriftGrowth;

    /// <summary>Whether the band's name and message come out garbled, worse with depth.</summary>
    [DataField]
    public bool Garbled;

    [DataField]
    public WFAnnounceChipLevel Chip = WFAnnounceChipLevel.Advisory;

    [DataField]
    public Color Color = Color.White;

    /// <summary>Whether the chip flashes.</summary>
    [DataField]
    public bool Flash;

    /// <summary>Ore from asteroid rocks here, relative to anchored space.</summary>
    [DataField]
    public float Yield = 1f;

    /// <summary>Exotic matter shards per vein of a rock broken here, on average.</summary>
    [DataField]
    public float Exotic;

    /// <summary>Whether FTL drives work here, both to jump out and as a jump's destination.</summary>
    [DataField]
    public bool Ftl = true;
}
