using Content.Server._WF.ReaverCampaign.Components;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;

namespace Content.Server._WF.ReaverCampaign.Systems;

/// <summary>Logs the Ashfall rule starting and ending; <see cref="WFReaverCampaignSystem.Idle"/> reads whether it is active.</summary>
public sealed class WFReaverCampaignRuleSystem : GameRuleSystem<WFReaverCampaignRuleComponent>
{
    protected override void Started(EntityUid uid, WFReaverCampaignRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        Log.Info("Ashfall game mode started: the Reaver campaign is on.");
    }

    protected override void Ended(EntityUid uid, WFReaverCampaignRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);
        Log.Info("Ashfall game mode ended: the Reaver campaign is off.");
    }
}
