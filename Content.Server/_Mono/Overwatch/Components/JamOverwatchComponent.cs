using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Overwatch.Components;

/// <summary>A grid with this component hides people aboard it from overwatch, except the listed companies.</summary>
[RegisterComponent]
public sealed partial class JamOverwatchComponent : Component
{
    [DataField]
    public List<ProtoId<CompanyPrototype>> ExcludedFactions = [];
}
