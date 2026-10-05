#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Caverns;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>Each world's cavern noise, sampled around its gate: open enough, connected, ore only in rock, signature present, lit.</summary>
[TestFixture]
[TestOf(typeof(WFCavernSampler))]
public sealed class CavernBiomeTest
{
    /// <summary>Section 4: the share of open tiles each world's 192-tile sample must fall within.</summary>
    private static readonly Dictionary<string, (float Min, float Max)> OpenBands = new()
    {
        { "WFSurfaceAsclepiu", (0.35f, 0.55f) },
        { "WFSurfaceFervidus", (0.30f, 0.45f) },
        { "WFSurfaceMerak", (0.40f, 0.60f) },
        { "WFSurfaceAerumna", (0.25f, 0.42f) },
        { "WFSurfaceThrascias", (0.35f, 0.55f) },
        { "WFSurfaceCarcinoma", (0.35f, 0.55f) },
    };

    /// <summary>
    /// Section 6: what each world's signature feature leaves in the noise. A named template must place the entity by
    /// itself, and every tile where it does must carry it in the cavern too, so another layer can't stand in for it.
    /// </summary>
    private static readonly Dictionary<string, Signature[]> Signatures = new()
    {
        {
            "WFSurfaceAsclepiu", new[]
            {
                new Signature("plunge pools", null, "MonoFloorWaterEntity"),
                new Signature("glowcaps", null, "WFCavernGlowcaps"),
            }
        },
        {
            "WFSurfaceFervidus", new[]
            {
                new Signature("lava tubes", null, "FloorLavaEntity", "WFCavernSignatureFervidus"),
                new Signature("ember lichen", null, "WFCavernEmberLichen"),
            }
        },
        {
            "WFSurfaceMerak", new[]
            {
                new Signature("hall pillars", "WFCavernFloorSandstone", "WallRockSand"),
                new Signature("fossils", null, "WallRockSandArtifactFragment"),
                new Signature("buried camps", null, "SalvageHumanCorpseSpawner"),
                new Signature("lamp agaves", null, "WFCavernLampAgave"),
            }
        },
        {
            "WFSurfaceAerumna", new[]
            {
                new Signature("pink geodes", null, "CrystalPink"),
                new Signature("shadow trees", null, "ShadowTree"),
                new Signature("shadow blooms", null, "WFCavernShadowBloom"),
            }
        },
        {
            "WFSurfaceThrascias", new[]
            {
                new Signature("ice galleries", "FloorIce"),
                new Signature("ice columns", null, "WallIce"),
                new Signature("plasma lakes", "FloorIce", "FloorLiquidPlasmaEntity", "WFCavernSignatureThrascias"),
                new Signature("rime thistles", null, "WFCavernRimeThistle"),
            }
        },
        {
            "WFSurfaceCarcinoma", new[]
            {
                new Signature("blood channels", null, "WFBloodRiver"),
                new Signature("acid pools", "WFCavernFloorGut", "WFCavernDigestiveAcid"),
                new Signature("nerve clusters", null, "WFCavernNerveCluster"),
            }
        },
    };

    /// <summary>
    /// Features that must come in dense stretches somewhere around the gate: the entity must fill at least the given
    /// share of the open samples in some window of <see cref="StretchWindow"/> samples a side.
    /// </summary>
    private static readonly Dictionary<string, Stretch[]> Stretches = new()
    {
        { "WFSurfaceCarcinoma", new[] { new Stretch("choked throats", "WFCavernTendons", 0.5f) } },
    };

    /// <summary>Samples a side of the window a dense stretch is looked for in (10 tiles at the signature step).</summary>
    private const int StretchWindow = 5;

    /// <summary>The walk to a light, in tiles, within which a tunnel tile counts as lit.</summary>
    private const int GlowWalk = WFCavernCommand.StatsGlowWalk;

    /// <summary>The share of the web's tunnel floor in 128² around each gate that must lie within that walk of a light.</summary>
    private const float MinGlowShare = 0.9f;

    /// <summary>Samples left around the 128² whose tunnels are measured, so a light just outside it still counts.</summary>
    private const int GlowMargin = WFCavernCommand.StatsGlowMargin;

    /// <summary>Edge of the square a viewer on the gate pad loads, 81 chunks.</summary>
    private const int ViewerSize = 72;

    /// <summary>Most lights the natural terrain may put in that square.</summary>
    private const int MaxViewerLights = 100;

    /// <summary>Edge of the open-fraction sample.</summary>
    private const int OpenSize = 192;

    /// <summary>Edge of the connectivity sample.</summary>
    private const int ConnectSize = 128;

    /// <summary>The largest walkable region must hold at least this share of the walkable tiles.</summary>
    private const float MinLargestShare = 0.6f;

    /// <summary>Edge of the signature sample, taken every other tile.</summary>
    private const int SignatureSize = 512;

    /// <summary>The share of open tiles in 192² around each gate falls inside its world's band.</summary>
    [Test]
    public async Task OpenFractionInBand()
    {
        var samples = await SampleEveryWorld(OpenSize, 1);

        using (Assert.EnterMultipleScope())
        {
            foreach (var world in samples)
            {
                var sample = world.Sample;
                var open = sample.OpenFraction();
                var (min, max) = OpenBands[world.Surface];
                TestContext.Out.WriteLine($"{world.Surface}: open {open:P1}, largest region {sample.LargestRegionShare():P1}, veins {sample.VeinFraction():P1} of rock.");

                Assert.That(open, Is.InRange(min, max), $"{world.Surface}: {open:P1} of the tiles around the gate are open, outside {min:P0}-{max:P0}.");
            }
        }
    }

    /// <summary>In 128² around each gate, the largest 4-connected walkable region holds most of the walkable tiles; lava, plasma and acid count as barriers.</summary>
    [Test]
    public async Task TunnelsConnect()
    {
        var samples = await SampleEveryWorld(ConnectSize, 1);

        using (Assert.EnterMultipleScope())
        {
            foreach (var world in samples)
            {
                var largest = world.Sample.LargestRegionShare();
                TestContext.Out.WriteLine($"{world.Surface}: largest walkable region {largest:P1} of the walkable tiles.");

                Assert.That(largest, Is.GreaterThanOrEqualTo(MinLargestShare),
                    $"{world.Surface}: the largest walkable region holds only {largest:P1} of the walkable tiles.");
            }
        }
    }

    /// <summary>Every ore vein sampled around each gate stands on the world's rock substrate, and there are veins.</summary>
    [Test]
    public async Task OreOnlyInRock()
    {
        var samples = await SampleEveryWorld(OpenSize, 1);

        using (Assert.EnterMultipleScope())
        {
            foreach (var world in samples)
            {
                var sample = world.Sample;
                var substrate = Floors[world.Surface].Substrate;
                var veins = 0;

                for (var i = 0; i < sample.Count; i++)
                {
                    if (!sample.Vein[i])
                        continue;

                    veins++;
                    Assert.That(sample.Tiles[i], Is.EqualTo(substrate),
                        $"{world.Surface}: {sample.Entities[i]} at {sample.IndexOf(i)} stands on {sample.Tiles[i]}, not the rock substrate.");
                }

                Assert.That(veins, Is.Positive, $"{world.Surface}: no ore vein near the gate.");
            }
        }
    }

    /// <summary>Each world's signature feature turns up in 512² around its gate, placed by its own template where one is named.</summary>
    [Test]
    public async Task SignaturePresent()
    {
        var samples = await SampleEveryWorld(SignatureSize, 2, true);

        using (Assert.EnterMultipleScope())
        {
            foreach (var world in samples)
            {
                var sample = world.Sample;

                foreach (var signature in Signatures[world.Surface])
                {
                    if (signature.Template is not { } template)
                    {
                        var found = Enumerable.Range(0, sample.Count).Count(i => signature.Matches(sample, i));
                        TestContext.Out.WriteLine($"{world.Surface}: {found} samples of {signature.What}.");

                        Assert.That(found, Is.Positive, $"{world.Surface}: no {signature.What} in {SignatureSize}² around the gate.");
                        continue;
                    }

                    var alone = world.Templates[template];
                    var placed = Enumerable.Range(0, alone.Count).Where(i => signature.Matches(alone, i)).ToList();
                    var missing = placed.Where(i => !signature.Matches(sample, i)).ToList();
                    TestContext.Out.WriteLine($"{world.Surface}: {placed.Count} samples of {signature.What} from {template}, {missing.Count} missing in the cavern.");

                    Assert.That(placed, Is.Not.Empty, $"{world.Surface}: {template} places no {signature.What} in {SignatureSize}² around the gate.");
                    Assert.That(missing, Is.Empty,
                        $"{world.Surface}: {missing.Count} of the {placed.Count} tiles where {template} places {signature.What} lack it in the cavern, the first at {(missing.Count > 0 ? sample.IndexOf(missing[0]) : default)}.");
                }

                if (!Stretches.TryGetValue(world.Surface, out var stretches))
                    continue;

                foreach (var stretch in stretches)
                {
                    var densest = stretch.Densest(sample, StretchWindow);
                    TestContext.Out.WriteLine($"{world.Surface}: {stretch.What} fill at most {densest:P0} of a {StretchWindow}-sample window.");

                    Assert.That(densest, Is.GreaterThanOrEqualTo(stretch.MinShare),
                        $"{world.Surface}: {stretch.Entity} fills at most {densest:P0} of the open ground in any {StretchWindow}-sample window; no {stretch.What}.");
                }
            }
        }
    }

    /// <summary>The sampler counts the surface's water, which Asclepiu's plunge pools use, as open ground and no hazard.</summary>
    [Test]
    public async Task SamplerCountsWaterAsOpen()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var sampler = server.System<WFCavernSampler>();

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(sampler.IsSolid("MonoFloorWaterEntity"), Is.False, "Water counts as rock.");
                Assert.That(sampler.IsHazard("MonoFloorWaterEntity"), Is.False, "Water counts as a walkability barrier.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// In 128² around each gate, most of the connected web's tunnel floor lies within a short walk of some glow, with
    /// rock and hazards lengthening the way, and the square a viewer loads holds a bounded number of lights.
    /// </summary>
    [Test]
    public async Task GlowReachesTunnels()
    {
        var samples = await SampleEveryWorld(ConnectSize + 2 * GlowMargin, 1);

        using (Assert.EnterMultipleScope())
        {
            foreach (var world in samples)
            {
                var sample = world.Sample;
                Assert.That(world.TunnelFloor, Is.Not.Null, $"{world.Surface}: the cavern biome lays no tunnel floor first.");

                var share = sample.LightWalkShare(GlowWalk, world.TunnelFloor, GlowMargin);
                var walks = sample.LightWalksOn(world.TunnelFloor, GlowMargin).OrderBy(walk => walk).ToList();
                var margin = (sample.Width - ViewerSize) / 2;
                var viewer = Enumerable.Range(0, sample.Count).Count(i =>
                {
                    var x = i % sample.Width;
                    var y = i / sample.Width;
                    return sample.Light[i] && x >= margin && y >= margin && x < margin + ViewerSize && y < margin + ViewerSize;
                });
                var half = walks.Count > 0 ? walks[walks.Count / 2] : 0;
                var most = walks.Count > 0 ? walks[(int) (walks.Count * 0.9f)] : 0;
                TestContext.Out.WriteLine($"{world.Surface}: {share:P1} of the tunnel floor within a {GlowWalk}-tile walk of a light (half within {half}, 90% within {most}); {viewer} lights in the {ViewerSize}² a viewer loads.");

                Assert.That(share, Is.GreaterThanOrEqualTo(MinGlowShare),
                    $"{world.Surface}: only {share:P1} of the tunnel floor lies within a {GlowWalk}-tile walk of a light.");
                Assert.That(viewer, Is.InRange(1, MaxViewerLights),
                    $"{world.Surface}: {viewer} lights in the {ViewerSize}² a viewer loads, outside 1-{MaxViewerLights}.");
            }
        }
    }

    /// <summary>
    /// Builds each world in turn on one pair and samples its cavern noise around the gate candidate (the planet centre
    /// plus the gate offset), and optionally its signature templates alone. The candidate, not the gate, so the sample
    /// doesn't move with the shape the gate's seed grows.
    /// </summary>
    private static async Task<List<WorldSample>> SampleEveryWorld(int size, int step, bool templates = false)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var sampler = server.System<WFCavernSampler>();
        var proto = server.ResolveDependency<IPrototypeManager>();
        var samples = new List<WorldSample>();

        await EnableCaverns(pair);

        foreach (var surfaceId in Surfaces)
        {
            var world = await BuildWorld(pair, surfaceId);

            try
            {
                var spec = CavernOf(pair, surfaceId).Mouths;

                await server.WaitPost(() =>
                {
                    var biome = server.EntMan.GetComponent<BiomeComponent>(world.Cavern);
                    var planet = server.EntMan.GetComponent<WFCavernGroundComponent>(world.Ground).Centre;
                    var centre = new Vector2i((int) MathF.Floor(planet.X), (int) MathF.Floor(planet.Y)) + spec.GateOffset;
                    var alone = new Dictionary<string, WFCavernSample>();

                    if (templates)
                    {
                        foreach (var signature in Signatures[surfaceId])
                        {
                            if (signature.Template is { } template && !alone.ContainsKey(template))
                                alone[template] = sampler.Sample(proto.Index<BiomeTemplatePrototype>(template).Layers, biome.Seed, centre, size, step);
                        }
                    }

                    samples.Add(new WorldSample(surfaceId, sampler.Sample(biome, centre, size, step), alone, WFCavernSampler.TunnelFloor(biome.Layers)));
                });
            }
            finally
            {
                await Teardown(pair, world);
            }
        }

        await pair.CleanReturnAsync();
        return samples;
    }

    /// <summary>A world's cavern sample, by template id its signature templates sampled alone, and its tunnel floor.</summary>
    private sealed record WorldSample(string Surface, WFCavernSample Sample, Dictionary<string, WFCavernSample> Templates, string? TunnelFloor);

    /// <summary>A feature that comes in dense stretches: what it is, its entity, and the share of a window it must fill.</summary>
    private sealed record Stretch(string What, string Entity, float MinShare)
    {
        /// <summary>The largest share of open samples the entity fills in any square window of the sample.</summary>
        public float Densest(WFCavernSample sample, int window)
        {
            var best = 0f;

            for (var y0 = 0; y0 + window <= sample.Width; y0++)
            for (var x0 = 0; x0 + window <= sample.Width; x0++)
            {
                var open = 0;
                var filled = 0;

                for (var y = y0; y < y0 + window; y++)
                for (var x = x0; x < x0 + window; x++)
                {
                    var i = y * sample.Width + x;
                    if (!sample.IsOpen(i))
                        continue;

                    open++;
                    if (sample.Entities[i] == Entity)
                        filled++;
                }

                if (open >= window * window / 4)
                    best = Math.Max(best, (float) filled / open);
            }

            return best;
        }
    }

    /// <summary>A signature: what it is, the tile and entity it leaves (null for any), and the template that places it.</summary>
    private sealed record Signature(string What, string? Tile, string? Entity = null, string? Template = null)
    {
        /// <summary>Whether a sample carries this signature.</summary>
        public bool Matches(WFCavernSample sample, int i)
        {
            return (Tile == null || sample.Tiles[i] == Tile) && (Entity == null || sample.Entities[i] == Entity);
        }
    }
}
