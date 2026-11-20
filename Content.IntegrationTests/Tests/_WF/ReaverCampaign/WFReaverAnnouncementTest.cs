#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Shared._WF.SectorControl;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>The sector watch's three tiers: a chimed banner, a quiet banner held to one per window, and a radio line.</summary>
[TestOf(typeof(WFReaverCampaignSystem))]
public sealed class WFReaverAnnouncementTest : WFReaverTestBase
{
    private const string Founded = "Reavers have built a stronghold at";
    private const string Inbound = "Reaver reinforcements are inbound to";
    private const string ConvoyLaunched = "A Reaver convoy is under way to";

    /// <summary>Two cells beyond the home stronghold's ring, for convoys to head for.</summary>
    private (WFSectorCell, WFSectorCell) Targets()
    {
        var cells = WFSectorHex.Ring(Home, 3).OrderByDescending(cell => WFSectorHex.Distance(cell, WFSectorCell.Origin)).ToList();
        return (cells[0], cells[1]);
    }

    /// <summary>A founding chimes once and is a banner, with no radio line.</summary>
    [Test]
    public async Task FoundingIsOneChimedBanner()
    {
        await StartCampaign();
        // A tier change in the same tick would take the chime; this one is past.
        await Server.WaitAssertion(() => Server.System<WFReaverCampaignSystem>().SetTier(5));
        await RunTicks(5);

        var since = ChatCount();
        var chimes = 0;
        await Server.WaitAssertion(() =>
        {
            chimes = Chimes();
            FoundAt(Home);
            Assert.That(Chimes() - chimes, Is.EqualTo(1), "One chime.");
        });
        await RunTicks(30);
        Assert.That(Banners(since, Founded), Is.EqualTo(1));
        Assert.That(RadioLines(since, Founded), Is.Zero);
    }

    /// <summary>A reinforcement launch is a radio line: no banner and no chime.</summary>
    [Test]
    public async Task ReinforcementLaunchIsARadioLine()
    {
        await StartCampaign();
        EntityUid stronghold = default;
        await Server.WaitAssertion(() => stronghold = FoundAt(Home));
        await RunTicks(5);

        var since = ChatCount();
        var chimes = 0;
        await Server.WaitAssertion(() =>
        {
            chimes = Chimes();
            Assert.That(Server.System<WFReaverCampaignSystem>().TryReinforce(stronghold), Is.True);
            Assert.That(Chimes(), Is.EqualTo(chimes), "No chime.");
        });
        await RunTicks(30);
        Assert.That(Banners(since, Inbound), Is.Zero);
        Assert.That(RadioLines(since, Inbound), Is.EqualTo(1));
    }

    /// <summary>Two convoys inside the window: the first is a quiet banner, the second goes out on the radio.</summary>
    [Test]
    public async Task SecondConvoyInsideTheWindowGoesOnTheRadio()
    {
        await StartCampaign();
        await Server.WaitAssertion(() => FoundAt(Home));
        await RunTicks(5);

        var since = ChatCount();
        var chimes = 0;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var (first, second) = Targets();
            chimes = Chimes();
            Assert.That(campaign.TryLaunchConvoy(first, out _), Is.True);
            Assert.That(campaign.TryLaunchConvoy(second, out _), Is.True);
            Assert.That(Chimes(), Is.EqualTo(chimes), "Neither chimes.");
            Assert.That(campaign.State()!.LastQuietBanner, Is.EqualTo(STiming.CurTime));
        });
        await RunTicks(30);
        Assert.That(Banners(since, ConvoyLaunched), Is.EqualTo(1));
        Assert.That(RadioLines(since, ConvoyLaunched), Is.EqualTo(1));
    }

    /// <summary>A quiet banner after the window has passed is a banner again.</summary>
    [Test]
    public async Task QuietBannerAfterTheWindowIsABannerAgain()
    {
        await StartCampaign();
        await Server.WaitAssertion(() => FoundAt(Home));
        await RunTicks(5);

        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var (first, second) = Targets();
            Assert.That(campaign.TryLaunchConvoy(first, out _), Is.True);
            campaign.State()!.LastQuietBanner = STiming.CurTime - WFReaverCampaignSystem.QuietBannerInterval - System.TimeSpan.FromSeconds(1);
            Assert.That(campaign.TryLaunchConvoy(second, out _), Is.True);
        });
        await RunTicks(30);
        Assert.That(Banners(since, ConvoyLaunched), Is.EqualTo(2));
        Assert.That(RadioLines(since, ConvoyLaunched), Is.Zero);
    }
}
