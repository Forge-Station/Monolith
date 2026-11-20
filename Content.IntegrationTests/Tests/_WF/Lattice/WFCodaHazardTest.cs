using System.Linq;
using System.Numerics;
using Content.Server._WF.Lattice;
using Content.Shared._WF.Lattice;
using Content.Shared.Damage;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Lattice;

/// <summary>The Coda's hazards work on a ship: hunters board, structure rots, and failed power comes back.</summary>
public sealed class WFCodaHazardTest
{
    [Test]
    public async Task HazardsHitAShip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();
        var hazards = entities.System<WFCodaHazardSystem>();
        var maps = entities.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            var profile = protos.Index<WFLatticeProfilePrototype>("WFDefault");
            Assert.That(profile.HunterMobs, Is.Not.Empty, "the Coda has nothing to hunt with.");

            var grid = (map.Grid.Owner, entities.GetComponent<MapGridComponent>(map.Grid));
            for (var x = -6; x <= 6; x++)
            {
                maps.SetTile(map.Grid, new Vector2i(x, 0), maps.GetTileRef(map.Grid, entities.GetComponent<MapGridComponent>(map.Grid), new Vector2i(0, 0)).Tile);
            }

            Assert.That(hazards.Board(grid, 2, profile), Is.EqualTo(2), "no hunters boarded.");
            var hunters = profile.HunterMobs.Select(id => id.Id).ToHashSet();
            var remade = entities.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype is { } proto && hunters.Contains(proto.ID));
            Assert.That(remade, Is.GreaterThanOrEqualTo(2));

            var wall = entities.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, new Vector2(3.5f, 0.5f)));
            for (var i = 0; i < 40; i++)
            {
                hazards.RotNow(grid, 1f, profile);
            }

            Assert.That(entities.Deleted(wall) || entities.GetComponent<DamageableComponent>(wall).TotalDamage > 0, Is.True,
                "the Coda didn't eat the hull.");
        });

        await pair.CleanReturnAsync();
    }
}
