#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.TradeGoods;
using Content.Shared._WF.TradeGoods;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>The Market PDA app builds every tab on the client from a server state, with live strings.</summary>
[TestFixture]
[TestOf(typeof(MarketUiFragment))]
public sealed class MarketAppTest
{
    [Test]
    public async Task MarketAppShowsPricesAlertsAndHold()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var station = new WFMarketStation { Name = "Halcyon", Faction = "Trans-Solarian Federation" };
            station.Prices.Add(new WFMarketPrice("medical supplies", 9500, 1.9f, "blockade", 1f, true));
            station.Prices.Add(new WFMarketPrice("metals", 2000, 0.5f, "glutted", 5f, false));
            station.Buys.Add(new WFMarketPrice("fabric", 1130, 1.13f, "scarce", 1f, false));
            var rival = new WFMarketStation { Name = "Camelot", Faction = "USSP" };
            rival.Prices.Add(new WFMarketPrice("medical supplies", 6000, 1f, "steady", 1f, false));
            var state = new WFMarketUiState();
            state.Stations.Add(station);
            state.Stations.Add(rival);
            var fence = new WFMarketStation { Name = "WF test fence", Underground = true, Faction = "The Underground" };
            fence.Prices.Add(new WFMarketPrice("medical supplies", 12000, 1f, "steady", 1f, false));
            state.Stations.Add(fence);
            state.Alerts.Add(new WFMarketAlert("Freighter Plutomkii inbound to Halcyon with medical supplies", null, false, WFMarketAlertKind.Traffic));
            state.Alerts.Add(new WFMarketAlert("RUSH Halcyon", System.TimeSpan.FromHours(1), true));

            var fragment = new MarketUiFragment(EntityUid.Invalid);
            fragment.UpdatePublicState(state);
            var texts = Texts(fragment).ToList();

            Assert.That(texts, Has.Some.Contains("Halcyon"));
            Assert.That(texts, Has.Some.EqualTo("9500"));
            Assert.That(texts, Has.Some.Contains("9500/t"), "Prices should show the per-tonne figure.");
            Assert.That(texts, Has.Some.EqualTo("★"), "The best price for a good in the sector should be starred.");
            Assert.That(texts, Has.Some.Contains("best for 1"));
            Assert.That(texts, Has.Some.EqualTo("UNDERGROUND UPLINK"), "Hidden markets should get their own tab.");
            Assert.That(texts, Has.Some.EqualTo("TRANS-SOLARIAN FEDERATION"), "Stations should be grouped under their faction.");
            Assert.That(texts, Has.Some.EqualTo("USSP"));
            Assert.That(texts, Has.Some.EqualTo("THE UNDERGROUND"));
            Assert.That(texts, Has.Some.EqualTo("WF test fence"));
            Assert.That(texts.Count(text => text == "★"), Is.EqualTo(1), "Stars rank each section on its own.");
            Assert.That(texts, Has.Some.StartsWith("2 markets open"), "The open-market count should leave hidden markets out.");
            Assert.That(texts, Has.Some.Contains("Plutomkii"));
            Assert.That(texts, Has.Some.EqualTo("TRAFFIC"), "Alerts should be tagged by kind.");
            Assert.That(texts, Has.Some.Contains("RUSH Halcyon"));
            Assert.That(texts, Has.Some.EndsWith("min left"), "A timed alert should show its countdown.");
            Assert.That(texts.Any(text => text.Contains("Hold") || text.Contains("aboard")), Is.False, "The app shows no ship information.");
            Assert.That(texts, Has.Some.EqualTo("SELL HERE"), "A card with both lists should label them.");
            Assert.That(texts, Has.Some.EqualTo("BUY HERE"), "Vendor prices should show on the station's card.");
            Assert.That(texts, Has.Some.EqualTo("1130"));
            Assert.That(texts, Has.Some.EqualTo("scarce"));
            Assert.That(texts.Any(text => text.Contains('{') || text.Contains("wf-market")), Is.False, "Every string should resolve.");

            // An empty state says so on every tab.
            fragment.UpdatePublicState(new WFMarketUiState());
            texts = Texts(fragment).ToList();
            Assert.That(texts, Has.Some.Contains("No stations"));
            Assert.That(texts, Has.Some.Contains("Nothing happening"));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every label's and rich-text label's text under a control.</summary>
    private static IEnumerable<string> Texts(Control control)
    {
        if (control is Label { Text: { } text })
            yield return text;

        if (control is RichTextLabel rich && rich.GetMessage() is { } message)
            yield return message;

        foreach (var child in control.Children)
        {
            foreach (var nested in Texts(child))
            {
                yield return nested;
            }
        }
    }
}
