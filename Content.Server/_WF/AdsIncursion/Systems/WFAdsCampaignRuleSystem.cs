using Content.Server._WF.AdsIncursion.Components;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;

namespace Content.Server._WF.AdsIncursion.Systems;

/// <summary>Logs the ADS rule starting and ending; <see cref="WFAdsCampaignSystem.Idle"/> reads whether it is active.</summary>
public sealed class WFAdsCampaignRuleSystem : GameRuleSystem<WFAdsCampaignRuleComponent>
{
    protected override void Started(EntityUid uid, WFAdsCampaignRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        Log.Info("ADS incursion rule started: the campaign is on.");
    }

    protected override void Ended(Entity<WFAdsCampaignRuleComponent> rule, ref GameRuleEndedEvent args)
    {
        base.Ended(rule, ref args);
        Log.Info("ADS incursion rule ended: the campaign is off.");
    }
}
