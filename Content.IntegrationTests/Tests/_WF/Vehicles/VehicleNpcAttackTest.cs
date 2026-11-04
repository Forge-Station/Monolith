#nullable enable
using System.Numerics;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>A space carp, hostile to the crew, goes for the vehicle they sit in and leaves it alone once it is empty.</summary>
[TestFixture]
public sealed class VehicleNpcAttackTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: WFInteriorTestVehicle
  id: WFNpcTargetTestVehicle
  components:
  - type: Damageable
    damageContainer: StructuralInorganic
";

    [Test]
    public async Task AHostileMobAttacksAnOccupiedVehicle()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid vehicle = default, user = default;
        var spot = new Vector2(5.5f, 5.5f);

        await server.WaitPost(() =>
        {
            // Nobody is connected; without this the carp sleeps.
            server.CfgMan.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, false);

            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -4; x < 16; x++)
            for (var y = -4; y < 16; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));

            vehicle = entMan.SpawnEntity("WFNpcTargetTestVehicle", new EntityCoordinates(map.Grid.Owner, spot));
            user = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, spot + new Vector2(1f, 0f)));
        });
        await server.WaitRunTicks(3);
        await server.WaitPost(() => server.System<SharedInteractionSystem>().InteractionActivate(user, vehicle));
        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(user).MapID, Is.EqualTo(entMan.GetComponent<VehicleInteriorComponent>(vehicle).MapId),
                "Test setup: the user did not get in.");
            entMan.SpawnEntity("MobCarp", new EntityCoordinates(map.Grid.Owner, spot + new Vector2(0f, 4f)));
        });

        await pair.RunTicksSync(pair.SecondsToTicks(15));

        await server.WaitAssertion(() =>
        {
            var damage = entMan.GetComponent<DamageableComponent>(vehicle).TotalDamage;
            Assert.That(damage.Float(), Is.GreaterThan(0f), "The carp never attacked the occupied vehicle.");
        });

        await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, CCVars.NPCPauseWhenNoPlayersInRange.DefaultValue));
        await pair.CleanReturnAsync();
    }
}
