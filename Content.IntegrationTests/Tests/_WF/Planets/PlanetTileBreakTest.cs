#nullable enable
using Content.Server._WF.Planets;
using Content.Shared._WF.Planets;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// A floor nobody built that breaks on a planet gives the ground back, however far an explosion breaks it; a player's
/// own floor over the ground breaks the station way; lattice laid on the ground stays.
/// </summary>
[TestFixture]
[TestOf(typeof(WFPlanetTileBreakSystem))]
public sealed class PlanetTileBreakTest
{
    [Test]
    public async Task BrokenFloorsGiveTheGroundBack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await PlanetFixture.EnableFeature(pair);
        var layers = await PlanetFixture.BuildStandalone(pair);
        var ground = layers[0];

        var pried = new Vector2i(3, 3);
        var blasted = new Vector2i(5, 3);
        var players = new Vector2i(7, 3);
        var laid = new Vector2i(9, 3);
        Tile Natural(Vector2i index)
        {
            var biome = entMan.GetComponent<BiomeComponent>(ground);
            Assert.That(server.System<SharedBiomeSystem>().TryGetBiomeTile(index, biome.Layers, biome.Seed, (MapGridComponent?) null, out var tile), Is.True);
            return tile!.Value;
        }

        Tile Of(string id) => new(tileDefs[id].TileId);

        try
        {
            await server.WaitPost(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(ground);
                foreach (var index in new[] { pried, blasted, players })
                    maps.SetTile(ground, grid, index, Of("FloorWood"));
                entMan.EnsureComponent<WFPlanetBuiltTilesComponent>(ground).Underlay[players] = Natural(players);
                maps.SetTile(ground, grid, laid, Natural(laid));
            });
            await server.WaitRunTicks(2);

            await server.WaitPost(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(ground);
                maps.SetTile(ground, grid, pried, Of("Plating"));
                maps.SetTile(ground, grid, blasted, Of("Lattice"));
                maps.SetTile(ground, grid, players, Of("Plating"));
                maps.SetTile(ground, grid, laid, Of("Lattice"));
            });
            await server.WaitRunTicks(2);

            await server.WaitAssertion(() =>
            {
                var grid = entMan.GetComponent<MapGridComponent>(ground);
                string At(Vector2i index) => tileDefs[maps.GetTileRef(ground, grid, index).Tile.TypeId].ID;

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(At(pried), Is.EqualTo(tileDefs[Natural(pried).TypeId].ID), "A pried-up floor left plating on the planet.");
                    Assert.That(At(blasted), Is.EqualTo(tileDefs[Natural(blasted).TypeId].ID), "A blasted floor left lattice on the planet.");
                    Assert.That(At(players), Is.EqualTo("Plating"), "A player's own floor did not break the station way.");
                    Assert.That(At(laid), Is.EqualTo("Lattice"), "Lattice laid on the ground was taken away.");
                }
            });
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }
}
