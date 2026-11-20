#nullable enable
using System.Linq;
using Content.Server._Mono.FireControl;
using Content.Server._WF.GunneryChips;
using Content.Shared.Interaction;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.GunneryChips;

/// <summary>Gunnery chips fit into a server up to the limit, and wreck crates come packed with guns or chips.</summary>
[TestFixture]
[TestOf(typeof(WFGunneryChipSystem))]
public sealed class GunneryChipTest
{
    [Test]
    public async Task ChipsRaiseAServersProcessingPowerUpToTheLimit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var gunnery = entMan.SpawnEntity("GunneryServerLow", map.GridCoords);
            var user = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var comp = entMan.GetComponent<FireControlServerComponent>(gunnery);
            var start = comp.ProcessingPower;
            var interaction = server.System<SharedInteractionSystem>();

            foreach (var id in new[] { "WFGunneryChipBasic", "WFGunneryChipAdvanced", "WFGunneryChipMilitary", "WFGunneryChipMilitary" })
            {
                var chip = entMan.SpawnEntity(id, map.GridCoords);
                var ev = new InteractUsingEvent(user, chip, gunnery, entMan.GetComponent<TransformComponent>(gunnery).Coordinates);
                entMan.EventBus.RaiseLocalEvent(gunnery, ev);
            }

            Assert.That(comp.ProcessingPower, Is.EqualTo(start + 6 + 12 + 24), "Three chips should fit and the fourth be refused.");
            Assert.That(entMan.GetComponent<WFGunneryChipsComponent>(gunnery).Fitted, Is.EqualTo(WFGunneryChipSystem.MaxChips));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WreckCratesComePacked()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var containers = server.System<SharedContainerSystem>();
            for (var i = 0; i < 10; i++)
            {
                var guns = entMan.SpawnEntity("WFWreckShipgunCrate", map.GridCoords);
                var packed = containers.GetContainer(guns, "entity_storage").ContainedEntities
                    .Select(item => entMan.GetComponent<MetaDataComponent>(item).EntityPrototype!.ID).ToList();
                Assert.That(packed.Count(id => id.StartsWith("WFFlatpackTurret")), Is.EqualTo(1), "An armament crate holds one gun.");
                Assert.That(packed.Count(id => id.StartsWith("WFFlatpackHardpoint")), Is.EqualTo(1), "...and the hardpoint it mounts on.");

                var chips = entMan.SpawnEntity("WFWreckGunneryChipCrate", map.GridCoords);
                Assert.That(containers.GetContainer(chips, "entity_storage").ContainedEntities
                    .All(chip => entMan.HasComponent<WFGunneryChipComponent>(chip)), Is.True);
                Assert.That(containers.GetContainer(chips, "entity_storage").ContainedEntities, Has.Count.InRange(1, 2));
            }
        });
        await pair.CleanReturnAsync();
    }
}
