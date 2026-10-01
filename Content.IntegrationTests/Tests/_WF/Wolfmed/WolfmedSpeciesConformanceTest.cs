#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;
using Content.Server.Body.Components;
using Content.Server._HL.Silicons.Synths.Battery;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._HL.Silicons.Synths.Battery;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// M4 (plan §9.1), strict: every round-start species answers what disables it, what kills it and what restores it.
/// Every check runs on every species; a failure is named with the species and fails the test unless its
/// <see cref="WolfmedSpeciesExceptionPrototype"/> excuses it with the plan's reason. An excuse that is no longer needed
/// fails too, and so does a species on a ladder (organic or machine) its exception does not name.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedLifeSystem))]
public sealed class WolfmedSpeciesConformanceTest : GameTest
{
    [Test]
    public async Task EverySpeciesConformsOrIsExcusedTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestBlood, 0.30f);
        var map = await Pair.CreateTestMap();
        var failed = new Dictionary<string, HashSet<WolfmedSpeciesCheck>>();
        var ladders = new Dictionary<string, bool>();
        var machines = new List<(string Species, EntityUid Body)>();
        var protoManager = Server.ResolveDependency<IPrototypeManager>();

        void Fail(string species, WolfmedSpeciesCheck check)
        {
            if (!failed.TryGetValue(species, out var set))
                failed[species] = set = new HashSet<WolfmedSpeciesCheck>();
            set.Add(check);
        }

        await Server.WaitAssertion(() =>
        {
            var graph = SEntMan.System<SharedBodySystem>();
            var s = new WolfmedScenario(SEntMan);
            var shutdown = SEntMan.System<WolfmedShutdownSystem>();
            var mobState = SEntMan.System<MobStateSystem>();

            foreach (var species in protoManager.EnumeratePrototypes<SpeciesPrototype>()
                         .Where(species => species.RoundStart)
                         .OrderBy(species => species.ID))
            {
                var id = species.ID;
                failed[id] = new HashSet<WolfmedSpeciesCheck>();
                var body = SEntMan.SpawnEntity(species.Prototype, map.GridCoords);

                if (!SEntMan.HasComponent<WoundHostComponent>(body))
                {
                    Fail(id, WolfmedSpeciesCheck.WoundHost);
                    ladders[id] = false;
                    SEntMan.DeleteEntity(body);
                    continue;
                }

                var organs = graph.GetBodyOrgans(body).Select(organ => organ.Id).ToList();
                var mechanical = shutdown.IsMechanical(body);
                ladders[id] = mechanical;
                var brains = organs.Where(organ => SEntMan.HasComponent<BrainComponent>(organ)).ToList();
                var hearts = organs.Where(organ => SEntMan.HasComponent<HeartComponent>(organ)).ToList();

                if (mechanical)
                {
                    if (brains.Count == 0 || !brains.All(SEntMan.HasComponent<WolfmedOrganComponent>))
                        Fail(id, WolfmedSpeciesCheck.CoreData);
                    if (hearts.Count == 0 || !hearts.All(SEntMan.HasComponent<WolfmedOrganComponent>))
                        Fail(id, WolfmedSpeciesCheck.PumpData);

                    // The power check needs the charge loop, which only runs on a chassis with a mind.
                    var minds = SEntMan.System<SharedMindSystem>();
                    minds.TransferTo(minds.CreateMind(null).Owner, body);
                    machines.Add((id, body));
                    continue;
                }

                if (brains.Count == 0 || !brains.All(brain => SEntMan.HasComponent<WolfmedBrainComponent>(brain) &&
                                                               SEntMan.HasComponent<WolfmedOrganComponent>(brain)))
                    Fail(id, WolfmedSpeciesCheck.BrainData);

                if (hearts.Count == 0)
                    Fail(id, WolfmedSpeciesCheck.Heart);
                else if (!hearts.All(SEntMan.HasComponent<WolfmedOrganComponent>))
                    Fail(id, WolfmedSpeciesCheck.HeartData);

                // Lungs belong to a body that breathes. A species built without a respirator (the Shadekin) has none.
                var lungs = organs.Where(organ => SEntMan.HasComponent<LungComponent>(organ)).ToList();
                if (lungs.Count == 0 && SEntMan.HasComponent<RespiratorComponent>(body))
                    Fail(id, WolfmedSpeciesCheck.Lungs);
                else if (!lungs.All(SEntMan.HasComponent<WolfmedOrganComponent>))
                    Fail(id, WolfmedSpeciesCheck.LungData);

                // 29% blood stops the heart (or collapses the circulation).
                if (!SEntMan.HasComponent<BloodstreamComponent>(body))
                {
                    Fail(id, WolfmedSpeciesCheck.BloodArrest);
                }
                else
                {
                    s.SetBlood(body, 0.29f);
                    s.Advance(body, 1);
                    if (!s.Life.InArrest(body))
                        Fail(id, WolfmedSpeciesCheck.BloodArrest);
                }

                // Taking the brain out kills.
                var second = SEntMan.SpawnEntity(species.Prototype, map.GridCoords);
                var brainOut = graph.GetBodyOrgans(second)
                    .FirstOrDefault(organ => SEntMan.HasComponent<BrainComponent>(organ.Id)).Id;
                if (brainOut == default || !graph.RemoveOrgan(brainOut) || !mobState.IsDead(second))
                    Fail(id, WolfmedSpeciesCheck.BrainRemoval);

                SEntMan.DeleteEntity(body);
                SEntMan.DeleteEntity(second);
            }
        });

        // A synth's battery system puts its starting cell in on its first update.
        await RunSeconds(2);

        // The power source out, for every machine at once: an IPC's cell slot, a synth's battery organ slot.
        await Server.WaitAssertion(() =>
        {
            foreach (var (species, body) in machines)
            {
                if (!PullPower(body))
                    Fail(species, WolfmedSpeciesCheck.PowerShutdown);
            }
        });

        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            var graph = SEntMan.System<SharedBodySystem>();
            var shutdown = SEntMan.System<WolfmedShutdownSystem>();
            var mobState = SEntMan.System<MobStateSystem>();

            foreach (var (species, body) in machines)
            {
                if (!shutdown.IsShutDown(body))
                    Fail(species, WolfmedSpeciesCheck.PowerShutdown);

                var core = graph.GetBodyOrgans(body).FirstOrDefault(organ => SEntMan.HasComponent<BrainComponent>(organ.Id)).Id;
                if (core == default || !graph.RemoveOrgan(core) || !mobState.IsDead(body))
                    Fail(species, WolfmedSpeciesCheck.CoreRemoval);
            }
        });

        var problems = new List<string>();
        var exceptions = protoManager.EnumeratePrototypes<WolfmedSpeciesExceptionPrototype>().ToDictionary(e => e.ID);
        foreach (var (species, checks) in failed.OrderBy(pair => pair.Key, System.StringComparer.Ordinal))
        {
            exceptions.TryGetValue(species, out var exception);
            var mechanical = ladders.GetValueOrDefault(species);
            if ((exception?.Mechanical ?? false) != mechanical)
                problems.Add($"{species}: runs the {(mechanical ? "machine" : "organic")} ladder, its exception says otherwise");

            var excused = exception?.Excuses.ToHashSet() ?? new HashSet<WolfmedSpeciesCheck>();
            foreach (var check in checks.Where(check => !excused.Contains(check)).OrderBy(check => check))
                problems.Add($"{species}: fails {check}");

            foreach (var check in excused.Where(check => !checks.Contains(check)).OrderBy(check => check))
                problems.Add($"{species}: excused from {check}, which it now passes");

            TestContext.Out.WriteLine($"{species}: {(mechanical ? "machine" : "organic")}" +
                                      (checks.Count == 0 ? ", conforms" : $", fails {string.Join(", ", checks)}") +
                                      (exception != null ? $" (exception: {exception.Reason})" : string.Empty));
        }

        foreach (var stale in exceptions.Keys.Where(id => !failed.ContainsKey(id)))
            problems.Add($"{stale}: an exception for a species that is not round-start");

        Assert.That(problems, Is.Empty, "species conformance (plan §9.1): " + string.Join("; ", problems));
    }

    /// <summary>Takes the power source out: the cell slot an IPC has, or the cell in a synth's battery organ slot.</summary>
    private bool PullPower(EntityUid body)
    {
        var containers = SEntMan.System<SharedContainerSystem>();
        if (SEntMan.TryGetComponent(body, out SynthBatteryComponent? synth))
        {
            if (!SEntMan.System<SynthBatterySystem>().TryGetBatteryContainer(body, synth.OrganSlot, out _, out var container) ||
                container.ContainedEntities.Count == 0)
                return false;

            return containers.Remove(container.ContainedEntities[0], container);
        }

        if (!SEntMan.System<ItemSlotsSystem>().TryGetSlot(body, "cell_slot", out var slot) || slot.Item is not { } cell)
            return false;

        return containers.Remove(cell, slot.ContainerSlot!);
    }
}
