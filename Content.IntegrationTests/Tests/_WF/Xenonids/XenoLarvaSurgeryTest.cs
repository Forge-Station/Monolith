#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Server._Shitmed.Medical.Surgery;
using Content.Server.Atmos.Components;
using Content.Server.Temperature.Components;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._WF.Xenonids.Parasite;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Standing;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>Larva removal: the surgery as a surgeon drives it, and the removal method the surgery calls.</summary>
[TestFixture]
public sealed class XenoLarvaSurgeryTest : WolfmedGameTest
{
    private const string Surgery = "WFSurgeryAlienEmbryoRemoval";

    private static readonly (string Surgery, string Step, string Tool)[] OpenRibcage =
    {
        ("SurgeryOpenIncision", "SurgeryStepOpenIncisionScalpel", "Scalpel"),
        ("SurgeryOpenIncision", "SurgeryStepRetractSkin", "Retractor"),
        ("SurgeryOpenIncision", "SurgeryStepClampBleeders", "Hemostat"),
        ("SurgeryOpenRibcage", "SurgeryStepSawBones", "Saw"),
        ("SurgeryOpenRibcage", "SurgeryStepPriseOpenBones", "Retractor"),
    };

    /// <summary>The surgery is listed only on an infected patient, cuts the roots, then removes the larva.</summary>
    [Test]
    public async Task SurgeryRemovesTheLarvaTest()
    {
        var map = await CreateTestMap();
        EntityUid body = default, torso = default, surgeon = default;
        await Server.WaitAssertion(() => (body, torso, surgeon) = Setup(map.GridCoords));

        foreach (var (surgery, step, tool) in OpenRibcage)
            await Perform(body, torso, surgeon, surgery, step, tool);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Valid(body, torso), Is.False, "the surgery lists on a patient with no larva.");
            SEntMan.EnsureComponent<VictimInfectedComponent>(body);
            Assert.That(Valid(body, torso), Is.True, "the surgery is not listed on an infected patient.");
        });

        await Perform(body, torso, surgeon, Surgery, "WFSurgeryStepCutLarvaRoots", "Scalpel");
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<VictimInfectedComponent>(body).RootsCut, Is.True, "the roots were not cut."));

        await Perform(body, torso, surgeon, Surgery, "WFSurgeryStepRemoveLarva", "Hemostat");

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<VictimInfectedComponent>(body), Is.False, "the infection survived the surgery.");
            Assert.That(CountEntities("RMCXenoEmbryo"), Is.EqualTo(1), "no dead embryo was left.");
            Assert.That(Valid(body, torso), Is.False, "the surgery still lists after the larva is out.");
        });
    }

    /// <summary>An unhatched larva leaves a dead embryo and never hatches; the removal refuses a clean host.</summary>
    [Test]
    public async Task RemovalEndsIncubationTest()
    {
        var map = await CreateTestMap();
        var parasites = SEntMan.System<SharedXenoParasiteSystem>();
        EntityUid host = default, parasite = default;
        await Server.WaitPost(() =>
        {
            host = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            parasite = SEntMan.SpawnEntity("CMXenoParasite", map.GridCoords);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(parasites.TryRemoveLarva(host), Is.False, "a clean host reported a removal.");
            Assert.That(parasites.Infect((parasite, SEntMan.GetComponent<XenoParasiteComponent>(parasite)), host, false, true), Is.True);
        });
        await RunSeconds(17);

        await Server.WaitAssertion(() =>
        {
            var infected = SEntMan.GetComponent<VictimInfectedComponent>(host);
            Assert.That(infected.SpawnedLarva, Is.Null);
            parasites.SetBurstDelay((host, infected), TimeSpan.FromSeconds(2));
            Assert.That(parasites.TryRemoveLarva(host), Is.True);
            Assert.That(SEntMan.HasComponent<VictimInfectedComponent>(host), Is.False);
        });

        await RunSeconds(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<VictimInfectedComponent>(host), Is.False);
            Assert.That(CountEntities("CMXenoLarva"), Is.Zero, "a live larva hatched after the removal.");
            Assert.That(CountEntities("RMCXenoEmbryo"), Is.EqualTo(1));
        });
    }

    /// <summary>A larva that has already hatched inside the host is freed alive.</summary>
    [Test]
    public async Task RemovalFreesAHatchedLarvaTest()
    {
        var map = await CreateTestMap();
        var parasites = SEntMan.System<SharedXenoParasiteSystem>();
        EntityUid host = default;
        await Server.WaitPost(() => host = SEntMan.SpawnEntity("MobHuman", map.GridCoords));
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            var infected = SEntMan.EnsureComponent<VictimInfectedComponent>(host);
            parasites.SetBurstDelay((host, infected), TimeSpan.FromSeconds(1));
        });
        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<VictimInfectedComponent>(host).SpawnedLarva, Is.Not.Null, "no larva hatched.");
            Assert.That(parasites.TryRemoveLarva(host), Is.True);
            Assert.That(SEntMan.HasComponent<VictimInfectedComponent>(host), Is.False);
            Assert.That(CountEntities("CMXenoLarva"), Is.EqualTo(1), "the larva was lost.");
            Assert.That(CountEntities("RMCXenoEmbryo"), Is.Zero);
        });
    }

    private int CountEntities(string prototype) =>
        SEntMan.AllEntities<MetaDataComponent>().Count(e => e.Comp.EntityPrototype?.ID == prototype);

    private (EntityUid Body, EntityUid Torso, EntityUid Surgeon) Setup(EntityCoordinates coordinates)
    {
        var body = SEntMan.SpawnEntity("MobHuman", coordinates);
        var surgeon = SEntMan.SpawnEntity("MobHuman", coordinates);
        foreach (var mob in new[] { body, surgeon })
        {
            SEntMan.RemoveComponent<BarotraumaComponent>(mob);
            SEntMan.RemoveComponent<TemperatureComponent>(mob);
        }

        SEntMan.System<StandingStateSystem>().Down(body);
        var torso = SEntMan.System<SharedBodySystem>().GetBodyChildrenOfType(body, BodyPartType.Torso).Single().Id;
        return (body, torso, surgeon);
    }

    /// <summary>One step as the surgery window sends it, with the tool in hand, then the do-after run out.</summary>
    private async Task Perform(EntityUid body, EntityUid torso, EntityUid surgeon, string surgery, string step, string tool)
    {
        await Server.WaitAssertion(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            foreach (var held in hands.EnumerateHeld(surgeon).ToList())
                SEntMan.DeleteEntity(held);
            var item = SEntMan.SpawnEntity(tool, SEntMan.GetComponent<TransformComponent>(surgeon).Coordinates);
            Assert.That(hands.TryPickupAnyHand(surgeon, item, checkActionBlocker: false), Is.True, $"the surgeon cannot hold the {tool}.");

            var chosen = new SurgeryStepChosenBuiMsg(SEntMan.GetNetEntity(torso), surgery, step, false)
            {
                UiKey = SurgeryUIKey.Key,
                Actor = surgeon,
                Entity = SEntMan.GetNetEntity(body),
            };
            SEntMan.EventBus.RaiseLocalEvent(body, chosen);
        });

        await RunSeconds(8);

        await Server.WaitAssertion(() =>
        {
            var surgerySystem = SEntMan.System<SurgerySystem>();
            Assert.That(surgerySystem.IsStepComplete(body, torso, step, surgerySystem.GetSingleton(surgery)!.Value), Is.True,
                $"{step} of {surgery} did not complete.");
        });
    }

    private bool Valid(EntityUid body, EntityUid torso) =>
        SEntMan.System<SurgerySystem>().WolfmedSurgeryValid(body, torso, Surgery);
}
