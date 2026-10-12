using Content.Server._WF.ReaverCampaign.Systems;

namespace Content.Server._WF.ReaverCampaign.Components;

/// <summary>Game rule marker: while it runs, the Ashfall Reaver campaign is on.</summary>
[RegisterComponent, Access(typeof(WFReaverCampaignRuleSystem))]
public sealed partial class WFReaverCampaignRuleComponent : Component;
