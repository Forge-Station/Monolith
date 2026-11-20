using Robust.Shared.Prototypes;

namespace Content.Shared._WF.NpcCrew;

/// <summary>A reusable navigation configuration for crew scenarios.</summary>
[Prototype("wfCrewNavigation")]
public sealed partial class WFCrewNavigationProfilePrototype : IPrototype
{
    /// <summary>Scenario profile identifier.</summary>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Localized name shown in the setup profile picker.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Defaults copied into each mission using this profile.</summary>
    [DataField]
    public WFCrewNavigationSettings Settings = new();
}
