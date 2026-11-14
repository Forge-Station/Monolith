#nullable enable
using System.Linq;
using Content.Shared._WF.RMCCore.Actions;
using Content.Shared._WF.Xenonids.Acid;
using Content.Shared._WF.Xenonids.Plasma;
using Content.Shared.Actions;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>A drone's corrosive acid melts station structures.</summary>
[TestFixture]
public sealed class XenoAcidStructureTest
{
    [TestCase("WallSolid")]
    [TestCase("Airlock")]
    public async Task DroneAcidMeltsStructure(string targetProto)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid drone = default, target = default;
        await server.WaitPost(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            maps.SetTile(map.Grid, new Vector2i(1, 0), map.Tile.Tile);
            target = entities.SpawnEntity(targetProto, new EntityCoordinates(map.Grid, 1.5f, 0.5f));
            drone = entities.SpawnEntity("CMXenoDrone", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var plasma = entities.GetComponent<XenoPlasmaComponent>(drone);
            entities.System<XenoPlasmaSystem>().SetPlasma((drone, plasma), plasma.MaxPlasma);

            var action = entities.System<SharedRMCActionsSystem>().GetActionsWithEvent<XenoCorrosiveAcidEvent>(drone).First();
            var targetAction = entities.GetComponent<EntityTargetActionComponent>(action);
            Assert.That(entities.System<SharedActionsSystem>().ValidateEntityTarget(drone, target, (action, targetAction)),
                $"The acid action should accept an adjacent {targetProto}.");

            var proto = (XenoCorrosiveAcidEvent) targetAction.Event!;
            var ev = new XenoCorrosiveAcidEvent
            {
                Performer = drone,
                Target = target,
                Action = action,
                AcidId = proto.AcidId,
                Strength = proto.Strength,
                PlasmaCost = proto.PlasmaCost,
                Dps = proto.Dps,
                Time = TimeSpan.FromSeconds(1),
            };
            entities.EventBus.RaiseLocalEvent(drone, ev);
            Assert.That(ev.Handled, "The drone should start applying acid.");
        });

        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
            Assert.That(entities.HasComponent<TimedCorrodingComponent>(target), "The acid should be on the target once applied."));

        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
            Assert.That(entities.Deleted(target), $"The acid should melt the {targetProto}."));

        await pair.CleanReturnAsync();
    }
}
