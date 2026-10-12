#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.SectorControl;
using Content.Server._WF.Encounters;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Server.Administration.Managers;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.SectorControl;
using Content.Shared.Administration;
using Robust.Client.Console;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>Founding, spreading and the threat tier: where strongholds go, what a cell needs to be taken and how the tier moves.</summary>
[TestOf(typeof(WFReaverCampaignSystem))]
public sealed class WFReaverFoundingTest : WFReaverTestBase
{
    /// <summary>The cells 14 to 24 km from the origin, where the first stronghold may go.</summary>
    private static List<WFSectorCell> Band()
    {
        return WFSectorHex.Within(WFSectorCell.Origin, 7)
            .Where(cell => WFSectorHex.Centre(cell, 3000f).Length() is >= 14000f and <= 24000f).ToList();
    }

    /// <summary>The campaign runs only with every switch on and a campaign for the storyteller's preset.</summary>
    [Test]
    public async Task CampaignRunsOnlyWhenEverySwitchIsOn()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var config = Server.ResolveDependency<IConfigurationManager>();
            Assert.That(campaign.Running, Is.False, "The storyteller is off by default.");

            // On, it runs the campaign of the preset in force; by default that is the standard one.
            config.SetCVar(EncountersCVars.Enabled, true);
            config.SetCVar(ReaverCampaignCVars.Enabled, true);
            Assert.That(campaign.Campaign?.ID, Is.EqualTo("WFReaverCampaignStandard"));
            Assert.That(campaign.Running, Is.True);
            config.SetCVar(EncountersCVars.Preset, TestPreset);
            Assert.That(campaign.Campaign?.ID, Is.EqualTo(TestCampaign));
            Assert.That(campaign.Running, Is.True);

            config.SetCVar(ReaverCampaignCVars.Enabled, false);
            Assert.That(campaign.Running, Is.False);
            config.SetCVar(ReaverCampaignCVars.Enabled, true);
            config.SetCVar(SectorControlCVars.Enabled, false);
            Assert.That(campaign.Running, Is.False);
            config.SetCVar(SectorControlCVars.Enabled, true);
            Assert.That(campaign.Running, Is.True);

            // A preset with no campaign runs none.
            config.SetCVar(EncountersCVars.Preset, "WFEncounterPresetStandard");
            Assert.That(campaign.Campaign?.ID, Is.EqualTo("WFReaverCampaignStandard"));
            config.SetCVar(EncountersCVars.Preset, BarePreset);
            Assert.That(campaign.Campaign, Is.Null);
            Assert.That(campaign.Running, Is.False);
        });
    }

    /// <summary>The first stronghold goes in a quiet, claimable cell 14 to 24 km out, and none is founded when every cell is noisy or vetoed.</summary>
    [Test]
    public async Task FirstStrongholdIsFoundedByTheRules()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var activity = Server.System<WFSectorActivitySystem>();
            var recorder = Server.System<WFSectorTestSystem>();
            var map = MapData.MapId;
            var band = Band();
            Assert.That(band, Is.Not.Empty);

            foreach (var cell in band)
            {
                activity.Stamp(map, cell);
            }

            Assert.That(campaign.TryFound(out _), Is.False, "Nowhere is quiet.");
            territory.Territory(map)!.Activity.Clear();

            recorder.Vetoed.UnionWith(band);
            Assert.That(campaign.TryFound(out _), Is.False, "Nowhere may be claimed.");
            recorder.Reset();
            Assert.That(campaign.Strongholds(), Is.Empty);

            Assert.That(campaign.TryFound(out var stronghold), Is.True);
            var comp = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold);
            var encounter = SEntMan.GetComponent<WFEncounterComponent>(stronghold);
            Assert.That(encounter.Prototype.Id, Is.EqualTo(Stronghold), "The light stronghold, with one player.");
            Assert.That(encounter.Resolution, Is.Null);
            Assert.That(comp.Lead, Is.EqualTo("lead"));
            Assert.That(band, Does.Contain(comp.Cell));
            Assert.That(WFSectorHex.Centre(comp.Cell, 3000f).Length(), Is.InRange(14000f, 24000f));

            var ring = WFSectorHex.Within(comp.Cell, 1);
            Assert.That(ring, Has.Count.EqualTo(7));
            foreach (var cell in ring)
            {
                Assert.That(territory.Owner(map, cell)?.Id, Is.EqualTo(Reavers), WFSectorHex.Callsign(cell));
                Assert.That(territory.Sources(map, cell), Is.EquivalentTo(new[] { stronghold }));
            }

            Assert.That(territory.Held(map, Reavers), Has.Count.EqualTo(7));
            var state = campaign.State()!;
            Assert.That(state.Founded, Is.EqualTo(1));
            Assert.That(state.Tier, Is.InRange(1, 2), "Ten for the stronghold, seven cells and a share for how near the core they lie.");
            Assert.That(state.Score, Is.GreaterThan(17f));
            Assert.That(state.LastFounded, Is.Not.Null);

            // The map legend says so.
            var legend = Server.System<WFSectorSyncSystem>().BuildStatus().Lines.Single();
            Assert.That(legend.Faction, Is.EqualTo(Reavers));
            Assert.That(legend.Line, Does.StartWith("Ashfall Reavers: "));

            Assert.That(campaign.TryFound(out _), Is.False, "A second needs held space two cells from the first.");
        });
        await RunTicks(60);
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            Assert.That(campaign.Strongholds(), Has.Count.EqualTo(1), "The stronghold stands, crewed, on the next check.");
        });
    }

    /// <summary>A later stronghold goes in a held cell on the edge of held space two cells from every stronghold.</summary>
    [Test]
    public async Task LaterStrongholdsGoOnTheHeldEdge()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            var first = FoundAt(Home);

            Assert.That(campaign.TryFound(out _), Is.False, "Every held cell is next to the stronghold.");
            Assert.That(campaign.TrySpreadOnce(), Is.True);
            var spread = territory.Held(map, Reavers).Single(cell => WFSectorHex.Distance(cell, Home) == 2);
            Assert.That(territory.Sources(map, spread), Is.EquivalentTo(new[] { first }));

            Assert.That(campaign.TryFound(out var second), Is.True);
            var comp = SEntMan.GetComponent<WFReaverStrongholdComponent>(second);
            Assert.That(comp.Cell, Is.EqualTo(spread), "The only held cell far enough from the first.");
            Assert.That(campaign.Strongholds(), Has.Count.EqualTo(2));
            Assert.That(territory.Sources(map, spread), Is.EquivalentTo(new[] { first, second }), "Both support the cell they share.");
            Assert.That(territory.Owner(map, comp.Cell)?.Id, Is.EqualTo(Reavers));
            foreach (var cell in WFSectorHex.Ring(comp.Cell, 1))
            {
                if (WFSectorHex.Distance(cell, WFSectorCell.Origin) > WFSectorProtectionSystem.CoreRadius)
                    Assert.That(territory.Owner(map, cell)?.Id, Is.EqualTo(Reavers));
            }

            Assert.That(campaign.State()!.Founded, Is.EqualTo(2));
        });
    }

    /// <summary>Spread takes one quiet, claimable frontier cell near a stronghold; a station's cell is contested and taken if it stays quiet.</summary>
    [Test]
    public async Task SpreadRespectsQuietnessVetoesAndBlockades()
    {
        await StartCampaign();
        var ring = WFSectorHex.Ring(Home, 2);
        var stationCell = ring[0];
        EntityUid stronghold = default, station = default;
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var activity = Server.System<WFSectorActivitySystem>();
            var recorder = Server.System<WFSectorTestSystem>();
            var map = MapData.MapId;
            stronghold = FoundAt(Home);
            Assert.That(territory.Held(map, Reavers), Has.Count.EqualTo(7));

            foreach (var cell in ring)
            {
                activity.Stamp(map, cell);
            }

            Assert.That(campaign.TrySpreadOnce(), Is.False, "Every cell in reach was flown lately.");
            territory.Territory(map)!.Activity.Clear();

            recorder.Vetoed.UnionWith(ring);
            Assert.That(campaign.TrySpreadOnce(), Is.False, "Every cell in reach is vetoed.");
            recorder.Reset();

            // Only the cell with a station in it is quiet: it is warned of, not taken.
            station = SpawnStation(CentreOf(stationCell), "Outpost Lambda");
            foreach (var cell in ring.Skip(1))
            {
                activity.Stamp(map, cell);
            }

            Assert.That(campaign.TrySpreadOnce(), Is.True);
            Assert.That(territory.Owner(map, stationCell), Is.Null, "Not yet.");
            var contest = territory.ContestOf(map, stationCell)!;
            Assert.That(contest.Faction.Id, Is.EqualTo(Reavers));
            Assert.That(contest.Source, Is.EqualTo(stronghold));
            Assert.That((contest.Deadline - STiming.CurTime).TotalMinutes, Is.InRange(9.9, 10.1), "Ten minutes' notice.");
            Assert.That(recorder.Contested.Select(entry => entry.Cell), Is.EquivalentTo(new[] { stationCell }));
            Assert.That(territory.Held(map, Reavers), Has.Count.EqualTo(7));

            // Traffic could hold the lane; it stayed quiet, and the deadline comes.
            contest.Deadline = STiming.CurTime;
        });
        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var scheduler = Server.System<WFEncounterSchedulerSystem>();
            var map = MapData.MapId;
            Assert.That(territory.Owner(map, stationCell)?.Id, Is.EqualTo(Reavers), "Quiet to the deadline, so it is taken.");
            Assert.That(territory.Sources(map, stationCell), Is.EquivalentTo(new[] { stronghold }));
            Assert.That(scheduler.Stations(map).Select(entry => entry.Owner), Does.Not.Contain(station), "A Reaver station is cut off.");
            Server.System<WFSectorTestSystem>().Reset();
        });
    }

    /// <summary>A plain frontier cell is taken at once, and only within support range of the stronghold.</summary>
    [Test]
    public async Task SpreadClaimsOneCellAtATimeWithinSupportRange()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            var stronghold = FoundAt(Home);

            Assert.That(campaign.TrySpreadOnce(), Is.True);
            Assert.That(territory.Held(map, Reavers), Has.Count.EqualTo(8));
            for (var i = 0; i < 40; i++)
            {
                campaign.TrySpreadOnce();
            }

            var held = territory.Held(map, Reavers);
            Assert.That(held.Count, Is.InRange(8, 19), "The stronghold's cell, its ring and the second ring, at most.");
            Assert.That(held.All(cell => WFSectorHex.Distance(cell, Home) <= 2), Is.True, "Nothing beyond support range.");
            Assert.That(held.All(cell => territory.Sources(map, cell).Contains(stronghold)), Is.True);
            campaign.Recompute();
            Assert.That(campaign.State()!.PeakCells, Is.EqualTo(held.Count), "The round-end summary remembers the most it held.");
        });
    }

    /// <summary>The tier rises at once when the score does and falls only after ten minutes under, ten since the last announcement.</summary>
    [Test]
    public async Task TierRisesAtOnceAndFallsWithHysteresis()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var proto = campaign.Campaign!;
            var map = MapData.MapId;
            var now = STiming.CurTime;

            Assert.That(WFReaverCampaignSystem.TierFor(proto, 0f), Is.Zero);
            Assert.That(WFReaverCampaignSystem.TierFor(proto, 11.9f), Is.Zero);
            Assert.That(WFReaverCampaignSystem.TierFor(proto, 12f), Is.EqualTo(1));
            Assert.That(WFReaverCampaignSystem.TierFor(proto, 54.9f), Is.EqualTo(2));
            Assert.That(WFReaverCampaignSystem.TierFor(proto, 105f), Is.EqualTo(WFReaverCampaignSystem.MaxTier));
            Assert.That(WFReaverCampaignSystem.TierFor(proto, 1000f), Is.EqualTo(WFReaverCampaignSystem.MaxTier));
            Assert.That(campaign.TierName(0), Is.EqualTo("None"));
            Assert.That(campaign.TierName(5), Is.EqualTo("Overrun"));

            FoundAt(Home);
            var state = campaign.State()!;
            var scored = WFReaverCampaignSystem.TierFor(proto, state.Score);
            Assert.That(state.Tier, Is.EqualTo(scored));
            Assert.That(state.TierAnnounced, Is.EqualTo(now), "A rise is announced at once.");

            // A forced tier is held, not undone by the next look at the score.
            campaign.SetTier(4);
            Assert.That(state.Tier, Is.EqualTo(4));
            campaign.Recompute();
            Assert.That(state.Tier, Is.EqualTo(4), "A fall waits.");
            Assert.That(state.BelowSince, Is.Not.Null);

            state.BelowSince = now - TimeSpan.FromMinutes(11);
            state.TierAnnounced = now - TimeSpan.FromMinutes(5);
            campaign.Recompute();
            Assert.That(state.Tier, Is.EqualTo(4), "Under for long enough, but a tier was announced five minutes ago.");

            state.TierAnnounced = now - TimeSpan.FromMinutes(11);
            campaign.Recompute();
            Assert.That(state.Tier, Is.EqualTo(scored), "Ten minutes under and ten since the last word: it falls to what the score says.");
            Assert.That(state.BelowSince, Is.Null);
            Assert.That(state.TierAnnounced, Is.EqualTo(now));

            // A jump in the score is a jump in the tier, at once.
            foreach (var cell in WFSectorHex.Within(Home, 3))
            {
                territory.TryClaim(map, cell, Reavers, null);
            }

            state.TierAnnounced = now - TimeSpan.FromMinutes(1);
            campaign.Recompute();
            Assert.That(state.Tier, Is.GreaterThan(scored));
            Assert.That(state.Tier, Is.EqualTo(WFReaverCampaignSystem.TierFor(proto, state.Score)));
            Assert.That(state.TierAnnounced, Is.EqualTo(now), "Rising is announced inside the gap.");
        });
    }

    /// <summary>The storyteller's own Reaver encounters are weighted up by the tier, within the campaign's cap, and nothing else is.</summary>
    [Test]
    public async Task ThreatScaleWeightsOnlyTheListedEncounters()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var config = Server.ResolveDependency<IConfigurationManager>();
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            var raiders = prototypes.Index<WFEncounterPrototype>("WFEncounterPirateRaiders");
            var unlisted = prototypes.Index<WFEncounterPrototype>(Stronghold);

            float Weight(WFEncounterPrototype prototype)
            {
                var ev = new WFEncounterWeightEvent(prototype, 1f);
                SEntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
                return ev.Weight;
            }

            Assert.That(Weight(raiders), Is.EqualTo(1f), "No campaign state yet.");
            campaign.SetTier(0);
            Assert.That(Weight(raiders), Is.EqualTo(1f));
            campaign.SetTier(2);
            Assert.That(Weight(raiders), Is.EqualTo(1.1f).Within(0.001f));
            campaign.SetTier(5);
            Assert.That(Weight(raiders), Is.EqualTo(1.25f).Within(0.001f), "The tier's 1.5 is held to the campaign's cap.");
            Assert.That(Weight(unlisted), Is.EqualTo(1f), "Only the listed encounters are weighted.");

            config.SetCVar(ReaverCampaignCVars.Enabled, false);
            Assert.That(Weight(raiders), Is.EqualTo(1f), "Switched off, the storyteller is left alone.");
            config.SetCVar(ReaverCampaignCVars.Enabled, true);
        });
    }

    /// <summary>The admin command founds, steps, pauses and clears the campaign.</summary>
    [Test]
    public async Task CommandDrivesTheCampaign()
    {
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            var console = Server.ConsoleHost;
            var campaign = Server.System<WFReaverCampaignSystem>();
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            var centre = CentreOf(Home);

            console.ExecuteCommand("wf_reavers status");
            console.ExecuteCommand($"wf_reavers found {(int) centre.X} {(int) centre.Y}");
            var strongholds = campaign.Strongholds();
            Assert.That(strongholds, Has.Count.EqualTo(1));
            Assert.That(strongholds[0].Comp1.Cell, Is.EqualTo(Home));
            Assert.That(territory.Held(map, Reavers), Has.Count.EqualTo(7));

            console.ExecuteCommand("wf_reavers found 1 x");
            Assert.That(campaign.Strongholds(), Has.Count.EqualTo(1));

            console.ExecuteCommand("wf_reavers tier 4");
            Assert.That(campaign.State()!.Tier, Is.EqualTo(4));
            console.ExecuteCommand("wf_reavers tier 9");
            Assert.That(campaign.State()!.Tier, Is.EqualTo(4), "Out of range.");

            console.ExecuteCommand("wf_reavers spread");
            Assert.That(territory.Held(map, Reavers), Has.Count.EqualTo(8));
            console.ExecuteCommand("wf_reavers patrol");
            Assert.That(campaign.Patrols(), Has.Count.EqualTo(1));

            console.ExecuteCommand("wf_reavers pause");
            Assert.That(campaign.Paused, Is.True);
            console.ExecuteCommand("wf_reavers resume");
            Assert.That(campaign.Paused, Is.False);

            console.ExecuteCommand("wf_reavers clear");
            Assert.That(campaign.Strongholds(), Is.Empty);
            Assert.That(campaign.Patrols(), Is.Empty);
            Assert.That(territory.Held(map, Reavers), Is.Empty);
            console.ExecuteCommand("wf_reavers status");
        });
    }

    /// <summary>With no stronghold standing, spread, patrol and raid say so and point at found, and status says when the first is due or why none will be.</summary>
    [Test]
    public async Task CommandPointsAtFoundWhenNoStrongholdStands()
    {
        await StartCampaign();
        var admins = Server.ResolveDependency<IAdminManager>();
        await Server.WaitPost(() => admins.PromoteHost(ServerSession));
        for (var i = 0; i < 60 && !admins.HasAdminFlag(ServerSession, AdminFlags.Admin); i++)
        {
            await RunTicks(1);
        }

        Assert.That(admins.HasAdminFlag(ServerSession, AdminFlags.Admin), "The player never became an admin.");

        var console = Client.ResolveDependency<IClientConsoleHost>();
        var output = new List<string>();
        EventHandler<AddStringArgs> capture = (_, args) =>
        {
            // Local lines are the client's own echo; the server's replies arrive as acks.
            if (!args.Local)
                output.Add(args.Text.ToString());
        };

        await Client.WaitPost(() => console.AddString += capture);
        var loc = Server.ResolveDependency<ILocalizationManager>();
        var config = Server.ResolveDependency<IConfigurationManager>();
        var campaign = Server.System<WFReaverCampaignSystem>();
        var noStronghold = loc.GetString("cmd-wf_reavers-no-stronghold");
        try
        {
            foreach (var step in new[] { "spread", "patrol", "raid" })
            {
                var lines = await Run(output, $"wf_reavers {step}");
                Assert.That(lines, Is.EqualTo(new[] { noStronghold }), step);
            }

            Assert.That(noStronghold, Does.Contain("wf_reavers found"), "It names the way to place one.");

            // Status ends with when the first is due, from the stations generating.
            var status = await Run(output, "wf_reavers status");
            Assert.That(status.Last(), Is.EqualTo(loc.GetString("cmd-wf_reavers-status-founding-untimed")));

            // No retry, so a founding that is due stays unfounded while it is read.
            SetPrivate(campaign, "_nextFoundTry", STiming.CurTime + TimeSpan.FromHours(1));
            SetPrivate(campaign, "_stationsAt", STiming.CurTime + TimeSpan.FromMinutes(7) - TimeSpan.FromSeconds(3));
            status = await Run(output, "wf_reavers status");
            Assert.That(status.Last(), Is.EqualTo(loc.GetString("cmd-wf_reavers-status-first-due", ("minutes", 7))));
            Assert.That(status.Last(), Does.Contain("7 minutes").And.Contain("wf_reavers found"));

            SetPrivate(campaign, "_stationsAt", STiming.CurTime - TimeSpan.FromMinutes(1));
            status = await Run(output, "wf_reavers status");
            Assert.That(status.Last(), Is.EqualTo(loc.GetString("cmd-wf_reavers-status-founding-overdue")));

            // After a stronghold has fallen it is the regroup that is timed.
            await Server.WaitPost(() =>
            {
                var state = campaign.State(true)!;
                state.Founded = 1;
                state.NoStrongholdSince = STiming.CurTime + TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(3);
            });
            status = await Run(output, "wf_reavers status");
            Assert.That(status.Last(), Is.EqualTo(loc.GetString("cmd-wf_reavers-status-regroup-due", ("minutes", 5))));

            // Paused or switched off, it says so and why.
            await Server.WaitPost(() => campaign.Paused = true);
            status = await Run(output, "wf_reavers status");
            Assert.That(status.Last(), Is.EqualTo(loc.GetString("cmd-wf_reavers-status-paused")));
            await Server.WaitPost(() => campaign.Paused = false);

            foreach (var cvar in new[] { ReaverCampaignCVars.Enabled, EncountersCVars.Enabled, SectorControlCVars.Enabled })
            {
                await Server.WaitPost(() => config.SetCVar(cvar, false));
                status = await Run(output, "wf_reavers status");
                Assert.That(status.Last(), Is.EqualTo(loc.GetString(
                    cvar == ReaverCampaignCVars.Enabled ? "cmd-wf_reavers-status-mode-off" : "cmd-wf_reavers-status-switch-off", ("cvar", cvar.Name))));
                Assert.That(status.Last(), Does.Contain(cvar.Name));
                await Server.WaitPost(() => config.SetCVar(cvar, true));
            }

            // A preset with no campaign has nothing to found.
            await Server.WaitPost(() => config.SetCVar(EncountersCVars.Preset, BarePreset));
            var noCampaign = loc.GetString("cmd-wf_reavers-no-campaign");
            Assert.That(await Run(output, "wf_reavers spread"), Is.EqualTo(new[] { noCampaign }));
            Assert.That(await Run(output, "wf_reavers status"), Is.EqualTo(new[] { noCampaign }));
            await Server.WaitPost(() => config.SetCVar(EncountersCVars.Preset, TestPreset));

            // With a stronghold standing a failure is the step's own.
            await Server.WaitAssertion(() => FoundAt(Home));
            Assert.That(await Run(output, "wf_reavers raid"), Is.EqualTo(new[] { loc.GetString("cmd-wf_reavers-raid-failed") }));
            status = await Run(output, "wf_reavers status");
            Assert.That(status, Has.None.Contain("No stronghold stands"));
        }
        finally
        {
            await Client.WaitPost(() => console.AddString -= capture);
        }
    }

    /// <summary>Runs a command as the test player, an admin, and returns the lines the server wrote back.</summary>
    private async Task<List<string>> Run(List<string> output, string command)
    {
        await Client.WaitPost(output.Clear);
        await Server.WaitPost(() => Server.ConsoleHost.ExecuteCommand(ServerSession, command));
        await RunTicks(5);
        var lines = new List<string>();
        await Client.WaitPost(() => lines.AddRange(output));
        return lines;
    }

    private static void SetPrivate(WFReaverCampaignSystem campaign, string field, object value)
    {
        typeof(WFReaverCampaignSystem).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(campaign, value);
    }
}
