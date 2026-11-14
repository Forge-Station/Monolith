#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.PlanetNatives;
using Content.Shared._WF.PlanetNatives;
using Content.Shared._WF.Planets;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.PlanetNatives;

/// <summary>Planet NPCs of every kind leave each other alone, and still go for an outsider.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetNativeSystem))]
public sealed class PlanetNativeTruceTest
{
    private static readonly string[] Natives =
    {
        "MobGiantSpiderAngry", "WFCityResident", "WFTempleSkeleton", "WFWreckZombieVerda", "WFStrangerMugger",
        "WeaponTurretLaserTSFMC",
    };

    [Test]
    public async Task NativesLeaveEachOtherAlone()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var planet = await pair.CreateTestMap();
        var plain = await pair.CreateTestMap();
        var natives = new List<EntityUid>();
        EntityUid outsider = default;
        EntityUid spiderOffWorld = default;
        EntityUid residentOffWorld = default;
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            foreach (var map in new[] { planet, plain })
            {
                for (var x = -8; x <= 8; x++)
                for (var y = -2; y <= 2; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            }

            entMan.EnsureComponent<WFPlanetLayerComponent>(planet.MapUid);
            for (var i = 0; i < Natives.Length; i++)
                natives.Add(entMan.SpawnEntity(Natives[i], new EntityCoordinates(planet.Grid.Owner, -7.5f + i * 2f, 0.5f)));
            // A brainless body walking in from elsewhere is no native.
            outsider = entMan.SpawnEntity("MobHuman", new EntityCoordinates(planet.Grid.Owner, 0.5f, 1.5f));

            spiderOffWorld = entMan.SpawnEntity("MobGiantSpiderAngry", new EntityCoordinates(plain.Grid.Owner, 0.5f, 0.5f));
            residentOffWorld = entMan.SpawnEntity("WFCityResident", new EntityCoordinates(plain.Grid.Owner, 2.5f, 0.5f));
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var factions = server.System<NpcFactionSystem>();
            // Off a planet, the same two would fight: the truce is what keeps them apart.
            Assert.That(factions.GetNearbyHostiles(spiderOffWorld, 10f), Does.Contain(residentOffWorld));
            Assert.That(entMan.HasComponent<WFPlanetNativeComponent>(spiderOffWorld), Is.False);

            foreach (var native in natives)
            {
                var id = entMan.GetComponent<MetaDataComponent>(native).EntityPrototype!.ID;
                Assert.That(entMan.HasComponent<WFPlanetNativeComponent>(native), Is.True, $"{id} is not a native.");
                var hostiles = factions.GetNearbyHostiles(native, 20f).ToList();
                Assert.That(hostiles.Intersect(natives), Is.Empty, $"{id} still goes for other natives.");
            }

            Assert.That(entMan.HasComponent<WFPlanetNativeComponent>(outsider), Is.False);
            Assert.That(factions.GetNearbyHostiles(natives[0], 20f), Does.Contain(outsider), "The spider ignores an outsider.");
        });

        await pair.CleanReturnAsync();
    }
}
