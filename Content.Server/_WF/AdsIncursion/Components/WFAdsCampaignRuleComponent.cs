using Content.Server._WF.AdsIncursion.Systems;

namespace Content.Server._WF.AdsIncursion.Components;

/// <summary>Game rule marker: while it runs, the ADS incursion is on.</summary>
[RegisterComponent, Access(typeof(WFAdsCampaignRuleSystem))]
public sealed partial class WFAdsCampaignRuleComponent : Component;
