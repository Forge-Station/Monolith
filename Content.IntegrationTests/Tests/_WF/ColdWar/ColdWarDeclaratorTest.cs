#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._Mono.AlertLevel;
using Content.Server._Mono.WarDeclarator;
using Content.Server._NF.SectorServices;
using Content.Server._WF.ColdWar;
using Content.Shared._Mono.Company;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Interaction.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.ColdWar;

/// <summary>
/// The faction war declarators move the war level both ways: war once both sides declare, a ceasefire once both
/// offer one, and nothing on one side's word alone.
/// </summary>
[TestFixture]
[TestOf(typeof(ManualPortstrikeRuleSystem))]
public sealed class ColdWarDeclaratorTest
{
    private const string Rule = "ManualPortstrikeTsfPdv";

    [Test]
    public async Task DeclaratorsRaiseAndLowerTheWarLevel()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var warLevel = entMan.System<WarLevelSystem>();

        await server.WaitAssertion(() =>
        {
            // The war level lives on the sector service entity, which a station's host component brings up.
            var host = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.AddComponent<StationSectorServiceHostComponent>(host);
            Assert.That(entMan.HasComponent<WarLevelComponent>(entMan.System<SectorServiceSystem>().GetServiceEntity()));

            var rule = entMan.SpawnEntity(Rule, MapCoordinates.Nullspace);
            var status = entMan.GetComponent<ManualPortstrikeRuleComponent>(rule).SectorStatus;
            var state = entMan.EnsureComponent<WFColdWarComponent>(rule);
            var cooldown = state.Cooldown;
            state.Cooldown = TimeSpan.Zero;

            var marine = Member("TSF");
            var vanguard = Member("PDV");
            var tsf = entMan.SpawnEntity("WarDeclaratorTsf", MapCoordinates.Nullspace);
            var pdv = entMan.SpawnEntity("WarDeclaratorPdv", MapCoordinates.Nullspace);

            Assert.That(Hot(), Is.False, "The round opens cold.");

            Use(tsf, vanguard);
            Assert.That(status["TSF"], Is.False, "Another faction's declarator answers only its own.");

            Use(tsf, marine);
            Assert.That(status["TSF"], Is.True);
            Assert.That(Hot(), Is.False, "One side's declaration is not a war.");

            Use(tsf, marine);
            Assert.That(status["TSF"], Is.False, "A second use withdraws the declaration.");

            Use(pdv, vanguard);
            Assert.That(Hot(), Is.False);
            Use(tsf, marine);
            Assert.That(Hot(), Is.True, "Both sides declaring is war.");

            Use(pdv, vanguard);
            Assert.That(status["PDV"], Is.False);
            Assert.That(Hot(), Is.True, "One side's ceasefire offer does not end the war.");

            Use(pdv, vanguard);
            Assert.That(status["PDV"], Is.True, "A second use withdraws the offer.");

            Use(pdv, vanguard);
            Use(tsf, marine);
            Assert.That(Hot(), Is.False, "Both sides offering a ceasefire ends the war.");

            // A level set from outside is the new baseline: the next use reads as a ceasefire offer.
            warLevel.SetLevel(true);
            Use(tsf, marine);
            Assert.That(status["TSF"], Is.False);
            Assert.That(status["PDV"], Is.True);
            Assert.That(Hot(), Is.True);
            Use(pdv, vanguard);
            Assert.That(Hot(), Is.False);

            // A second rule for the same factions follows the first: one use is one change of stance in each.
            var second = entMan.SpawnEntity(Rule, MapCoordinates.Nullspace);
            var other = entMan.GetComponent<ManualPortstrikeRuleComponent>(second).SectorStatus;
            entMan.EnsureComponent<WFColdWarComponent>(second).Cooldown = TimeSpan.Zero;
            Use(tsf, marine);
            Use(pdv, vanguard);
            Assert.That(Hot(), Is.True);
            Assert.That(other["TSF"] && other["PDV"], Is.True, "The second rule's stances follow the first's.");
            Use(tsf, marine);
            Use(pdv, vanguard);
            Assert.That(Hot(), Is.False);
            Assert.That(other["TSF"] || other["PDV"], Is.False);
            entMan.DeleteEntity(second);

            state.Cooldown = cooldown;
            Use(tsf, marine);
            Use(tsf, marine);
            Assert.That(status["TSF"], Is.True, "A declarator cannot be used again inside its cooldown.");

            foreach (var uid in new[] { marine, vanguard, tsf, pdv, rule, host })
            {
                entMan.DeleteEntity(uid);
            }

            return;

            bool Hot() => warLevel.GetWarLevel(EntityUid.Invalid);

            void Use(EntityUid declarator, EntityUid user) =>
                entMan.EventBus.RaiseLocalEvent(declarator, new UseInHandEvent(user));

            EntityUid Member(string company)
            {
                var uid = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
                entMan.EnsureComponent<CompanyComponent>(uid).CompanyName = company;
                return uid;
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A scheduled encounter that sets the Federation's ships on the Dynasty's waits for a declared war.</summary>
    [Test]
    public async Task NavyClashesWaitForWar()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var encounter in server.ProtoMan.EnumeratePrototypes<WFEncounterPrototype>())
                {
                    if (encounter.Start == WFEncounterStart.Manual)
                        continue;

                    var federation = Sides(encounter, "TSF");
                    var dynasty = Sides(encounter, "PDV");
                    if (federation.Any(side => dynasty.Any(other => other != side)))
                        Assert.That(encounter.RequiresWar, Is.True, $"{encounter.ID} sets TSF and PDV ships on each other without requiresWar.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    private static HashSet<string> Sides(WFEncounterPrototype encounter, string family)
    {
        return encounter.Ships
            .Where(ship => WFCompanyFamily.Of(ship.Company?.Id) == family)
            .Select(ship => ship.Side)
            .ToHashSet();
    }
}
