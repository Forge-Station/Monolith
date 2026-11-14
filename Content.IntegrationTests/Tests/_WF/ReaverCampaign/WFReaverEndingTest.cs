#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.SectorControl;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>The end of the campaign: breaking the last stronghold is announced once, and freed Reaver cells are announced as clearing.</summary>
[TestOf(typeof(WFReaverCampaignSystem))]
public sealed class WFReaverEndingTest : WFReaverTestBase
{
    private const string Ending = "The Ashfall Reavers are broken. Their strongholds are gone and the sector is clear.";

    private static readonly WFSectorCell Other = new(-5, 3);

    /// <summary>Breaking the last stronghold announces the ending once and one clearing line naming every freed callsign; the legend and the round summary say so.</summary>
    [Test]
    public async Task BreakingTheLastStrongholdAnnouncesTheEnding()
    {
        await StartCampaign();
        EntityUid stronghold = default;
        var since = 0;
        await Server.WaitAssertion(() =>
        {
            stronghold = FoundAt(Home);
            Assert.That(Server.System<WFReaverCampaignSystem>().State()!.Defeated, Is.False);
        });
        await RunTicks(60);
        since = ChatCount();
        await Server.WaitAssertion(() => KillCrew(stronghold, "lead"));
        await WaitUntilServer(() => SEntMan.GetComponent<WFEncounterComponent>(stronghold).Resolution == WFEncounterResolution.Destroyed, 300);
        // Before the regroup clock (three seconds) founds another.
        await RunTicks(20);
        await Server.WaitAssertion(() =>
        {
            var state = Server.System<WFReaverCampaignSystem>().State()!;
            Assert.That(state.Defeated, Is.True);
            Assert.That(state.Defeats, Is.EqualTo(1));
            Assert.That(Server.System<WFReaverCampaignSystem>().Strongholds(), Is.Empty);
        });

        var lines = ChatSince(since);
        Assert.That(lines.Count(line => line.Contains(Ending)), Is.EqualTo(1), "The ending is announced once.");
        Assert.That(lines.Count(line => line.Contains("is broken.")), Is.EqualTo(1), "The stronghold's own line stays separate.");
        var clearing = lines.Where(line => line.Contains("is clearing.")).ToList();
        Assert.That(clearing, Has.Count.EqualTo(1), "One line for all the cells freed together.");
        Assert.That(clearing[0], Does.Contain("Drone activity returns in 10 minutes. Clear the area."));
        var cells = WFSectorHexCells(Home);
        foreach (var cell in cells)
        {
            Assert.That(clearing[0], Does.Contain(WFSectorHex.Callsign(cell)));
        }

        var listed = string.Join(", ", cells.Select(WFSectorHex.Callsign).OrderBy(callsign => callsign, StringComparer.Ordinal));
        Assert.That(clearing[0], Does.Contain($"Reaver space at {listed} is clearing."));
    }

    /// <summary>Breaking one of two strongholds is no ending.</summary>
    [Test]
    public async Task BreakingOneOfTwoIsNoEnding()
    {
        await StartCampaign();
        EntityUid first = default;
        var since = 0;
        await Server.WaitAssertion(() =>
        {
            first = FoundAt(Home);
            FoundAt(Other);
        });
        await RunTicks(60);
        since = ChatCount();
        await Server.WaitAssertion(() => KillCrew(first, "lead"));
        await WaitUntilServer(() => SEntMan.GetComponent<WFEncounterComponent>(first).Resolution == WFEncounterResolution.Destroyed, 300);
        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            var state = Server.System<WFReaverCampaignSystem>().State()!;
            Assert.That(state.Defeated, Is.False);
            Assert.That(state.Defeats, Is.Zero);
        });

        var lines = ChatSince(since);
        Assert.That(lines.Any(line => line.Contains(Ending)), Is.False);
        Assert.That(lines.Count(line => line.Contains("is clearing.")), Is.EqualTo(1), "Its cells are still clearing.");
    }

    private static System.Collections.Generic.List<WFSectorCell> WFSectorHexCells(WFSectorCell cell)
    {
        var cells = new System.Collections.Generic.List<WFSectorCell> { cell };
        cells.AddRange(WFSectorHex.Ring(cell, 1));
        return cells;
    }
}
