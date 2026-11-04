using Content.Shared._WF.Planets;
using Content.Shared._WF.Xenobiology;
using Content.Shared.Maps;
using Content.Shared.NPC.Prototypes;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.WildSlimes;

/// <summary>
/// One world's wild slime breed and where it lives: the surface patches its biome lays, the grottos under them, and
/// how many slimes each holds.
/// </summary>
[Prototype("wfWildSlimeHabitat")]
public sealed partial class WFWildSlimeHabitatPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The world the slimes live on.</summary>
    [DataField(required: true)]
    public ProtoId<WFPlanetSurfacePrototype> Surface;

    /// <summary>The breed the wild slimes are.</summary>
    [DataField(required: true)]
    public ProtoId<BreedPrototype> Breed;

    /// <summary>What a wild baby spawns as.</summary>
    [DataField]
    public EntProtoId BabyPrototype = "MobSlimeXenobioBaby";

    /// <summary>What a wild adult spawns as.</summary>
    [DataField]
    public EntProtoId AdultPrototype = "MobSlimeXenobioAdult";

    /// <summary>Chance a wild slime spawns grown.</summary>
    [DataField]
    public float AdultChance = 0.3f;

    /// <summary>The template of the surface biome's top-level meta layer that holds the patches; its noise is checked first.</summary>
    [DataField(required: true)]
    public ProtoId<BiomeTemplatePrototype> PatchTemplate;

    /// <summary>The ground tile of a patch.</summary>
    [DataField(required: true)]
    public ProtoId<ContentTileDefinition> PatchTile;

    /// <summary>The cavern floor of the grotto under a patch; no grotto slimes when unset.</summary>
    [DataField]
    public ProtoId<ContentTileDefinition>? GrottoTile;

    /// <summary>Patches smaller than this many tiles hold no slimes.</summary>
    [DataField]
    public int MinPatchTiles = 12;

    /// <summary>Wild slimes on each patch at most.</summary>
    [DataField]
    public int SurfaceSlimes = 3;

    /// <summary>Wild slimes in each grotto at most.</summary>
    [DataField]
    public int GrottoSlimes = 2;

    /// <summary>How long a slime caught, tamed or killed takes to be replaced.</summary>
    [DataField]
    public TimeSpan RespawnDelay = TimeSpan.FromMinutes(10);

    /// <summary>Whether each patch gets a shaft down to its grotto.</summary>
    [DataField]
    public bool Entrance = true;

    /// <summary>The HTN a wild slime runs.</summary>
    [DataField]
    public string WildRootTask = "WFWildSlimeCompound";

    /// <summary>The HTN a slime runs once caught or tamed.</summary>
    [DataField]
    public string TameRootTask = "XenoSlimeCompound";

    /// <summary>The faction a wild slime is in, which nobody is hostile to.</summary>
    [DataField]
    public ProtoId<NpcFactionPrototype> WildFaction = "Passive";

    /// <summary>The factions a slime gets once caught or tamed.</summary>
    [DataField]
    public HashSet<ProtoId<NpcFactionPrototype>> TameFactions = new() { "SimpleHostile" };
}
