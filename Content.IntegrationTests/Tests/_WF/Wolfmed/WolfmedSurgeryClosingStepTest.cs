#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Shitmed.Medical.Surgery;
using Content.Server.Atmos.Components;
using Content.Server.Temperature.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Steps.Parts;
using Content.Shared._WF.Wolfmed.Surgery;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Standing;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Playtest 5, "surgery fails at the graft the burned tissue step, saying it requires a previous step which is already
/// complete". The graft removes the charring the procedure was listed for, the procedure stopped validating, and its
/// closing step was refused without a word, leaving the torso open. Driven the way a surgeon drives it: the step
/// chosen in the surgery window, the tool in hand, the do-after run out. A begun procedure now stays open until its own
/// closing step, and an open part without charring still does not offer the graft.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedSurgeryConditionSystem))]
public sealed class WolfmedSurgeryClosingStepTest : GameTest
{
    private static readonly (string Surgery, string Step, string Tool)[] OpenIncision =
    {
        ("SurgeryOpenIncision", "SurgeryStepOpenIncisionScalpel", "Scalpel"),
        ("SurgeryOpenIncision", "SurgeryStepRetractSkin", "Retractor"),
        ("SurgeryOpenIncision", "SurgeryStepClampBleeders", "Hemostat"),
    };

    [Test]
    public async Task GraftIsClosedByItsOwnSealStepTest()
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default, torso = default, surgeon = default;

        await Server.WaitAssertion(() =>
        {
            (body, torso, surgeon) = Setup(map.GridCoords);
            SEntMan.System<WoundSystem>().CreateOrMergeWound(torso, "WFWolfmedCharringWound", 30);
        });

        foreach (var (surgery, step, tool) in OpenIncision)
            await Perform(body, torso, surgeon, surgery, step, tool);
        await Perform(body, torso, surgeon, "WFSurgeryGraftSkin", "WFSurgeryStepGraftSkin", "WFWolfmedSkinGraft");

        await Server.WaitAssertion(() =>
        {
            Assert.That(Wound(torso, "WFWolfmedCharringWound"), Is.False, "the graft did not take the charring.");
            Assert.That(Valid(body, torso, "WFSurgeryGraftSkin"), Is.True,
                "the graft stopped being a procedure once it removed the charring, so it cannot be sealed.");
        });

        await Perform(body, torso, surgeon, "WFSurgeryGraftSkin", "SurgeryStepSealTendWound", "Cautery");

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<IncisionOpenComponent>(torso), Is.False, "the seal step never ran: the torso is still open.");
                Assert.That(Wound(torso, "SurgicalIncisionWound"), Is.False, "the operative incision is still there.");
                Assert.That(Valid(body, torso, "WFSurgeryGraftSkin"), Is.False, "the graft is still offered after it was sealed.");
                Assert.That(SEntMan.TryGetComponent(torso, out WolfmedSurgeryProgressComponent? progress) && progress.Surgeries.Count > 0,
                    Is.False, "the sealed procedure is still recorded as open.");
            });
        });
    }

    /// <summary>The same rule for a procedure listed by something other than a wound: a lodged object.</summary>
    [Test]
    public async Task EmbeddedObjectRemovalIsClosedByItsOwnSealStepTest()
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default, torso = default, surgeon = default;

        await Server.WaitAssertion(() =>
        {
            (body, torso, surgeon) = Setup(map.GridCoords);
            var wound = SEntMan.System<WoundSystem>().CreateOrMergeWound(torso, "SlashWound", 10);
            Assert.That(wound, Is.Not.Null);
            Assert.That(SEntMan.System<WolfmedEmbeddedObjectSystem>().Add(wound!.Value, "WFWolfmedShrapnelFragment", 1, 1), Is.EqualTo(1));
        });

        foreach (var (surgery, step, tool) in OpenIncision)
            await Perform(body, torso, surgeon, surgery, step, tool);
        await Perform(body, torso, surgeon, "WFSurgeryRemoveEmbeddedObjects", "WFSurgeryStepExtractEmbedded", "Hemostat");

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<WolfmedEmbeddedObjectSystem>().GetPartCount(torso), Is.Zero, "the fragment is still in."));

        await Perform(body, torso, surgeon, "WFSurgeryRemoveEmbeddedObjects", "SurgeryStepSealTendWound", "Cautery");

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.HasComponent<IncisionOpenComponent>(torso), Is.False, "the seal step never ran: the torso is still open."));
    }

    /// <summary>Only a begun procedure is kept: an open torso with no charring does not offer the graft.</summary>
    [Test]
    public async Task OpenPartWithoutTheWoundDoesNotListTheProcedureTest()
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default, torso = default, surgeon = default;
        await Server.WaitAssertion(() => (body, torso, surgeon) = Setup(map.GridCoords));

        foreach (var (surgery, step, tool) in OpenIncision)
            await Perform(body, torso, surgeon, surgery, step, tool);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<BleedersClampedComponent>(torso), Is.True, "the torso did not open.");
            Assert.That(Valid(body, torso, "WFSurgeryGraftSkin"), Is.False, "an open torso offers a graft it does not need.");
        });
    }

    private (EntityUid Body, EntityUid Torso, EntityUid Surgeon) Setup(EntityCoordinates coordinates)
    {
        var body = SEntMan.SpawnEntity("MobVulpkanin", coordinates);
        var surgeon = SEntMan.SpawnEntity("MobHuman", coordinates);
        // The test map is vacuum; the patient and the surgeon are not what is being tested.
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
                $"{step} of {surgery} did not complete when chosen from the surgery window.");
        });
    }

    private bool Valid(EntityUid body, EntityUid torso, string surgery) =>
        SEntMan.System<SurgerySystem>().WolfmedSurgeryValid(body, torso, surgery);

    private bool Wound(EntityUid part, string prototype) =>
        SEntMan.System<WoundSystem>().GetWounds(part).Any(wound => wound.Comp.Prototype == prototype);
}
