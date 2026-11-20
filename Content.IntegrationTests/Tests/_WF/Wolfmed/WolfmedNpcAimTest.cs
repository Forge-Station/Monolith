#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Targeting;
using Content.Shared._Shitmed.Targeting;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>An NPC's aim moves around the body instead of staying on the torso; a body with no NPC brain keeps its own.</summary>
[TestFixture]
[TestOf(typeof(WolfmedNpcAimSystem))]
public sealed class WolfmedNpcAimTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedNpcAimTestMob
  parent: MobHuman
  components:
  - type: HTN
    rootTask:
      task: IdleCompound
";

    [Test]
    public async Task NpcAimMovesTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid npc = default, player = default;
        var seen = new HashSet<TargetBodyPart>();

        await server.WaitPost(() =>
        {
            npc = entMan.SpawnEntity("WolfmedNpcAimTestMob", map.GridCoords);
            player = entMan.SpawnEntity("MobHuman", map.GridCoords);
        });

        for (var i = 0; i < 30; i++)
        {
            await pair.RunTicksSync(30);
            await server.WaitPost(() => seen.Add(entMan.GetComponent<TargetingComponent>(npc).Target));
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(seen, Has.Count.GreaterThanOrEqualTo(4), "the NPC's aim barely moved off one part.");
            Assert.That(seen, Has.Some.Not.EqualTo(TargetBodyPart.Torso), "the NPC only ever aims at the torso.");
            Assert.That(entMan.GetComponent<TargetingComponent>(player).Target, Is.EqualTo(TargetBodyPart.Torso),
                "a body with no NPC brain had its aim moved for it.");
            entMan.DeleteEntity(npc);
            entMan.DeleteEntity(player);
        });

        await pair.CleanReturnAsync();
    }
}
