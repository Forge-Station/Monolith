using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Encounters;

/// <summary>
/// Who a company is at war with. The id is the company's id. War is mutual: it holds if either company lists
/// the other. Companies with no entry are at war with nobody.
/// </summary>
/// <remarks>
/// A declared war only counts while the sector's war level is hot. Until then the ceasefire holds: the two warn
/// each other's ships off and fire only when fired on.
/// </remarks>
[Prototype("wfStanding")]
public sealed partial class WFStandingPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public List<ProtoId<CompanyPrototype>> AtWar = new();

    /// <summary>Companies it is at war with once war has been declared.</summary>
    [DataField]
    public List<ProtoId<CompanyPrototype>> DeclaredWar = new();
}

/// <summary>Which ships a zone answers to.</summary>
public enum WFEncounterZoneTargets : byte
{
    /// <summary>Every player ship that is not of the ship's own company.</summary>
    Everyone,

    /// <summary>Only ships of a company the ship's company is at war with.</summary>
    AtWar,
}
