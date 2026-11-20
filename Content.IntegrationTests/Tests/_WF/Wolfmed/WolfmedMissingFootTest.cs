#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Synthetics playtest: "when a foot is detached, the walk speed isn't changed (this might be a bug with organics
/// too)". A foot that is off slows the body as a disabled one does; a foot put back on gives the speed back.
/// </summary>
[TestFixture]
public sealed class WolfmedMissingFootTest : WolfmedGameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestCase("MobHuman")]
    [TestCase("MobIPC")]
    public async Task MissingFootSlowsTest(string mobId)
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, WolfmedIvDripTest.StationAir());
            var body = SEntMan.SpawnEntity(mobId, map.GridCoords);
            var graph = SEntMan.System<SharedBodySystem>();
            var movement = SEntMan.System<MovementSpeedModifierSystem>();
            var speed = SEntMan.GetComponent<MovementSpeedModifierComponent>(body);
            var foot = graph.GetBodyChildrenOfType(body, BodyPartType.Foot, symmetry: BodyPartSymmetry.Left).Single().Id;
            var leg = graph.GetParentPartOrNull(foot)!.Value;
            var slot = graph.GetParentPartAndSlotOrNull(foot)!.Value.Slot;

            movement.RefreshMovementSpeedModifiers(body);
            var whole = speed.SprintSpeedModifier;

            Assert.That(SEntMan.System<WolfmedBodySystem>().TryDetachPart(foot), Is.True, "the foot did not come off.");
            movement.RefreshMovementSpeedModifiers(body);
            Assert.That(speed.SprintSpeedModifier, Is.LessThan(whole), "a missing foot does not slow the body.");

            Assert.That(graph.AttachPart(leg, slot, foot), Is.True, "the foot did not go back on.");
            var enable = new BodyPartEnableChangedEvent(true);
            SEntMan.EventBus.RaiseLocalEvent(foot, ref enable);
            movement.RefreshMovementSpeedModifiers(body);
            Assert.That(speed.SprintSpeedModifier, Is.EqualTo(whole).Within(0.001f), "the foot back on did not give the speed back.");
        });
    }
}
