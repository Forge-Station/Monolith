using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.SectorControl.Components;

/// <summary>
/// A source that holds its cell for a company and is not a player beacon. The origin marker and the faction
/// stations carry it. Protection and retakes leave these cells alone.
/// </summary>
[RegisterComponent]
public sealed partial class WFSectorCompanyHomeComponent : Component
{
    [DataField]
    public ProtoId<CompanyPrototype> Company;
}
