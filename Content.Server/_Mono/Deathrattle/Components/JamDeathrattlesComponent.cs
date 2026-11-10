using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Deathrattle.Components;

/// <summary>A grid with this component swallows deathrattle radios, except the listed implants.</summary>
[RegisterComponent]
public sealed partial class JamDeathrattlesComponent : Component
{
    [DataField]
    public List<EntProtoId> Exclusions = [];
}
