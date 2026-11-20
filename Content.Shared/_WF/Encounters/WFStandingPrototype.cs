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

    /// <summary>Thalers paid at once to a player who kills one of its NPC crewmen, half that to one who assists; zero for none.</summary>
    [DataField]
    public int KillBounty;

    /// <summary>
    /// Thalers a downed NPC crewman drops at his feet per step of threat tier, tier 0 counting as one step, give or take
    /// a quarter; zero for none.
    /// </summary>
    [DataField]
    public int CashDrop;
}

/// <summary>Which ships a zone answers to.</summary>
public enum WFEncounterZoneTargets : byte
{
    /// <summary>Every player ship that is not of the ship's own company.</summary>
    Everyone,

    /// <summary>Only ships of a company the ship's company is at war with.</summary>
    AtWar,

    /// <summary>Only ships that declared themselves a threat when hailed, or fired on it; the rest are asked.</summary>
    Interrogate,
}
