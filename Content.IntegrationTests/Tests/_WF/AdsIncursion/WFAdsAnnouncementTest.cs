#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WF.AdsIncursion.Systems;
using Content.Shared._WF.SectorControl;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.AdsIncursion;

/// <summary>The sector watch's three tiers: a chimed banner, a quiet banner held to one per window, and a radio line.</summary>
[TestOf(typeof(WFAdsCampaignSystem))]
public sealed class WFAdsAnnouncementTest : WFAdsTestBase
{
    private const string Founded = "An Automated Defense System node has come online at";
    private const string Joined = "An ADS sentinel has joined the node at";
    private const string DeploymentLaunched = "An ADS deployment is under way to";

    /// <summary>Two cells beyond the node's ring, for deployments to head for.</summary>
    private (WFSectorCell, WFSectorCell) Targets()
    {
        var cells = WFSectorHex.Ring(AdsHome, 3).OrderByDescending(cell => WFSectorHex.Distance(cell, WFSectorCell.Origin)).ToList();
        return (cells[0], cells[1]);
    }

    /// <summary>A founding chimes once and is a banner, with no radio line.</summary>
    [Test]
    public async Task FoundingIsOneChimedBanner()
    {
        await StartAds();
        // A tier change in the same tick would take the chime; this one is past.
        await Server.WaitAssertion(() => Server.System<WFAdsCampaignSystem>().SetTier(5));
        await RunTicks(5);

        var since = ChatCount();
        var chimes = 0;
        await Server.WaitAssertion(() =>
        {
            chimes = Chimes();
            FoundNode(AdsHome);
            Assert.That(Chimes() - chimes, Is.EqualTo(1), "One chime.");
        });
        await RunTicks(30);
        Assert.That(Banners(since, Founded), Is.EqualTo(1));
        Assert.That(RadioLines(since, Founded), Is.Zero);
    }

    /// <summary>A sentinel joining its node is a radio line: no banner and no chime.</summary>
    [Test]
    public async Task SentinelIsARadioLine()
    {
        await StartAds();
        EntityUid node = default;
        await Server.WaitAssertion(() => node = FoundNode(AdsHome));
        await RunTicks(5);

        var since = ChatCount();
        var chimes = 0;
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            chimes = Chimes();
            Assert.That(ads.TrySentinel(node), Is.True);
            Assert.That(ads.Join(ads.Sentinels().Single()), Is.True);
            Assert.That(Chimes(), Is.EqualTo(chimes), "No chime.");
        });
        await RunTicks(30);
        Assert.That(Banners(since, Joined), Is.Zero);
        Assert.That(RadioLines(since, Joined), Is.EqualTo(1));
    }

    /// <summary>Two deployments inside the window: the first is a quiet banner, the second goes out on the radio.</summary>
    [Test]
    public async Task SecondDeploymentInsideTheWindowGoesOnTheRadio()
    {
        await StartAds();
        await Server.WaitAssertion(() => FoundNode(AdsHome));
        await RunTicks(5);

        var since = ChatCount();
        var chimes = 0;
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            var (first, second) = Targets();
            chimes = Chimes();
            Assert.That(ads.TryLaunchDeployment(first, out _), Is.True);
            Assert.That(ads.TryLaunchDeployment(second, out _), Is.True);
            Assert.That(Chimes(), Is.EqualTo(chimes), "Neither chimes.");
            Assert.That(ads.State()!.LastQuietBanner, Is.EqualTo(STiming.CurTime));
        });
        await RunTicks(30);
        Assert.That(Banners(since, DeploymentLaunched), Is.EqualTo(1));
        Assert.That(RadioLines(since, DeploymentLaunched), Is.EqualTo(1));
    }

    /// <summary>A quiet banner after the window has passed is a banner again.</summary>
    [Test]
    public async Task QuietBannerAfterTheWindowIsABannerAgain()
    {
        await StartAds();
        await Server.WaitAssertion(() => FoundNode(AdsHome));
        await RunTicks(5);

        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            var (first, second) = Targets();
            Assert.That(ads.TryLaunchDeployment(first, out _), Is.True);
            ads.State()!.LastQuietBanner = STiming.CurTime - WFAdsCampaignSystem.QuietBannerInterval - TimeSpan.FromSeconds(1);
            Assert.That(ads.TryLaunchDeployment(second, out _), Is.True);
        });
        await RunTicks(30);
        Assert.That(Banners(since, DeploymentLaunched), Is.EqualTo(2));
        Assert.That(RadioLines(since, DeploymentLaunched), Is.Zero);
    }
}
