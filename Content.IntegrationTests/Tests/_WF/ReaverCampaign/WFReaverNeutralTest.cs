#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Server._Mono.Company;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Components;
using Content.Shared._Mono.Company;
using Content.Shared._WF.SectorControl;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Station.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>The Underground is neutral to the Reavers: its ships, crews and station are never warned, hunted, marked or disputed over.</summary>
[TestOf(typeof(WFSectorActivitySystem))]
public sealed class WFReaverNeutralTest : WFReaverTestBase
{
    private const string Underground = "WFUnderground";

    private static readonly WFSectorCell Far = new(4, -2);

    [TearDown]
    public async Task ResetTerritory()
    {
        await Server.WaitPost(() =>
        {
            Server.ResolveDependency<IConfigurationManager>().SetCVar(SectorControlCVars.RetakeMinutes, SectorControlCVars.RetakeMinutes.DefaultValue);
            Server.System<WFSectorTerritorySystem>().Clear(MapData.MapId);
            Disembark();
        });
    }

    private void Fly(EntityUid uid, string company)
    {
        SEntMan.EnsureComponent<CompanyComponent>(uid).CompanyName = company;
    }

    /// <summary>A station whose BecomesStation id is Underground, as the black market's map gives its grid.</summary>
    private EntityUid SpawnUndergroundStation(Vector2 position)
    {
        var station = SpawnStation(position, "Test Black Market");
        var comp = SEntMan.AddComponent<BecomesStationComponent>(station);
        typeof(BecomesStationComponent).GetField(nameof(BecomesStationComponent.Id))!.SetValue(comp, "Underground");
        return station;
    }

    /// <summary>A ship of the Underground, a ship docked to its station and a crew of its people are neutral; anyone else is not.</summary>
    [Test]
    public async Task NeutralityFollowsCompanyStationAndCrew()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var activity = Server.System<WFSectorActivitySystem>();
            var plain = SpawnGrid(CentreOf(Far), "Test Hauler");
            Assert.That(activity.IsNeutralTo(Reavers, plain), Is.False);

            Fly(plain, Underground);
            Assert.That(activity.IsNeutralTo(Reavers, plain), Is.True, "Its flag.");
            Assert.That(activity.IsNeutralTo(Holder, plain), Is.False, "Only to a faction that names it.");
            SEntMan.RemoveComponent<CompanyComponent>(plain);

            Board(plain);
            Assert.That(activity.IsNeutralTo(Reavers, plain), Is.False, "A crew of no company.");
            Fly(SEntMan.GetEntity(Player), Underground);
            Assert.That(activity.IsNeutralTo(Reavers, plain), Is.True, "A crew of the Underground, with no flag of its own.");
            Fly(plain, "WFAshfallReavers");
            Assert.That(activity.IsNeutralTo(Reavers, plain), Is.False, "Another flag wins.");
            SEntMan.RemoveComponent<CompanyComponent>(plain);
            Disembark();
            SEntMan.RemoveComponent<CompanyComponent>(SEntMan.GetEntity(Player));

            var station = SpawnUndergroundStation(CentreOf(Far.Neighbour(1)));
            var other = SpawnStation(CentreOf(Far.Neighbour(2)), "Test Outpost");
            Assert.That(activity.IsNeutralStation(Reavers, station), Is.True, "By station id.");
            Assert.That(activity.IsNeutralStation(Reavers, other), Is.False);

            var ship = SpawnStation(CentreOf(Far.Neighbour(3)), "Test Smuggler");
            SEntMan.RemoveComponent<StationMemberComponent>(ship);
            Assert.That(activity.IsNeutralTo(Reavers, ship), Is.False);
            var port = SEntMan.EntityQuery<DockingComponent, TransformComponent>().First(entry => entry.Item2.GridUid == station).Item1;
            var own = SEntMan.EntityQuery<DockingComponent, TransformComponent>().First(entry => entry.Item2.GridUid == ship).Item1;
            own.DockedWith = port.Owner;
            Assert.That(activity.IsNeutralTo(Reavers, ship), Is.True, "Docked to its station.");
            own.DockedWith = null;

            var skiff = SpawnStation(CentreOf(Far.Neighbour(4)), "Test Skiff");
            SEntMan.RemoveComponent<StationMemberComponent>(skiff);
            Fly(skiff, Underground);
            var theirs = SEntMan.EntityQuery<DockingComponent, TransformComponent>().First(entry => entry.Item2.GridUid == skiff).Item1;
            own.DockedWith = theirs.Owner;
            Assert.That(activity.IsNeutralTo(Reavers, skiff), Is.True, "The skiff flies the flag.");
            Assert.That(activity.IsNeutralTo(Reavers, ship), Is.False, "A ship berthed beside a neutral ship, not a neutral station, is not neutral.");
            own.DockedWith = null;
        });
    }

    /// <summary>A neutral ship in a cell Reaver ships sit in disputes nothing, and one sitting in a Reaver cell starts no retake; the same ship under another flag does both.</summary>
    [Test]
    public async Task NeutralShipsDoNotDisputeOrRetake()
    {
        await StartRound();
        await Server.WaitPost(() => Server.ResolveDependency<IConfigurationManager>().SetCVar(SectorControlCVars.RetakeMinutes, 0.05f));
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            Assert.That(territory.TryClaim(map, Far, Reavers, null), Is.True);
            territory.SetPresence(Reavers, map, new[] { Far });
            var ship = SpawnGrid(CentreOf(Far) + new Vector2(500f, 0f), "Test Smuggler");
            Fly(ship, Underground);
            Board(ship);

            territory.CheckDisputes(map);
            territory.CheckRetakes(map);
            Assert.That(territory.IsDisputed(map, Far), Is.False, "A neutral ship is no trespasser.");
            Assert.That(territory.RetakeOf(map, Far), Is.Null, "Nor does it retake.");

            Fly(ship, "None");
            territory.CheckDisputes(map);
            territory.CheckRetakes(map);
            Assert.That(territory.IsDisputed(map, Far), Is.True);
            Assert.That(territory.RetakeOf(map, Far), Is.Null, "Disputed with Reaver ships there, so no retake yet.");
            territory.SetPresence(Reavers, map, Array.Empty<WFSectorCell>());
            territory.CheckRetakes(map);
            Assert.That(territory.RetakeOf(map, Far), Is.Not.Null, "Anyone else retakes it.");
        });
    }

    /// <summary>The black market's cell is claimed like open space, by founding or by spread, and is not cut off from traders.</summary>
    [Test]
    public async Task UndergroundCellIsClaimedWithoutAContest()
    {
        await StartCampaign();
        var ring = WFSectorHex.Ring(Home, 2);
        var near = Home.Neighbour(1);
        var spread = ring[0];
        EntityUid station = default, far = default;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var activity = Server.System<WFSectorActivitySystem>();
            var map = MapData.MapId;
            station = SpawnUndergroundStation(CentreOf(near));
            far = SpawnUndergroundStation(CentreOf(spread));
            FoundAt(Home);
            Assert.That(territory.Owner(map, near)?.Id, Is.EqualTo(Reavers), "Claimed with the stronghold's ring.");
            Assert.That(territory.ContestOf(map, near), Is.Null, "Without a blockade.");

            foreach (var cell in ring.Skip(1))
            {
                activity.Stamp(map, cell);
            }

            Assert.That(campaign.TrySpreadOnce(), Is.True);
            Assert.That(territory.Owner(map, spread)?.Id, Is.EqualTo(Reavers), "Spread takes it at once.");
            Assert.That(territory.ContestOf(map, spread), Is.Null);
        });
        await Server.WaitAssertion(() =>
        {
            var stations = Server.System<Content.Server._WF.Encounters.Systems.WFEncounterSchedulerSystem>().Stations(MapData.MapId).Select(entry => entry.Owner);
            Assert.That(stations, Does.Contain(station), "A Reaver cell does not cut it off.");
            Assert.That(stations, Does.Contain(far));
        });
    }

    /// <summary>Held space does not time, intercept, raid or mark a neutral ship; the same ship under another flag is all of those.</summary>
    [Test]
    public async Task NeutralShipsAreNeverInterceptedRaidedOrMarked()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var stronghold = FoundAt(Home);
            campaign.SetTier(3);
            var ship = SpawnGrid(CentreOf(Home.Neighbour(1)) + new Vector2(0f, 1500f), "Test Smuggler");
            Fly(ship, Underground);
            Board(ship);
            var state = campaign.State()!;

            campaign.CheckIntercepts();
            Assert.That(state.InsideSince.ContainsKey(ship), Is.False, "Its time in held space is not counted.");
            state.InsideSince[ship] = STiming.CurTime - TimeSpan.FromMinutes(10);
            campaign.CheckIntercepts();
            Assert.That(campaign.Patrols(), Is.Empty, "No patrol is sent after it.");
            Assert.That(campaign.TryIntercept(ship), Is.False, "Not even when asked.");
            Assert.That(campaign.TryLaunchRaid(), Is.False, "Beside held space, but not raided.");

            ShootAt(stronghold, "lead", ship);
            campaign.CheckStrongholds();
            Assert.That(campaign.IsMarked(ship), Is.False, "Not even for shooting at a stronghold.");

            Fly(ship, "None");
            ShootAt(stronghold, "lead", ship);
            campaign.CheckStrongholds();
            Assert.That(campaign.IsMarked(ship), Is.True, "Anyone else is.");

            // The same ship under no flag is timed, intercepted and raided, so the refusals above were the neutrality.
            state.InsideSince.Clear();
            campaign.CheckIntercepts();
            Assert.That(state.InsideSince.ContainsKey(ship) || campaign.Patrols().Any(), Is.True, "Its time in held space is counted, or a patrol is sent.");
            Assert.That(campaign.TryLaunchRaid(), Is.True, "Beside held space, it is raided.");
        });
    }

    /// <summary>A campaign ship is told which companies to leave alone, from its faction.</summary>
    [Test]
    public async Task CampaignShipsCarryTheFactionsNeutralCompanies()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var stronghold = FoundAt(Home);
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(stronghold).Ships["lead"].NeutralCompanies, Is.EquivalentTo(new[] { Underground }));
            Assert.That(campaign.TryLaunchPatrol(stronghold), Is.True);
            var patrol = campaign.Patrols().Single().Owner;
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(patrol).Ships["lead"].NeutralCompanies, Is.EquivalentTo(new[] { Underground }));
        });
    }

    /// <summary>The Fugitive and Godfather jobs put their people in the Underground company when they spawn.</summary>
    [Test]
    public async Task UndergroundJobsSpawnInTheUndergroundCompany()
    {
        await Server.WaitAssertion(() =>
        {
            var handler = typeof(CompanySystem).GetMethod("OnPlayerSpawnComplete", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var mob = SEntMan.GetEntity(Player);
            foreach (var job in new[] { "Fugitive", "Godfather" })
            {
                SEntMan.RemoveComponent<CompanyComponent>(mob);
                var spawned = new PlayerSpawnCompleteEvent(mob, ServerSession, job, false, true, 0, EntityUid.Invalid,
                    HumanoidCharacterProfile.DefaultWithSpecies());
                handler.Invoke(Server.System<CompanySystem>(), new object[] { spawned });
                Assert.That(SEntMan.GetComponent<CompanyComponent>(mob).CompanyName.Id, Is.EqualTo(Underground), job);
            }

            SEntMan.RemoveComponent<CompanyComponent>(mob);
        });
    }
}
