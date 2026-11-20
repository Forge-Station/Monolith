#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Shared._DV.Planet;
using Content.Shared._WF.Planets;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Layers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Noise;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>Biome noise is copied once per source and seed, and every Wolfgate world generates exactly as before.</summary>
[TestFixture]
[TestOf(typeof(WFBiomeNoiseCacheSystem))]
public sealed class BiomeNoiseCacheTest
{
    private const int Seed = 424242;
    private const int Side = 24;
    private static readonly Entity<MapGridComponent>? NoGrid = null;
    private static readonly Vector2i Origin = new(1000, -2000);
    private static readonly ProtoId<BiomeTemplatePrototype> Fervidus = "WFBiomeFervidus";

    /// <summary>A tile's generated tile, entity and decals.</summary>
    private readonly record struct Terrain(Tile? Tile, string? Entity, string Decals);

    [Test]
    public async Task CachedTerrainMatchesFreshCopies()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var biome = server.System<SharedBiomeSystem>();
            var cache = server.System<WFBiomeNoiseCacheSystem>();
            var ser = server.ResolveDependency<ISerializationManager>();
            var templates = Templates(server.ProtoMan);
            Assert.That(templates, Is.Not.Empty);

            foreach (var template in templates)
            {
                var layers = server.ProtoMan.Index<BiomeTemplatePrototype>(template).Layers;
                // Cache cleared before every lookup, so each copies its noise afresh as before the cache (only
                // TryGetDecals reads its tile through the copies it just made).
                var fresh = new Terrain[Side * Side];
                for (var i = 0; i < fresh.Length; i++)
                {
                    fresh[i] = Sample(biome, layers, Index(i), cache);
                }

                // Warm and shared across threads, as chunk and marker generation use it.
                var cached = new Terrain[Side * Side];
                Parallel.For(0, cached.Length, i => cached[i] = Sample(biome, layers, Index(i)));

                // Every cached copy, meta templates' layers included, reads like a fresh serialiser copy.
                var differing = new List<string>();
                var compared = 0;
                var nested = 0;
                foreach (var (owner, index, layer) in AllLayers(server.ProtoMan, template))
                {
                    if (!cache.TryGet(layer.Noise, Seed, out var copy))
                        continue;

                    if (!SameNoise(copy, FreshCopy(ser, layer.Noise, Seed)))
                        differing.Add($"{owner} layer {index}");

                    compared++;
                    if (owner != template)
                        nested++;
                }

                Assert.That(differing, Is.Empty, $"{template}: cached copies differ from fresh ones");
                Assert.That(compared, Is.GreaterThan(0), $"{template} cached none of its layers");
                if (layers.Any(layer => layer is BiomeMetaLayer))
                    Assert.That(nested, Is.GreaterThan(0), $"{template} cached none of its meta templates' layers");

                var mismatch = Enumerable.Range(0, fresh.Length).FirstOrDefault(i => fresh[i] != cached[i], -1);
                Assert.That(mismatch, Is.EqualTo(-1),
                    () => $"{template} at {Index(mismatch)}: fresh {fresh[mismatch]}, cached {cached[mismatch]}");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OneCopyPerSourceAndSeed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var biome = server.System<SharedBiomeSystem>();
            var cache = server.System<WFBiomeNoiseCacheSystem>();
            var layers = server.ProtoMan.Index(Fervidus).Layers;
            var probe = layers[^1].Noise;

            cache.Clear();
            SampleRow(biome, layers, Seed);
            Assert.That(cache.TryGet(probe, Seed, out var first), Is.True, "generation did not go through the cache");
            var count = cache.CopyCount;

            SampleRow(biome, layers, Seed);
            Assert.That(cache.TryGet(probe, Seed, out var again), Is.True);
            Assert.That(again, Is.SameAs(first));
            Assert.That(cache.CopyCount, Is.EqualTo(count), "the same tiles copied noise again");

            SampleRow(biome, layers, Seed + 1);
            Assert.That(cache.TryGet(probe, Seed + 1, out var other), Is.True);
            Assert.That(other, Is.Not.SameAs(first));
            Assert.That(other!.GetSeed(), Is.EqualTo(probe.GetSeed() + Seed + 1));
            Assert.That(first!.GetSeed(), Is.EqualTo(probe.GetSeed() + Seed));
            Assert.That(cache.TryGet(probe, Seed, out var kept) && ReferenceEquals(kept, first), Is.True);

            // A copy made by a second thread that lost the race gives way to the first.
            var late = new FastNoiseLite(first.GetSeed());
            Assert.That(cache.Add(probe, late), Is.SameAs(first));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShiftedSourceSeedGetsItsOwnCopy()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var biome = server.System<SharedBiomeSystem>();
            var cache = server.System<WFBiomeNoiseCacheSystem>();
            var ser = server.ResolveDependency<ISerializationManager>();
            // Own layers, so shifting a seed leaves the prototypes alone.
            var layers = ser.CreateCopy(server.ProtoMan.Index(Fervidus).Layers,
                notNullableOverride: true);
            var probe = layers[^1].Noise;

            SampleRow(biome, layers, Seed);
            Assert.That(cache.TryGet(probe, Seed, out var before), Is.True);

            // What BiomeSystem.AddTemplate does to a template's layers.
            probe.SetSeed(probe.GetSeed() + 5);
            SampleRow(biome, layers, Seed);
            Assert.That(cache.TryGet(probe, Seed, out var after), Is.True);
            Assert.That(after, Is.Not.SameAs(before));
            Assert.That(after!.GetSeed(), Is.EqualTo(probe.GetSeed() + Seed));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CacheStartsOverWhenFull()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var cache = server.System<WFBiomeNoiseCacheSystem>();
            var source = new FastNoiseLite();

            cache.Clear();
            for (var i = 0; i <= WFBiomeNoiseCacheSystem.Capacity; i++)
            {
                cache.Add(source, new FastNoiseLite(source.GetSeed() + i));
            }

            Assert.That(cache.CopyCount, Is.InRange(1, WFBiomeNoiseCacheSystem.Capacity));
            cache.Clear();
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The biome of every Wolfgate world: planet surfaces and cavern levels.</summary>
    private static List<string> Templates(IPrototypeManager proto)
    {
        return proto.EnumeratePrototypes<PlanetPrototype>()
            .Where(planet => planet.ID.StartsWith("WF"))
            .Select(planet => planet.Biome.Id)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }

    private static Vector2i Index(int i)
    {
        return Origin + new Vector2i(i % Side, i / Side);
    }

    /// <summary>Samples a tile, clearing <paramref name="clear"/> before each lookup if given.</summary>
    private static Terrain Sample(SharedBiomeSystem biome, List<IBiomeLayer> layers, Vector2i index,
        WFBiomeNoiseCacheSystem? clear = null)
    {
        clear?.Clear();
        if (!biome.TryGetBiomeTile(index, layers, Seed, NoGrid, out var tile))
            return new Terrain(null, null, string.Empty);

        clear?.Clear();
        biome.TryGetEntity(index, layers, tile.Value, Seed, NoGrid, out var entity);
        clear?.Clear();
        biome.TryGetDecals(index, layers, Seed, NoGrid, out var decals);
        var decalText = decals == null ? string.Empty : string.Join(";", decals.Select(d => $"{d.ID}@{d.Position}"));
        return new Terrain(tile, entity, decalText);
    }

    private static void SampleRow(SharedBiomeSystem biome, List<IBiomeLayer> layers, int seed)
    {
        for (var x = 0; x < 64; x++)
        {
            biome.TryGetTile(new Vector2i(x, -x), layers, seed, NoGrid, out _);
        }
    }

    /// <summary>Every layer of a template and of the templates its meta layers pick, with the template holding it.</summary>
    private static IEnumerable<(string Owner, int Index, IBiomeLayer Layer)> AllLayers(IPrototypeManager proto,
        string template, HashSet<string>? visited = null)
    {
        visited ??= new HashSet<string>();
        if (!visited.Add(template))
            yield break;

        var layers = proto.Index<BiomeTemplatePrototype>(template).Layers;
        for (var i = 0; i < layers.Count; i++)
        {
            yield return (template, i, layers[i]);

            if (layers[i] is not BiomeMetaLayer meta)
                continue;

            foreach (var inner in AllLayers(proto, meta.Template, visited))
            {
                yield return inner;
            }
        }
    }

    /// <summary>Whether two noises give the same values through both read overloads biome sampling uses.</summary>
    private static bool SameNoise(FastNoiseLite a, FastNoiseLite b)
    {
        for (var x = -40; x <= 40; x += 8)
        {
            for (var y = -40; y <= 40; y += 8)
            {
                if (a.GetNoise(x, y) != b.GetNoise(x, y) || a.GetNoise(x * 8, y * 8, 3) != b.GetNoise(x * 8, y * 8, 3))
                    return false;
            }
        }

        return true;
    }

    /// <summary>The copy SharedBiomeSystem.GetNoise made on every call before the cache.</summary>
    private static FastNoiseLite FreshCopy(ISerializationManager ser, FastNoiseLite source, int seed)
    {
        var copy = new FastNoiseLite();
        ser.CopyTo(source, ref copy, notNullableOverride: true);
        copy.SetSeed(copy.GetSeed() + seed);
        copy.SetFractalOctaves(copy.GetFractalOctaves());
        return copy;
    }
}
