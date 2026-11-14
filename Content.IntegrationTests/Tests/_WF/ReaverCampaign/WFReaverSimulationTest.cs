#nullable enable
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>A simulated stronghold answers a simulated attack on it as it does a real one.</summary>
[TestOf(typeof(WFEncounterSystem))]
public sealed class WFReaverSimulationTest : WFReaverTestBase
{
    /// <summary>Each round of a simulated fight on a simulated hub counts as a hit by the attacker on the stronghold.</summary>
    [Test]
    public async Task ASimulatedAttackOnASimulatedHubCountsAsHits()
    {
        await StartCampaign();
        var config = Server.ResolveDependency<IConfigurationManager>();
        EntityUid stronghold = default, attacker = default;
        await Server.WaitAssertion(() =>
        {
            config.SetCVar(EncountersCVars.Simulate, true);
            config.SetCVar(EncountersCVars.MaterializeRange, 100f);
            // No losses: only the hits are wanted.
            config.SetCVar(EncountersCVars.DuelMeanSeconds, 1000000f);
            stronghold = FoundAt(Home);
            var hub = SEntMan.GetComponent<WFEncounterComponent>(stronghold).Ships[SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold).Lead];
            Assert.That(hub.Virtual, Is.True, "Nobody near: the hub is simulated.");

            var encounters = Server.System<WFEncounterSystem>();
            var hubAt = Server.System<SharedTransformSystem>().GetWorldPosition(hub.Grid);
            Assert.That(encounters.TrySpawn(ProtoMan.Index<WFEncounterPrototype>("WFTestSimHauler"),
                new MapCoordinates(hubAt + new Vector2(800f, 0f), MapData.MapId), out attacker, offBudget: true), Is.True);
            Assert.That(encounters.SetOrders(attacker, "ship", new List<WFCrewObjective>
            {
                new() { Kind = WFCrewObjectiveKind.Attack, Target = SEntMan.GetNetEntity(hub.Grid), Range = 600f },
            }), Is.True);
        });

        try
        {
            var hit = false;
            for (var i = 0; i < 15 && !hit; i++)
            {
                await RunTicks(60);
                await Server.WaitPost(() =>
                {
                    var attackerShip = SEntMan.GetComponent<WFEncounterComponent>(attacker).Ships["ship"];
                    hit = SEntMan.GetComponent<WFReaverStrongholdComponent>(stronghold).NpcHits.ContainsKey(attackerShip.Grid);
                });
            }

            Assert.That(hit, Is.True, "The stronghold counts the simulated attacker's hits.");
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                Server.System<WFEncounterSystem>().End(attacker);
                config.SetCVar(EncountersCVars.Simulate, false);
                config.SetCVar(EncountersCVars.MaterializeRange, EncountersCVars.MaterializeRange.DefaultValue);
                config.SetCVar(EncountersCVars.DuelMeanSeconds, EncountersCVars.DuelMeanSeconds.DefaultValue);
            });
        }
    }
}
