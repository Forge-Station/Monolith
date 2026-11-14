using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Lattice;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._WF.Lattice;
using Content.Shared.Body.Systems;
using Content.Shared.Stacks;
using Content.Shared.Traits.Assorted;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Lattice;

/// <summary>Excising the song frees a Choir member, uses up a shard, and lets them be revived again.</summary>
public sealed class WFSongExcisionTest
{
    [Test]
    public async Task ExcisionFreesAMember()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var patient = entities.SpawnEntity("MobHuman", map.GridCoords);
            var surgeon = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.System<WFChoirSystem>().Join(patient);
            Assert.That(entities.HasComponent<UnrevivableComponent>(patient), Is.True);

            var head = entities.System<SharedBodySystem>().GetBodyChildren(patient).First(part => part.Component.PartType == Content.Shared.Body.Part.BodyPartType.Head).Id;
            var shards = entities.SpawnEntity("WFMaterialExoticMatter", map.GridCoords);
            var before = entities.GetComponent<StackComponent>(shards).Count;

            var step = entities.SpawnEntity("WFLatticeStepExciseSong", map.GridCoords);
            entities.GetComponent<WFSongExcisionEffectComponent>(step).FailChance = 0f;
            var ev = new SurgeryStepEvent(surgeon, patient, head, new List<EntityUid> { shards }, step);
            entities.EventBus.RaiseLocalEvent(step, ref ev);

            Assert.That(entities.HasComponent<WFChoirMemberComponent>(patient), Is.False, "the song is still in them.");
            Assert.That(entities.HasComponent<UnrevivableComponent>(patient), Is.False, "a freed member still can't be revived.");
            Assert.That(entities.GetComponent<StackComponent>(shards).Count, Is.EqualTo(before - 1), "the excision didn't use a shard.");
        });

        await pair.CleanReturnAsync();
    }
}
