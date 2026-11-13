using Content.Server.Worldgen.Prototypes;
using Content.Server.Worldgen.Systems.Biomes;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Worldgen.Components;

[RegisterComponent]
[Access(typeof(BiomeSelectionSystem))]
public sealed partial class CurrentBiomeComponent : Component
{
    [ViewVariables]
    public ProtoId<BiomePrototype> BiomeId;
}
