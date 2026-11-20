#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.AdsIncursion.Components;
using Content.Server._WF.AdsIncursion.Systems;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Server.GameTicking;
using Content.Shared._WF.AdsIncursion;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.AdsIncursion;

/// <summary>The tier 5 dreadnought: it activates after the delay at Overrun, hunts the Reavers' strongholds, holds by a node with none standing, and finishes the ADS when destroyed.</summary>
[TestOf(typeof(WFAdsCampaignSystem))]
public sealed class WFAdsDreadnoughtTest : WFAdsTestBase
{
    /// <summary>Lifts the hourly payout cap for the bounty checks, so earlier tests on a pooled pair cannot cut them short.</summary>
    [SetUp]
    public async Task LiftPayoutCap()
    {
        await Server.WaitPost(() => Server.ResolveDependency<IConfigurationManager>().SetCVar(EncountersCVars.PayoutHourlyCap, int.MaxValue));
    }

    [TearDown]
    public async Task RestorePayoutCap()
    {
        await Server.WaitPost(() => Server.ResolveDependency<IConfigurationManager>()
            .SetCVar(EncountersCVars.PayoutHourlyCap, EncountersCVars.PayoutHourlyCap.DefaultValue));
    }

    /// <summary>A Reaver stronghold nearer the ADS home than <see cref="WFReaverTestBase.Home"/> is.</summary>
    private static WFSectorCell NearHome => FarCell(AdsHome, 3);

    /// <summary>Has one ship land a ship weapon hit on another, as a gun does.</summary>
    private void Hit(EntityUid grid, EntityUid attacker)
    {
        var hit = new WFCrewHullHitEvent(grid, attacker);
        SEntMan.EventBus.RaiseLocalEvent(grid, ref hit, true);
    }

    /// <summary>The lead hull of a stronghold.</summary>
    private EntityUid HubOf(EntityUid stronghold)
    {
        return LeadOf(stronghold);
    }

    /// <summary>Asserts a dreadnought's orders close on a hub and attack it.</summary>
    private void AssertStrikes(EntityUid dreadnought, EntityUid stronghold)
    {
        var hub = HubOf(stronghold);
        var orders = Orders(dreadnought);
        Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[] { WFCrewObjectiveKind.GoTo, WFCrewObjectiveKind.Attack }));
        Assert.That(Vector2.Distance(orders[0].Position, Server.System<SharedTransformSystem>().GetWorldPosition(hub)), Is.LessThan(1f));
        Assert.That(orders[0].Range, Is.EqualTo(800f));
        Assert.That(orders[1].Target, Is.EqualTo(SEntMan.GetNetEntity(hub)), "It attacks the hub's lead hull.");
        Assert.That(SEntMan.GetComponent<WFAdsDreadnoughtComponent>(dreadnought).Target, Is.EqualTo(stronghold));
    }

    /// <summary>Below tier 5 no dreadnought activates however long it waits, and at tier 5 none does before the delay.</summary>
    [Test]
    public async Task NoDreadnoughtBelowOverrunOrBeforeTheDelay()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            FoundNode(AdsHome);
            FoundAt(Home);
            Server.System<WFAdsCampaignSystem>().SetTier(4);
        });

        await RunTicks(300);
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            Assert.That(ads.Dreadnoughts(), Is.Empty, "No dreadnought below Overrun.");
            Assert.That(AdsState().OverrunSince, Is.Null);

            ads.SetTier(WFAdsCampaignSystem.MaxTier);
            Assert.That(AdsState().OverrunSince, Is.EqualTo(STiming.CurTime), "The delay runs from reaching the tier.");
        });

        await RunTicks(60);
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            Assert.That(ads.Dreadnoughts(), Is.Empty, "Not before the delay.");

            ads.SetTier(4);
            Assert.That(AdsState().OverrunSince, Is.Null, "Dropping a tier resets the delay.");
        });
    }

    /// <summary>At tier 5 past the delay the dreadnought activates beside a node, is announced and goes for the nearest Reaver stronghold.</summary>
    [Test]
    public async Task DreadnoughtActivatesAtOverrunAgainstTheNearestStronghold()
    {
        await StartAds();
        var since = ChatCount();
        EntityUid node = default, near = default;
        await Server.WaitAssertion(() =>
        {
            node = FoundNode(AdsHome);
            FoundAt(Home);
            near = FoundAt(NearHome);
            Server.System<WFAdsCampaignSystem>().SetTier(WFAdsCampaignSystem.MaxTier);
        });

        await WaitUntilServer(() => Server.System<WFAdsCampaignSystem>().Dreadnoughts().Count > 0, 600);
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            var (uid, comp, encounter) = ads.Dreadnoughts().Single();
            Assert.That(encounter.Prototype.Id, Is.EqualTo("WFTestAdsDreadnought"));
            Assert.That(comp.Source, Is.EqualTo(node));
            var nodeAt = Server.System<SharedTransformSystem>().GetWorldPosition(LeadOf(node));
            Assert.That(Vector2.Distance(Server.System<SharedTransformSystem>().GetWorldPosition(LeadOf(uid)), nodeAt), Is.LessThanOrEqualTo(SpawnReach),
                "It activates beside the node.");
            AssertStrikes(uid, near);
            Assert.That(AdsState().Dreadnoughts, Is.EqualTo(1));
            Assert.That(ads.TryLaunchDreadnought(out _), Is.False, "One dreadnought at a time.");

            var score = AdsState().Score;
            SEntMan.RemoveComponent<WFAdsDreadnoughtComponent>(uid);
            ads.Recompute();
            Assert.That(score - AdsState().Score, Is.EqualTo(10f).Within(0.01f), "A dreadnought out adds ten to the score.");
            SEntMan.AddComponent<WFAdsDreadnoughtComponent>(uid);
        });

        await RunTicks(30);
        var lines = ChatSince(since);
        Assert.That(lines.Any(line => line.Contains("DIRECTIVE UPDATE. Dreadnought-class asset activated at")
            && line.Contains("Hostile strongholds will be neutralised.")), Is.True, "The activation is announced.");
    }

    /// <summary>
    /// Activated by hand, it chimes with the capital sound on top; when its stronghold breaks it retargets the next,
    /// with none standing it holds by a node, and it looks again only once a minute.
    /// </summary>
    [Test]
    public async Task DreadnoughtRetargetsAndHoldsByANode()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            var reavers = Server.System<WFReaverCampaignSystem>();
            var node = FoundNode(AdsHome);
            var far = FoundAt(Home);
            var near = FoundAt(NearHome);

            var chimes = Chimes();
            Assert.That(ads.TryLaunchDreadnought(out var dreadnought), Is.True, "By hand it ignores the tier.");
            Assert.That(Chimes() - chimes, Is.EqualTo(2), "The activation chimes and plays the capital sound.");
            AssertStrikes(dreadnought, near);

            // Its fire on the hub makes it one of the hub's attackers; the hub breaks with it among them.
            Hit(HubOf(near), LeadOf(dreadnought));
            Assert.That(SEntMan.GetComponent<WFReaverStrongholdComponent>(near).NpcHits.Keys, Does.Contain(LeadOf(dreadnought)));
            KillCrew(near, "lead");
            reavers.CheckStrongholds();
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(near).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));
            Assert.That(AdsState().StrongholdsBroken, Is.EqualTo(1), "The broken stronghold is counted for the dreadnought.");

            ads.CheckDreadnoughts();
            AssertStrikes(dreadnought, far);

            // No hit from it: the next one broken is not counted for it.
            KillCrew(far, "lead");
            reavers.CheckStrongholds();
            Assert.That(AdsState().StrongholdsBroken, Is.EqualTo(1));
            ads.CheckDreadnoughts();
            var comp = SEntMan.GetComponent<WFAdsDreadnoughtComponent>(dreadnought);
            Assert.That(comp.Target, Is.Null);
            Assert.That(comp.Station, Is.EqualTo(node));
            var orders = Orders(dreadnought);
            Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[] { WFCrewObjectiveKind.GoTo, WFCrewObjectiveKind.Hold }));
            Assert.That(Vector2.Distance(orders[0].Position, Server.System<SharedTransformSystem>().GetWorldPosition(LeadOf(node))), Is.LessThan(1f));
            Assert.That(orders[0].Range, Is.EqualTo(1000f));

            // A stronghold founded now is not seen until the minute is up.
            var fresh = FoundAt(FarCell(Home, 3));
            ads.CheckDreadnoughts();
            Assert.That(comp.Target, Is.Null, "It looks again only once a minute.");
            comp.NextLook = STiming.CurTime;
            ads.CheckDreadnoughts();
            AssertStrikes(dreadnought, fresh);
        });
    }

    /// <summary>
    /// A Reaver hub hit by the dreadnought answers it as it answers a player: its crew takes it for an attacker, with no
    /// reinforcement docked a sortie pack goes after it, and a docked reinforcement breaks away to attack it.
    /// </summary>
    [Test]
    public async Task ReaverHubDefendsAgainstTheDreadnought()
    {
        await StartAds(reavers: true);
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            var reavers = Server.System<WFReaverCampaignSystem>();
            var transform = Server.System<SharedTransformSystem>();
            Assert.That(reavers.Running, Is.True, "The Reavers run beside the ADS.");
            FoundNode(AdsHome);
            var stronghold = FoundAt(Home);
            Assert.That(ads.TryLaunchDreadnought(out var dreadnought), Is.True);
            var hub = HubOf(stronghold);
            var hubAt = transform.GetWorldPosition(hub);
            var attacker = LeadOf(dreadnought);
            Place(attacker, hubAt + new Vector2(0f, 1500f));

            Hit(hub, attacker);
            reavers.CheckStrongholds();
            var home = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            Assert.That(home.Hits, Is.EqualTo(1), "The dreadnought's hit counts.");
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(stronghold).ShipHits, Is.Empty, "It is no player hit: nothing is paid for it.");
            Assert.That(Server.System<WFCrewAlertSystem>().IsAttacker(hub, attacker), Is.True, "The hub's crew takes it for an attacker and fires back.");
            Assert.That(SEntMan.HasComponent<WFReaverMarkComponent>(attacker), Is.False, "Only a player ship is marked.");
            var sortie = reavers.Patrols().Single(patrol => patrol.Comp1.Sortie);
            Assert.That(sortie.Comp1.Intercepting, Is.EqualTo(attacker), "With no reinforcement docked, a sortie pack goes after it.");
            Assert.That(home.Sortie, Is.EqualTo(sortie.Owner));

            // A reinforcement docked at the hub breaks away at the next hit.
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>(Reinforcement);
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(hubAt + new Vector2(0f, -900f), MapData.MapId),
                out var uid, offBudget: true), Is.True);
            var reinforcement = SEntMan.AddComponent<WFReaverReinforcementComponent>(uid);
            reinforcement.Stronghold = stronghold;
            reinforcement.Lead = "lead";
            reinforcement.Level = 1;
            reinforcement.Phase = WFReaverReinforcementPhase.Docked;
            reinforcement.Landed = true;
            home.Reinforcements.Add(uid);

            Hit(hub, attacker);
            reavers.CheckStrongholds();
            Assert.That(home.Hits, Is.EqualTo(2));
            Assert.That(reinforcement.Phase, Is.EqualTo(WFReaverReinforcementPhase.Sortie));
            Assert.That(reinforcement.Target, Is.EqualTo(attacker));
            Assert.That(home.Scattered, Is.True);
            var orders = Orders(uid);
            Assert.That(orders.Select(order => order.Kind), Is.EqualTo(new[] { WFCrewObjectiveKind.Undock, WFCrewObjectiveKind.Attack }));
            Assert.That(orders[1].Target, Is.EqualTo(SEntMan.GetNetEntity(attacker)), "The reinforcement attacks the dreadnought.");
        });
    }

    /// <summary>
    /// Destroying the dreadnought pays its bounty, is announced as the ending and finishes the ADS: nothing is founded,
    /// launched or sent again however long it waits, while a node still standing fights on and pays when broken.
    /// </summary>
    [Test]
    public async Task DestroyedDreadnoughtPaysAndFinishesTheAds()
    {
        await StartAds();
        var since = ChatCount();
        EntityUid node = default, ship = default;
        var before = 0;
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            ship = SpawnGrid(CentreOf(AdsHome) + new Vector2(0f, 7000f), "Test Gunship");
            Board(ship);
            node = FoundNode(AdsHome);
            for (var i = 0; i < 6; i++)
            {
                ads.TrySpreadOnce();
            }

            Assert.That(ads.TryLaunchDreadnought(out var dreadnought), Is.True);
            ShootAt(dreadnought, "lead", ship);
            before = Balance();
            KillCrew(dreadnought, "lead");
            ads.CheckDreadnoughts();

            var state = AdsState();
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(dreadnought).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));
            Assert.That(state.DreadnoughtsDestroyed, Is.EqualTo(1));
            Assert.That(state.Finished, Is.True);
            Assert.That(Server.System<WFSectorSyncSystem>().BuildStatus().Lines.Single(line => line.Line.Contains("Automated Defense System")).Line,
                Does.Contain(": finished,"), "The legend reads finished.");
            AssertRefuses(node);

            // The tier follows the score but a rise is not announced again.
            var announced = state.TierAnnounced;
            state.Tier = 0;
            ads.SetTier(WFAdsCampaignSystem.MaxTier);
            Assert.That(state.Tier, Is.EqualTo(WFAdsCampaignSystem.MaxTier));
            Assert.That(state.TierAnnounced, Is.EqualTo(announced), "No rise is announced once finished.");

            var summary = new RoundEndTextAppendEvent();
            SEntMan.EventBus.RaiseEvent(EventSource.Local, summary);
            Assert.That(summary.Text, Does.Contain("The ADS went dormant for good when its dreadnought was destroyed."));
            Assert.That(summary.Text, Does.Contain("The ADS activated 1 dreadnought: 1 destroyed"));
        });

        // Past the dreadnought delay and every other clock the test campaign keeps short, nothing comes back.
        await RunTicks(300);
        var expected = 0;
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            Assert.That(Balance() - before, Is.EqualTo(DreadnoughtBounty), "The bounty is paid to those who hit it.");
            Assert.That(AdsState().Finished, Is.True);
            Assert.That(ads.Dreadnoughts(), Is.Empty);
            Assert.That(ads.Deployments(), Is.Empty);
            Assert.That(ads.Sentinels(), Is.Empty);
            Assert.That(ads.Sweeps(), Is.Empty);
            Assert.That(ads.Interdictors(), Is.Empty);
            AssertRefuses(node);

            // The standing node fights on and still breaks and pays.
            var comp = SEntMan.GetComponent<WFAdsNodeComponent>(node);
            expected = NodeBounties[Math.Max(comp.Tier, AdsState().Tier)];
            before = Balance();
            ShootAt(node, "lead", ship);
            KillCrew(node, "lead");
            ads.CheckNodes();
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(node).Resolution, Is.EqualTo(WFEncounterResolution.Destroyed));
            Assert.That(AdsState().Broken, Is.EqualTo(1));
            Assert.That(AdsState().Defeated, Is.False, "A finished campaign does not go dormant as well.");
        });

        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Balance() - before, Is.EqualTo(expected), "The last node pays its own bounty.");
            Assert.That(Server.System<WFAdsCampaignSystem>().Nodes(), Is.Empty, "No regroup once finished.");
            Assert.That(Server.System<WFAdsCampaignSystem>().TryFound(out _), Is.False);
        });

        var lines = ChatSince(since);
        Assert.That(lines.Any(line => line.Contains("The ADS dreadnought is destroyed at ")
            && line.Contains($"Bounty {DreadnoughtBounty} thalers paid. The Automated Defense System has gone dormant for good.")), Is.True,
            "The destruction is announced as the ending.");
        Assert.That(lines.Any(line => line.Contains("The Automated Defense System has gone dormant. Its nodes are gone")), Is.False, "No second ending.");
    }

    /// <summary>Asserts every launch and founding the campaign has is refused.</summary>
    private void AssertRefuses(EntityUid node)
    {
        var ads = Server.System<WFAdsCampaignSystem>();
        Assert.That(ads.TryFound(out _), Is.False, "No founding once finished.");
        Assert.That(ads.TryFoundAt(new MapCoordinates(CentreOf(FarCell(AdsHome, 3)), MapData.MapId), out _), Is.False);
        Assert.That(ads.TryLaunchDeployment(out _), Is.False, "No deployment once finished.");
        Assert.That(ads.TryLaunchDeployment(FarCell(AdsHome, 1), out _), Is.False);
        Assert.That(ads.TrySpreadOnce(), Is.False, "No spread once finished.");
        Assert.That(ads.TryLaunchSweep(node), Is.False, "No sweep once finished.");
        Assert.That(ads.TrySentinel(node), Is.False, "No sentinel once finished.");
        Assert.That(ads.TryChallenge(out _), Is.False, "No challenge once finished.");
        Assert.That(ads.TryChallenge(Home, out _), Is.False);
        Assert.That(ads.TryLaunchDreadnought(out _), Is.False, "No dreadnought once finished.");
    }

    /// <summary>One dreadnought at a time; one ended by an admin pays and finishes nothing, and another may follow.</summary>
    [Test]
    public async Task EndedDreadnoughtPaysNothing()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            FoundNode(AdsHome);
            Assert.That(ads.TryLaunchDreadnought(out var first), Is.True);
            Assert.That(ads.TryLaunchDreadnought(out _), Is.False, "One at a time.");

            Server.System<WFEncounterSystem>().End(first);
            Assert.That(AdsState().Finished, Is.False);
            Assert.That(AdsState().DreadnoughtsDestroyed, Is.Zero);
            Assert.That(ads.TryLaunchDreadnought(out var second), Is.True, "Another may follow one ended.");
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(AdsState().Dreadnoughts, Is.EqualTo(2));
        });
    }

    /// <summary>Every real campaign activates a dreadnought from a well-formed encounter on the Wyvern, and its numbers are sane.</summary>
    [Test]
    public async Task EveryCampaignHasADreadnought()
    {
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            foreach (var campaign in prototypes.EnumeratePrototypes<WFAdsCampaignPrototype>().Where(entry => !entry.ID.StartsWith("WFTest")))
            {
                var id = campaign.ID;
                Assert.That(campaign.Dreadnought, Is.Not.Null, $"{id} activates a dreadnought");
                Assert.That(campaign.DreadnoughtDelay, Is.GreaterThan(TimeSpan.Zero), id);
                Assert.That(campaign.DreadnoughtBounty, Is.GreaterThan(0), id);
                Assert.That(campaign.CapitalSound, Is.Not.Null, id);

                var dreadnoughtId = campaign.Dreadnought!.Value;
                var dreadnought = prototypes.Index(dreadnoughtId);
                Assert.That(dreadnought.Start, Is.EqualTo(WFEncounterStart.Manual), $"{dreadnoughtId}: only the campaign starts it");
                Assert.That(dreadnought.Lifetime, Is.EqualTo(WFEncounterLifetime.Persistent), $"{dreadnoughtId}");
                Assert.That(dreadnought.Ships, Has.Count.EqualTo(1), $"{dreadnoughtId}: one hull");
                var lead = dreadnought.Ships[0];
                Assert.That(lead.Key, Is.EqualTo("lead"), $"{dreadnoughtId}");
                Assert.That(lead.Evades, Is.False, $"{dreadnoughtId}");
                Assert.That(lead.Platform, Is.False, $"{dreadnoughtId}: a ship under way");
                Assert.That(lead.ZoneTargets, Is.EqualTo(WFEncounterZoneTargets.Interrogate), $"{dreadnoughtId}: players only by the interrogation rules");
                Assert.That(lead.WarnRange, Is.GreaterThan(lead.AttackRange), $"{dreadnoughtId}");
                Assert.That(lead.Vessels, Does.Contain(new ProtoId<Content.Shared._NF.Shipyard.Prototypes.VesselPrototype>("WFAdsWyvern")), $"{dreadnoughtId}");
                foreach (var vessel in lead.Vessels)
                {
                    Assert.That(prototypes.HasIndex(vessel), Is.True, $"{dreadnoughtId}: {vessel}");
                }
            }
        });
    }
}
