#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.Caverns;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Atmosphere;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._DV.Planet;
using Content.Shared._WF.Caverns;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Atmos;
using Content.Shared.Light.Components;
using Content.Shared.Parallax;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>The cavern hooks: nothing below ground with caverns off, one cavern at depth -1 under every world with them on.</summary>
[TestFixture]
[TestOf(typeof(WFCavernSystem))]
public sealed class CavernNetworkTest
{
    /// <summary>Layer roles for WFSurfaceAsclepiu: 0 ground, 1-3 air, 4 orbit.</summary>
    private const int AsclepiuLayerCount = 5;

    /// <summary>With the CVar at its default the network is exactly the Planets stack.</summary>
    [Test]
    public async Task CavernsOffLeavesNetworkUntouched()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await PlanetFixture.EnableFeature(pair);

        await server.WaitAssertion(() =>
            Assert.That(server.CfgMan.GetCVar(CavernCVars.Caverns), Is.False, "Precondition: wf.caverns defaults to off."));

        var world = await BuildWorld(pair, "WFSurfaceAsclepiu");

        await server.WaitAssertion(() =>
        {
            var zNetwork = entMan.GetComponent<CEZMapNetworkComponent>(world.Network);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(world.LowerLayers, Is.Empty, "A cavern was built with wf.caverns off.");
                Assert.That(zNetwork.ZLevels.Keys.Where(depth => depth < 0), Is.Empty,
                    "The z-network has a depth below ground with wf.caverns off.");
                Assert.That(world.Layers, Has.Count.EqualTo(AsclepiuLayerCount), "The Planets stack changed shape.");
                Assert.That(entMan.HasComponent<WFCavernGroundComponent>(world.Ground), Is.False,
                    "The ground points at a cavern that was never built.");
            }
        });

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>Each of the six worlds, built and torn down in turn, gets one fitted-out cavern at depth -1.</summary>
    [Test]
    public async Task EveryWorldGetsOneCavern()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var zLevels = server.System<CEZLevelsSystem>();
        var atmos = server.System<AtmosphereSystem>();
        var caverns = server.System<WFCavernSystem>();

        await EnableCaverns(pair);

        foreach (var surfaceId in Surfaces)
        {
            var world = await BuildWorld(pair, surfaceId);

            // Tear down even on a failed assertion, so the pair goes back to the pool without planet maps.
            try
            {
                await server.WaitAssertion(() =>
                {
                    var surface = proto.Index<WFPlanetSurfacePrototype>(surfaceId);

                    Assert.That(caverns.TryGetCavern(surfaceId, out var cavernProto), Is.True, $"{surfaceId} has no wfCavern.");
                    Assert.That(world.LowerLayers, Has.Count.EqualTo(1), $"{surfaceId} should have exactly one map below ground.");

                    var level = proto.Index<PlanetPrototype>(cavernProto!.Level);
                    var cavern = world.Cavern;
                    var expectedLayers = surface.AirLayers + (surface.CloudLayer ? 1 : 0) + 2;

                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(world.Layers, Has.Count.EqualTo(expectedLayers), $"{surfaceId}: Layers changed shape.");
                        Assert.That(world.Layers, Does.Not.Contain(cavern), $"{surfaceId}: the cavern leaked into Layers.");
                        if (surfaceId == "WFSurfaceAsclepiu")
                        {
                            Assert.That(world.Layers, Has.Count.EqualTo(AsclepiuLayerCount),
                                "PlanetNetworkTest's five-layer Asclepiu stack changed with caverns on.");
                        }

                        var zMap = entMan.GetComponent<CEZMapComponent>(cavern);
                        Assert.That(zMap.Depth, Is.EqualTo(-1), $"{surfaceId}: the cavern is at the wrong depth.");
                        Assert.That(zMap.NetworkUid, Is.EqualTo(world.Network), $"{surfaceId}: the cavern is in another network.");

                        Assert.That(zLevels.TryMapDown(world.Ground, out var below), Is.True, $"{surfaceId}: nothing below the ground.");
                        Assert.That(below.Owner, Is.EqualTo(cavern), $"{surfaceId}: the map below the ground is not the cavern.");
                        Assert.That(zLevels.TryMapUp(cavern, out var above), Is.True, $"{surfaceId}: nothing above the cavern.");
                        Assert.That(above.Owner, Is.EqualTo(world.Ground), $"{surfaceId}: the map above the cavern is not the ground.");

                        var planetLayer = entMan.GetComponent<WFPlanetLayerComponent>(cavern);
                        Assert.That(planetLayer.Network, Is.EqualTo(entMan.GetNetEntity(world.Network)),
                            $"{surfaceId}: the cavern's planet layer points at another network.");
                        Assert.That(planetLayer.Gravity, Is.EqualTo(surface.Gravity), $"{surfaceId}: the cavern has the wrong gravity.");

                        var cavernLayer = entMan.GetComponent<WFCavernLayerComponent>(cavern);
                        Assert.That(cavernLayer.Cavern.Id, Is.EqualTo(cavernProto.ID), $"{surfaceId}: the cavern layer names the wrong cavern.");
                        Assert.That(cavernLayer.Ground, Is.EqualTo(world.Ground), $"{surfaceId}: the cavern layer names the wrong ground.");

                        var biome = entMan.GetComponent<BiomeComponent>(cavern);
                        Assert.That(biome.Template?.Id, Is.EqualTo(level.Biome.Id), $"{surfaceId}: the cavern has the wrong biome.");
                        Assert.That(biome.Seed, Is.EqualTo(unchecked(surface.Seed!.Value + cavernProto.SeedOffset)),
                            $"{surfaceId}: the cavern has the wrong seed.");

                        Assert.That(entMan.HasComponent<LightCycleComponent>(cavern), Is.False, $"{surfaceId}: the cavern has a day cycle.");
                        Assert.That(entMan.HasComponent<SunShadowComponent>(cavern), Is.False, $"{surfaceId}: the cavern has sun shadows.");
                        Assert.That(entMan.HasComponent<SunShadowCycleComponent>(cavern), Is.False,
                            $"{surfaceId}: the cavern has a sun shadow cycle.");
                        Assert.That(entMan.HasComponent<ParallaxComponent>(cavern), Is.False, $"{surfaceId}: the cavern has parallax.");
                        Assert.That(entMan.HasComponent<CEZGroundLayerComponent>(cavern), Is.False,
                            $"{surfaceId}: the cavern is marked as a ground layer.");
                        Assert.That(entMan.GetComponent<RoofComponent>(cavern).Color, Is.EqualTo(cavernProto.RoofColor),
                            $"{surfaceId}: the cavern has the wrong roof colour.");

                        var mixture = atmos.GetTileMixture(null, new Entity<MapAtmosphereComponent?>(cavern, null), Vector2i.Zero);
                        Assert.That(mixture, Is.Not.Null, $"{surfaceId}: the cavern has no map atmosphere.");
                        Assert.That(atmos.IsTileSpace(null, new Entity<MapAtmosphereComponent?>(cavern, null), Vector2i.Zero), Is.False,
                            $"{surfaceId}: the cavern is space.");
                        Assert.That(mixture!.Temperature, Is.EqualTo(level.Atmosphere.Temperature).Within(0.01f),
                            $"{surfaceId}: the cavern kept the network's air instead of its level's.");
                        for (var gas = 0; gas < Atmospherics.AdjustedNumberOfGases; gas++)
                        {
                            Assert.That(mixture.GetMoles(gas), Is.EqualTo(level.Atmosphere.GetMoles(gas)).Within(0.001f),
                                $"{surfaceId}: the cavern's air has the wrong amount of gas {gas}.");
                        }

                        // Rooms can be built down here, and nothing is tracked until one is.
                        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
                        var terrain = entMan.GetComponent<WFTerrainAtmosphereComponent>(cavern);
                        var (substrate, chamber) = Floors[surfaceId];
                        Assert.That(terrain.OpenTiles, Does.Contain((int) tileDefs[substrate].TileId),
                            $"{surfaceId}: the cavern's rock floor is not bare ground to atmos.");
                        Assert.That(terrain.OpenTiles, Does.Contain((int) tileDefs[chamber].TileId),
                            $"{surfaceId}: the cavern's chamber floor is not bare ground to atmos.");
                        Assert.That(terrain.OpenTiles, Does.Contain((int) tileDefs[cavernProto.Mouths.LandingTile].TileId),
                            $"{surfaceId}: the pad under a mouth is not bare ground to atmos.");
                        Assert.That(terrain.OpenTiles, Does.Not.Contain((int) tileDefs["Plating"].TileId),
                            $"{surfaceId}: plating laid in the cavern would not hold air.");

                        foreach (var layer in new[] { cavern, world.Ground })
                        {
                            var gridAtmos = entMan.GetComponent<GridAtmosphereComponent>(layer);
                            Assert.That(gridAtmos.Tiles, Is.Empty, $"{surfaceId}: atmos tracks bare ground the gate laid.");
                            Assert.That(gridAtmos.InvalidatedCoords, Is.Empty, $"{surfaceId}: bare ground the gate laid is queued for atmos.");
                        }

                        Assert.That(entMan.TryGetComponent(world.Ground, out WFCavernGroundComponent? ground), Is.True,
                            $"{surfaceId}: the ground has no link to its cavern.");
                        Assert.That(ground!.Cavern, Is.EqualTo(cavern), $"{surfaceId}: the ground links the wrong cavern.");
                        Assert.That(ground.Prototype.Id, Is.EqualTo(cavernProto.ID), $"{surfaceId}: the ground names the wrong cavern.");
                    }
                });
            }
            finally
            {
                await Teardown(pair, world);
            }
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Deleting the network takes the cavern and any transit map hanging off it.</summary>
    [Test]
    public async Task DeleteRemovesCavernAndTransits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        await EnableCaverns(pair);
        var world = await BuildWorld(pair, "WFSurfaceAsclepiu");

        var fromCavern = EntityUid.Invalid;
        var intoCavern = EntityUid.Invalid;

        // No PrimaryGrid, so CE's transit update leaves the stubs alone.
        await server.WaitPost(() =>
        {
            fromCavern = maps.CreateMap(out _);
            entMan.EnsureComponent<CEZTransitMapComponent>(fromCavern).LowerMap = world.Cavern;

            intoCavern = maps.CreateMap(out _);
            entMan.EnsureComponent<CEZTransitMapComponent>(intoCavern).UpperMap = world.Cavern;
        });

        await Teardown(pair, world);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Alive(entMan, world.Cavern), Is.False, "The cavern outlived its network.");
                Assert.That(Alive(entMan, world.Network), Is.False, "The network entity outlived its deletion.");
                Assert.That(Alive(entMan, fromCavern), Is.False, "A transit whose lower map was the cavern outlived it.");
                Assert.That(Alive(entMan, intoCavern), Is.False, "A transit whose upper map was the cavern outlived it.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Whether an entity exists and isn't already on its way out.</summary>
    private static bool Alive(IEntityManager entMan, EntityUid uid)
    {
        return entMan.EntityExists(uid) && !entMan.IsQueuedForDeletion(uid);
    }
}
