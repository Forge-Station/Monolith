#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Caverns;
using Content.Shared._DV.Planet;
using Content.Shared._WF.Caverns;
using Content.Shared._WF.Planets;
using Content.Shared.Burial.Components;
using Content.Shared.Lathe;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Layers;
using Content.Shared.Prototypes;
using Content.Shared.Research.Prototypes;
using Content.Shared.Tools.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>Cavern prototypes: one per surface, every reference resolves, floors hold and no template leaks spawners.</summary>
[TestFixture]
[TestOf(typeof(WFCavernPrototype))]
public sealed class CavernPrototypeTest
{
    /// <summary>The self-deleting markers a cavern template may place: loot, and fauna through the planet caps.</summary>
    private static readonly HashSet<string> AllowedSpawners = new()
    {
        "WFCavernFaunaAsclepiu",
        "WFCavernFaunaFervidus",
        "WFCavernFaunaMerak",
        "WFCavernFaunaAerumna",
        "WFCavernFaunaThrascias",
        "WFCavernFaunaCarcinoma",
        "WFCarcinomaAssimilationSack",
        "SalvageHumanCorpseSpawner",
        "SalvageSpawnerScrapValuable",
        "SalvageSpawnerTreasureValuable",
    };

    /// <summary>Exactly one wfCavern lies under each surface, and its level, biome, tiles and entities all resolve.</summary>
    [Test]
    public async Task OneCavernPerSurface()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var caverns = proto.EnumeratePrototypes<WFCavernPrototype>().ToList();

            using (Assert.EnterMultipleScope())
            {
                foreach (var surface in proto.EnumeratePrototypes<WFPlanetSurfacePrototype>())
                {
                    Assert.That(caverns.Count(cavern => cavern.Surface == surface.ID), Is.EqualTo(1),
                        $"{surface.ID} should have exactly one wfCavern.");
                }

                foreach (var cavern in caverns)
                {
                    Assert.That(proto.HasIndex<WFPlanetSurfacePrototype>(cavern.Surface), Is.True,
                        $"{cavern.ID} lies under a missing surface {cavern.Surface}.");

                    if (!proto.TryIndex<PlanetPrototype>(cavern.Level, out var level))
                    {
                        Assert.Fail($"{cavern.ID} names a missing level {cavern.Level}.");
                        continue;
                    }

                    var templates = new List<string>();
                    var tiles = new HashSet<string>();
                    var entities = new HashSet<string>();
                    var missing = Walk(proto, level.Biome, templates, tiles, entities);

                    Assert.That(missing, Is.Empty, $"{cavern.ID}: its biome names missing templates.");
                    Assert.That(templates, Does.Contain($"WFCavernBiome{World(cavern)}"),
                        $"{cavern.ID}'s level does not use its own biome.");

                    var spec = cavern.Mouths;
                    tiles.UnionWith(spec.GroundTiles.Select(tile => tile.Id));
                    tiles.Add(spec.LandingTile);
                    entities.UnionWith(spec.Avoid.Select(entity => entity.Id));
                    entities.UnionWith(spec.Rim.Select(entity => entity.Id));
                    entities.Add(spec.Shade);
                    entities.Add(spec.ClimbPoint);

                    foreach (var tile in tiles)
                    {
                        Assert.That(tileDefs.TryGetDefinition(tile, out _), Is.True, $"{cavern.ID} names a missing tile {tile}.");
                    }

                    foreach (var entity in entities)
                    {
                        Assert.That(proto.HasIndex<EntityPrototype>(entity), Is.True, $"{cavern.ID} names a missing entity {entity}.");
                    }

                    Assert.That(proto.Index<EntityPrototype>(spec.Shade).HasComponent<WFCavernShaftComponent>(factory), Is.True,
                        $"{cavern.ID}'s shade {spec.Shade} is not a shaft.");
                    Assert.That(proto.Index<EntityPrototype>(spec.ClimbPoint).HasComponent<WFCavernClimbComponent>(factory), Is.True,
                        $"{cavern.ID}'s climb point {spec.ClimbPoint} can't be climbed.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every tile a cavern template or mouth can put in a cavern is indestructible and can't be dug or pried.</summary>
    [Test]
    public async Task FloorsIndestructibleAndUndiggable()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var cavern in proto.EnumeratePrototypes<WFCavernPrototype>())
                {
                    var tiles = new HashSet<string>();
                    Walk(proto, proto.Index<PlanetPrototype>(cavern.Level).Biome, new List<string>(), tiles, new HashSet<string>());
                    tiles.Add(cavern.Mouths.LandingTile);

                    foreach (var id in tiles)
                    {
                        var tile = (ContentTileDefinition) tileDefs[id];

                        Assert.That(tile.Indestructible, Is.True, $"{cavern.ID}: {id} can be blown open.");
                        Assert.That(tile.CanShovel, Is.False, $"{cavern.ID}: {id} can be dug.");
                        Assert.That(tile.CanCrowbar, Is.False, $"{cavern.ID}: {id} can be pried.");
                    }
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>No cavern template places a self-deleting spawner but the allowed fauna and loot markers, so no wall leaks out of one.</summary>
    [Test]
    public async Task NoLeakingWallSpawners()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var cavern in proto.EnumeratePrototypes<WFCavernPrototype>())
                {
                    var entities = new HashSet<string>();
                    Walk(proto, proto.Index<PlanetPrototype>(cavern.Level).Biome, new List<string>(), new HashSet<string>(), entities);

                    foreach (var id in entities)
                    {
                        if (AllowedSpawners.Contains(id))
                            continue;

                        Assert.That(id, Does.Not.StartWith("MonoPlanetmap"), $"{cavern.ID} places the outcrop spawner {id}.");

                        var spawners = proto.Index<EntityPrototype>(id).Components.Keys
                            .Where(name => name.EndsWith("Spawner"))
                            .ToList();
                        Assert.That(spawners, Is.Empty, $"{cavern.ID} places {id}, a spawner ({string.Join(", ", spawners)}) outside the allowlist.");
                    }
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A basic autolathe can make a shovel that digs cavern shafts, so every crew can get underground.</summary>
    [Test]
    public async Task AutolatheMakesAShaftShovel()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.That(proto.Index<EntityPrototype>("Autolathe").TryGetComponent<LatheComponent>(out var lathe, factory));
            var shovels = lathe!.StaticPacks
                .SelectMany(pack => proto.Index(pack).Recipes)
                .Select(recipe => proto.Index<LatheRecipePrototype>(recipe).Result)
                .Where(result => result is { } id && DigsShafts(proto.Index(id)));
            Assert.That(shovels, Is.Not.Empty, "The autolathe can't make a shovel that digs cavern shafts.");
        });

        await pair.CleanReturnAsync();

        bool DigsShafts(EntityPrototype item)
        {
            if (!item.HasComponent<ShovelComponent>(factory) || !item.TryGetComponent<ToolComponent>(out var tool, factory))
                return false;
            var qualities = tool.Qualities;
            return qualities.Contains(WFCavernDigSystem.DiggingQuality);
        }
    }

    /// <summary>The world name in a cavern id, WFCavernFervidus giving Fervidus.</summary>
    private static string World(WFCavernPrototype cavern)
    {
        return cavern.ID["WFCavern".Length..];
    }

    /// <summary>Walks a biome template and every template it nests, collecting their tiles and entities; returns the missing templates.</summary>
    internal static List<string> Walk(IPrototypeManager proto, string template, List<string> templates, HashSet<string> tiles,
        HashSet<string> entities)
    {
        var missing = new List<string>();

        if (templates.Contains(template))
            return missing;

        if (!proto.TryIndex<BiomeTemplatePrototype>(template, out var biome))
        {
            missing.Add(template);
            return missing;
        }

        templates.Add(template);

        foreach (var layer in biome.Layers)
        {
            switch (layer)
            {
                case BiomeTileLayer tile:
                    tiles.Add(tile.Tile);
                    break;
                case BiomeEntityLayer entity:
                    entities.UnionWith(entity.Entities);
                    tiles.UnionWith(entity.AllowedTiles);
                    break;
                case BiomeMetaLayer meta:
                    missing.AddRange(Walk(proto, meta.Template, templates, tiles, entities));
                    break;
            }
        }

        return missing;
    }
}
