#nullable enable
using Content.Shared._WF.Xenonids.Construction.Events;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared._WF.Xenonids.Plasma;
using Content.Shared.Doors.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>Station walls hold up resin doors the way RMC's walls do.</summary>
[TestFixture]
public sealed class XenoConstructionSupportTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: CMXenoDrone
  id: WFTestXenoDoorBuilder
  components:
  - type: XenoConstruction
    buildDelay: 0.5
    canBuild:
    - DoorXenoResin
    buildChoice: DoorXenoResin
";

    [TestCase("WallPlastitanium", false)]
    [TestCase("WallSolid", false)]
    [TestCase("WallSolid", true)]
    public async Task ResinDoorBetweenStationWalls(string wallProto, bool eastWest)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid drone = default;
        EntityCoordinates doorCoords = default;
        await server.WaitPost(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            for (var x = 0; x <= 2; x++)
                for (var y = -1; y <= 1; y++)
                    maps.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);

            doorCoords = new EntityCoordinates(map.Grid, 1.5f, 0.5f);
            if (eastWest)
            {
                // walls left and right of the door; the drone stands below it
                entities.SpawnEntity(wallProto, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
                entities.SpawnEntity(wallProto, new EntityCoordinates(map.Grid, 2.5f, 0.5f));
            }
            else
            {
                entities.SpawnEntity(wallProto, new EntityCoordinates(map.Grid, 1.5f, 1.5f));
                entities.SpawnEntity(wallProto, new EntityCoordinates(map.Grid, 1.5f, -0.5f));
            }

            var hives = entities.System<SharedXenoHiveSystem>();
            var hive = entities.SpawnEntity("CMXenoHive", MapCoordinates.Nullspace);
            hives.SetHive(entities.SpawnEntity("XenoWeeds", doorCoords), hive);

            drone = entities.SpawnEntity("WFTestXenoDoorBuilder", eastWest ? new EntityCoordinates(map.Grid, 1.5f, -0.5f) : new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            hives.SetHive(drone, hive);
            var plasma = entities.GetComponent<XenoPlasmaComponent>(drone);
            entities.System<XenoPlasmaSystem>().SetPlasma((drone, plasma), plasma.MaxPlasma);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var ev = new XenoSecreteStructureActionEvent { Performer = drone, Target = doorCoords };
            entities.EventBus.RaiseLocalEvent(drone, ev);
            Assert.That(ev.Handled, $"A resin door between two {wallProto} should start building.");
        });

        await pair.RunSeconds(2);

        await server.WaitAssertion(() =>
        {
            var found = false;
            var lookup = entities.System<EntityLookupSystem>();
            foreach (var uid in lookup.GetEntitiesIntersecting(doorCoords, LookupFlags.Static))
            {
                if (entities.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "DoorXenoResin" &&
                    entities.HasComponent<DoorComponent>(uid))
                {
                    found = true;
                    var rotation = entities.GetComponent<TransformComponent>(uid).LocalRotation.Degrees;
                    Assert.That(rotation, Is.EqualTo(eastWest ? 90 : 0).Within(0.01), "The door turns to span the walls beside it.");
                }
            }

            Assert.That(found, $"The resin door should stand between the {wallProto} walls.");
        });

        await pair.CleanReturnAsync();
    }
}
