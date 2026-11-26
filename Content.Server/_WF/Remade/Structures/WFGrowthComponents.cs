using Content.Shared._WF.Remade.Structures;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Remade.Structures;

/// <summary>Marks a growth: a structure the Director grows on corruption for its Monolith.</summary>
[RegisterComponent]
public sealed partial class WFGrowthComponent : Component
{
    /// <summary>Its entry in the Director's list of growth kinds.</summary>
    [DataField(required: true)]
    public ProtoId<WFRemadeGrowthPrototype> Kind;

    [ViewVariables]
    public EntityUid? Source;

    /// <summary>When it finishes swelling up. Null once it has.</summary>
    [ViewVariables]
    public TimeSpan? ReadyAt;

    /// <summary>Off corruption, or its Monolith is gone: it does nothing.</summary>
    [ViewVariables]
    public bool Dormant;

    /// <summary>When it dies after its Monolith has gone, if it has.</summary>
    [ViewVariables]
    public TimeSpan? DecayAt;

    [ViewVariables]
    public TimeSpan NextCheck;

    [ViewVariables]
    public WFGrowthState State = WFGrowthState.Growing;

    public bool Active => !Dormant && ReadyAt == null;
}

/// <summary>A node: feeds corruption within its reach and raises the Monolith's tile cap.</summary>
[RegisterComponent]
public sealed partial class WFCorruptionNodeComponent : Component
{
    /// <summary>Tiles it can feed.</summary>
    [DataField]
    public int Capacity = 32;

    /// <summary>Metres from the node within which it feeds tiles.</summary>
    [DataField]
    public float Reach = 5f;

    [ViewVariables]
    public EntityUid? Source;

    /// <summary>Corruption this node is feeding.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Tiles = new();
}

/// <summary>Fires a bio-bomb at anyone walking across its arc, then reloads.</summary>
[RegisterComponent]
public sealed partial class WFCystComponent : Component
{
    /// <summary>How far it sees.</summary>
    [DataField]
    public float Range = 4.5f;

    /// <summary>Degrees of its arc, centred on the way it faces.</summary>
    [DataField]
    public float Arc = 90f;

    [DataField]
    public TimeSpan Reload = TimeSpan.FromSeconds(8);

    [DataField]
    public EntProtoId Bomb = "WFRemadeCystBomb";

    [DataField]
    public float BombSpeed = 12f;

    [DataField]
    public SoundSpecifier FireSound = new SoundCollectionSpecifier("WFRemadeCystFire");

    [ViewVariables]
    public bool Ready;

    [ViewVariables]
    public TimeSpan ReloadAt;

    [ViewVariables]
    public TimeSpan NextScan;

    /// <summary>Bombs fired so far. Tests read it.</summary>
    [ViewVariables]
    public int Shots;
}

/// <summary>Trips and slows whoever steps on it, once per reload.</summary>
[RegisterComponent]
public sealed partial class WFSnareComponent : Component
{
    [DataField]
    public TimeSpan Reload = TimeSpan.FromSeconds(8);

    [DataField]
    public TimeSpan Knockdown = TimeSpan.FromSeconds(3);

    [DataField]
    public TimeSpan Slow = TimeSpan.FromSeconds(4);

    [DataField]
    public float SlowMultiplier = 0.6f;

    [DataField]
    public SoundSpecifier TripSound = new SoundPathSpecifier("/Audio/Effects/Fluids/splat.ogg");

    [ViewVariables]
    public TimeSpan ReadyAt;

    /// <summary>Times it has tripped someone. Tests read it.</summary>
    [ViewVariables]
    public int Trips;
}

/// <summary>Warns, grabs whoever stands on it, bites them apart and eats the dead.</summary>
[RegisterComponent]
public sealed partial class WFMawComponent : Component
{
    /// <summary>Seconds between the warning and the grab.</summary>
    [DataField]
    public TimeSpan Warning = TimeSpan.FromSeconds(3);

    [DataField]
    public TimeSpan BiteEvery = TimeSpan.FromSeconds(2);

    /// <summary>Slash damage of one bite.</summary>
    [DataField]
    public float BiteDamage = 8f;

    /// <summary>Every this many bites on a living victim takes a limb.</summary>
    [DataField]
    public int LimbEvery = 3;

    [DataField]
    public float BiteBiomass = 1.5f;

    [DataField]
    public float LimbBiomass = 12f;

    [DataField]
    public float CorpseBiomass = 25f;

    [DataField]
    public float RemadeBiomass = 20f;

    /// <summary>Seconds a freed victim can stand on the maw untouched.</summary>
    [DataField]
    public TimeSpan Mercy = TimeSpan.FromSeconds(8);

    /// <summary>Seconds a rescuer takes to pull someone free, and a victim to wrench themselves out.</summary>
    [DataField]
    public TimeSpan PullTime = TimeSpan.FromSeconds(4);

    [DataField]
    public TimeSpan StruggleTime = TimeSpan.FromSeconds(8);

    [DataField]
    public SoundSpecifier WarnSound = new SoundPathSpecifier("/Audio/Effects/demon_attack1.ogg");

    [DataField]
    public SoundSpecifier GrabSound = new SoundPathSpecifier("/Audio/Effects/demon_consume.ogg");

    [DataField]
    public SoundSpecifier BiteSound = new SoundPathSpecifier("/Audio/Effects/bite.ogg");

    [DataField]
    public SoundSpecifier EatSound = new SoundCollectionSpecifier("gib");

    /// <summary>Everything touching it now.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Touching = new();

    /// <summary>When each warned victim gets grabbed, if still standing there.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Pending = new();

    /// <summary>Victims it has let go and leaves alone until then.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Immune = new();

    /// <summary>Dead bodies on it, with when it started eating each.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Eating = new();

    [ViewVariables]
    public TimeSpan NextBite;

    [ViewVariables]
    public int Bites;

    /// <summary>Times it has grabbed someone. Tests read it.</summary>
    [ViewVariables]
    public int Grabs;
}

/// <summary>Someone a maw is holding. Others can pull them free.</summary>
[RegisterComponent]
public sealed partial class WFMawHeldComponent : Component
{
    [ViewVariables]
    public EntityUid Maw;

    [ViewVariables]
    public bool Freeing;

    /// <summary>The maw is letting this one go, so its refusal to unbuckle stands aside.</summary>
    [ViewVariables]
    public bool Releasing;
}

/// <summary>Makes its chosen Remade slowly, a few at a time.</summary>
[RegisterComponent]
public sealed partial class WFNestComponent : Component
{
    [DataField]
    public EntProtoId? Produces;

    [DataField]
    public int MaxAlive = 2;

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(90);

    [ViewVariables]
    public TimeSpan NextMake;

    [ViewVariables]
    public TimeSpan NextCheck;

    [ViewVariables]
    public HashSet<EntityUid> Alive = new();

    /// <summary>Remade it has made. Tests read it.</summary>
    [ViewVariables]
    public int Made;
}

/// <summary>Turns plants, food and corpses nearby into biomass once it has warmed up.</summary>
[RegisterComponent]
public sealed partial class WFHarvesterComponent : Component
{
    [DataField]
    public float Range = 7f;

    [DataField]
    public TimeSpan WarmUp = TimeSpan.FromSeconds(60);

    /// <summary>Biomass a second from each growing plant, food item and body.</summary>
    [DataField]
    public float PlantRate = 0.15f;

    [DataField]
    public float FoodRate = 0.02f;

    [DataField]
    public float CorpseRate = 0.2f;

    [DataField]
    public int FoodCap = 40;

    [DataField]
    public int CorpseCap = 6;

    [ViewVariables]
    public TimeSpan? ActiveAt;

    [ViewVariables]
    public bool Running;

    /// <summary>Biomass a second at the last count.</summary>
    [ViewVariables]
    public float Rate;

    [ViewVariables]
    public TimeSpan NextScan;

    [ViewVariables]
    public TimeSpan LastPay;
}

/// <summary>Watches a radius. When a crewman walks in, the Director hears of it.</summary>
[RegisterComponent]
public sealed partial class WFEyeComponent : Component
{
    [DataField]
    public float Range = 7f;

    /// <summary>How far the Remade hear of a sighting.</summary>
    [DataField]
    public float AlertRange = 28f;

    [DataField]
    public TimeSpan AlertEvery = TimeSpan.FromSeconds(8);

    [DataField]
    public TimeSpan NoticeEvery = TimeSpan.FromSeconds(60);

    [DataField]
    public TimeSpan StaysOpen = TimeSpan.FromSeconds(6);

    [ViewVariables]
    public TimeSpan NextScan;

    [ViewVariables]
    public TimeSpan NextAlert;

    [ViewVariables]
    public TimeSpan NextNotice;

    [ViewVariables]
    public TimeSpan? OpenUntil;

    /// <summary>Times it has told the Director. Tests read it.</summary>
    [ViewVariables]
    public int Alerts;
}

/// <summary>An Infector sent to raise a growth at a place.</summary>
[RegisterComponent]
public sealed partial class WFRemadeBuildJobComponent : Component
{
    [ViewVariables]
    public ProtoId<WFRemadeGrowthPrototype> Kind;

    [ViewVariables]
    public Robust.Shared.Map.EntityCoordinates Site;

    [ViewVariables]
    public EntityUid Monolith;

    [ViewVariables]
    public TimeSpan GiveUpAt;

    /// <summary>When the work ends, once it has begun.</summary>
    [ViewVariables]
    public TimeSpan? WorkUntil;

    [ViewVariables]
    public TimeSpan NextSound;
}
