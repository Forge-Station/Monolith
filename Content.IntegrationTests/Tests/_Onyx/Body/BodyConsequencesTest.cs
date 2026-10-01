using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE(Wolfmed): §2.7, Onyx's SharedBodySystem.TryDetachPart lives here.
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Standing;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Onyx.Body;

/// <remarks>
/// WOLFGATE(Wolfmed): Onyx's version asserts Nubody behaviour Wolfgate does not have. Its `Groin` part does not exist
/// here (D9) and no Wolfgate system couples inventory slots to body parts (grep: nothing in
/// Content.{Shared,Server}/Inventory subscribes BodyPartRemovedEvent/BodyPartDroppedEvent), so the shoes /
/// socks / underwear assertions are dropped. `StandUpAttemptEvent` does not exist either (PLAN §6.1). What
/// survives is the part of the contract the wound bridge can actually break: detaching a part must still
/// cascade to its children and still put a legless mob down, with GUARDs B/C in place on a wound host.
/// </remarks>
[TestFixture]
public sealed class BodyConsequencesTest : GameTest
{
    [Test]
    public async Task DetachingLegRemovesChildFootTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("MobHuman", map.GridCoords);
            var graph = entityManager.System<SharedBodySystem>();
            var wfBody = entityManager.System<WolfmedBodySystem>();
            var leg = graph.GetBodyChildrenOfType(body, BodyPartType.Leg).First().Id;
            var feetBefore = graph.GetBodyChildrenOfType(body, BodyPartType.Foot).Count();

            Assert.That(wfBody.TryDetachPart(leg), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(graph.BodyHasChild(body, leg), Is.False);
                Assert.That(graph.GetBodyChildrenOfType(body, BodyPartType.Foot).Count(),
                    Is.EqualTo(feetBefore - 1));
            });
        });
    }

    [Test]
    public async Task DetachingEveryLegPreventsStandingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entityManager.SpawnEntity("MobHuman", map.GridCoords);
            var graph = entityManager.System<SharedBodySystem>();
            var wfBody = entityManager.System<WolfmedBodySystem>();

            // WOLFGATE(Wolfmed): Wolfgate goes down at zero legs, not at one (SharedBodySystem.Parts.cs:390).
            foreach (var leg in graph.GetBodyChildrenOfType(body, BodyPartType.Leg).ToArray())
                Assert.That(wfBody.TryDetachPart(leg.Id), Is.True);

            Assert.That(graph.BodyHasPartType(body, BodyPartType.Leg), Is.False);
            Assert.That(entityManager.System<StandingStateSystem>().IsDown(body), Is.True);
        });
    }
}
