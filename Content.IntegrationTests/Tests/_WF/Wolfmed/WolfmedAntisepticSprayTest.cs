#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Medical;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Playtest 5, "targeting yourself with antiseptic spray makes you drink it": a click on yourself, or a use in hand,
/// sprays the antiseptic onto your skin and cleans the wound; nothing goes down your throat, and the bottle loses one
/// press. A click on somebody else does the same to them. The bottle's use delay is waited out between presses.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedAntisepticSpraySystem))]
public sealed class WolfmedAntisepticSprayTest : GameTest
{
    [Test]
    public async Task SprayOnYourselfCleansTheWoundInsteadOfDrinkingTest()
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default, other = default, spray = default, wound = default, otherWound = default;
        FixedPoint2 full = default;

        await Server.WaitAssertion(() =>
        {
            var infection = SEntMan.System<WolfmedInfectionSystem>();
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            other = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var slash = SProtoMan.Index<DamageTypePrototype>("Slash");
            foreach (var patient in new[] { body, other })
                SEntMan.System<DamageableSystem>().TryChangeDamage(patient, new DamageSpecifier(slash, FixedPoint2.New(15)),
                    origin: null, targetPart: TargetBodyPart.LeftArm);

            wound = InfectableWound(body);
            otherWound = InfectableWound(other);
            infection.Contaminate(wound);
            infection.Contaminate(otherWound);
            Assert.That(Contamination(wound), Is.GreaterThan(1f));

            spray = SEntMan.SpawnEntity("WFWolfmedAntisepticSpray", map.GridCoords);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(body, spray), Is.True);
            Assert.That(solutions.TryGetSolution(spray, "spray", out _, out var before), Is.True);
            full = before!.Volume;

            // Clicking yourself.
            var click = new AfterInteractEvent(body, spray, body, map.GridCoords, canReach: true);
            SEntMan.EventBus.RaiseLocalEvent(spray, click);
            Assert.That(click.Handled, Is.True, "the spray did nothing on a click on yourself.");

            var swallowed = solutions.TryGetSolution(body, "chemicals", out _, out var chemicals) && chemicals.ContainsPrototype("Ethanol") ||
                            solutions.TryGetSolution(body, "food", out _, out var stomach) && stomach.ContainsPrototype("Ethanol");
            Assert.Multiple(() =>
            {
                Assert.That(Volume(spray), Is.EqualTo(full - 10), "the bottle did not lose one press.");
                Assert.That(swallowed, Is.False, "the patient drank the antiseptic.");
                Assert.That(Contamination(wound), Is.EqualTo(1f), "the spray did not clean the wound.");
            });
        });

        // The bottle's use delay.
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            // Use in hand sprays yourself too.
            var use = new UseInHandEvent(body);
            SEntMan.EventBus.RaiseLocalEvent(spray, use);
            Assert.That(use.Handled, Is.True, "use in hand did nothing.");
            Assert.That(Volume(spray), Is.EqualTo(full - 20), "use in hand did not spray.");
        });

        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            // Clicking somebody else sprays them.
            var clickOther = new AfterInteractEvent(body, spray, other, map.GridCoords, canReach: true);
            SEntMan.EventBus.RaiseLocalEvent(spray, clickOther);
            Assert.That(clickOther.Handled, Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(Volume(spray), Is.EqualTo(full - 30));
                Assert.That(Contamination(otherWound), Is.EqualTo(1f), "the spray did not clean the other patient's wound.");
            });
        });
    }

    private EntityUid InfectableWound(EntityUid body)
    {
        var arm = SEntMan.System<SharedBodySystem>().GetBodyChildrenOfType(body, BodyPartType.Arm, symmetry: BodyPartSymmetry.Left).Single().Id;
        return SEntMan.System<WoundSystem>().GetWounds(arm).First(w => SEntMan.HasComponent<WolfmedInfectionComponent>(w)).Owner;
    }

    private float Contamination(EntityUid wound) => SEntMan.GetComponent<WolfmedInfectionComponent>(wound).Contamination;

    private FixedPoint2 Volume(EntityUid spray) =>
        SEntMan.System<SharedSolutionContainerSystem>().TryGetSolution(spray, "spray", out _, out var solution) ? solution!.Volume : FixedPoint2.Zero;
}
