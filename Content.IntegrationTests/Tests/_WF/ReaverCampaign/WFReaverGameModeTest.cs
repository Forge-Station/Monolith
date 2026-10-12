#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Presets;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.ReaverCampaign;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>The Ashfall game mode: its preset, and the campaign following its rule.</summary>
[TestOf(typeof(WFReaverCampaignRuleSystem))]
public sealed class WFReaverGameModeTest : WFReaverTestBase
{
    /// <summary>The preset is votable and is Standard's rules plus the Ashfall rule.</summary>
    [Test]
    public async Task AshfallPresetIsStandardPlusTheRule()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var protos = Server.ResolveDependency<IPrototypeManager>();
            var ashfall = protos.Index<GamePresetPrototype>("MonoAshfall");
            var standard = protos.Index<GamePresetPrototype>("MonoStandard");
            Assert.That(ashfall.ShowInVote, Is.True);
            Assert.That(ashfall.Rules, Is.SupersetOf(standard.Rules));
            Assert.That(ashfall.Rules, Does.Contain("WFReaverCampaignRule"));
            Assert.That(ashfall.Rules.Count, Is.EqualTo(standard.Rules.Count + 1));
        });
    }

    /// <summary>With the cvar at its default the campaign idles; the rule running turns it on.</summary>
    [Test]
    public async Task CampaignFollowsTheRule()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var config = Server.ResolveDependency<IConfigurationManager>();
            var ticker = Server.System<GameTicker>();
            var campaign = Server.System<WFReaverCampaignSystem>();
            config.SetCVar(ReaverCampaignCVars.Enabled, false);
            config.SetCVar(EncountersCVars.Enabled, true);

            Assert.That(campaign.Idle(), Is.EqualTo(WFReaverIdle.CampaignOff));
            Assert.That(campaign.Running, Is.False);

            ticker.StartGameRule("WFReaverCampaignRule", out var rule);
            Assert.That(ticker.IsGameRuleActive<WFReaverCampaignRuleComponent>(), Is.True);
            Assert.That(campaign.Idle(), Is.Not.EqualTo(WFReaverIdle.CampaignOff));
            Assert.That(campaign.Running, Is.True);

            ticker.EndGameRule(rule);
            Assert.That(campaign.Idle(), Is.EqualTo(WFReaverIdle.CampaignOff));
        });
    }
}
