using Content.Shared.Parallax.Biomes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Procedural.Loot;

/// <summary>
/// Adds a biome template layer for dungeon loot.
/// </summary>
public sealed partial class BiomeTemplateLoot : IDungeonLoot
{
    [DataField("proto", required: true, customTypeSerializer:typeof(ProtoId<BiomeTemplatePrototype>))]
    public string Prototype = string.Empty;
}
