using System.Linq;
using System.Threading.Tasks;
using Content.Server.Atmos.Components;
using Content.Server.EntityEffects.Effects;
using Content.Server.Parallax;
using Content.Server.Tiles;
using Content.Shared._WF.Caverns;
using Content.Shared.Damage.Components;
using Content.Shared.Mining.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Layers;
using Robust.Server.GameObjects;
using Robust.Shared.CPUJob.JobQueues;
using Robust.Shared.CPUJob.JobQueues.Queues;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Caverns;

/// <summary>Reads a cavern's natural terrain from its noise alone; wfcavern stats and the cavern tests share it.</summary>
public sealed partial class WFCavernSampler : EntitySystem
{
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;

    /// <summary>Seconds a tick that background samples may take.</summary>
    private const double JobTime = 0.002;

    private static readonly Entity<MapGridComponent>? NoGrid = null;

    private readonly JobQueue _jobs = new(JobTime);
    private readonly Dictionary<string, bool> _solid = new();
    private readonly Dictionary<string, bool> _veins = new();
    private readonly Dictionary<string, bool> _hazards = new();
    private readonly Dictionary<string, bool> _lights = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _jobs.Process();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<EntityPrototype>())
            return;

        _solid.Clear();
        _veins.Clear();
        _hazards.Clear();
        _lights.Clear();
    }

    /// <summary>Samples a square of <paramref name="size"/> tiles centred on a tile, taking every <paramref name="step"/>th tile on each axis.</summary>
    public WFCavernSample Sample(BiomeComponent biome, Vector2i centre, int size, int step = 1)
    {
        return Sample(biome.Layers, biome.Seed, centre, size, step);
    }

    /// <summary>Samples a square of terrain from any layers, such as one template on its own, with a biome's seed.</summary>
    public WFCavernSample Sample(List<IBiomeLayer> layers, int seed, Vector2i centre, int size, int step = 1)
    {
        var sample = NewSample(centre, size, step);

        for (var y = 0; y < sample.Width; y++)
        {
            SampleRow(layers, seed, sample, y);
        }

        return sample;
    }

    /// <summary>
    /// Samples every tile like <see cref="Sample"/>, a few rows a tick; null if the cavern goes away first. While the
    /// game is paused, as an empty server is, nothing ticks and nobody waits on one, so it samples at once.
    /// </summary>
    public Task<WFCavernSample?> SampleLater(EntityUid cavern, Vector2i centre, int size)
    {
        if (_timing.Paused)
        {
            return Task.FromResult<WFCavernSample?>(TryComp<BiomeComponent>(cavern, out var biome)
                ? Sample(biome, centre, size)
                : null);
        }

        var job = new SampleJob(this, cavern, NewSample(centre, size, 1));
        _jobs.EnqueueJob(job);
        return job.AsTask;
    }

    private static WFCavernSample NewSample(Vector2i centre, int size, int step)
    {
        var width = (size + step - 1) / step;
        return new WFCavernSample(centre - new Vector2i(size / 2, size / 2), width, step);
    }

    private void SampleRow(List<IBiomeLayer> layers, int seed, WFCavernSample sample, int y)
    {
        for (var x = 0; x < sample.Width; x++)
        {
            var i = y * sample.Width + x;
            var index = sample.IndexOf(i);

            if (!_biome.TryGetTile(index, layers, seed, NoGrid, out var tile))
                continue;

            sample.Tiles[i] = _tileDefs[tile.Value.TypeId].ID;

            if (!_biome.TryGetEntity(index, layers, tile.Value, seed, NoGrid, out var entity))
                continue;

            sample.Entities[i] = entity;
            sample.Solid[i] = IsSolid(entity);
            sample.Vein[i] = IsVein(entity);
            sample.Hazard[i] = IsHazard(entity);
            sample.Light[i] = IsLight(entity);
        }
    }

    /// <summary>Whether an entity is rock: anything airtight, from plain walls to pillars and ice columns.</summary>
    public bool IsSolid(string entity)
    {
        if (!_solid.TryGetValue(entity, out var solid))
        {
            solid = _proto.Index<EntityPrototype>(entity).TryGetComponent<AirtightComponent>(out _, _factory);
            _solid[entity] = solid;
        }

        return solid;
    }

    /// <summary>Whether an entity is an ore vein: rock that always yields a set ore.</summary>
    public bool IsVein(string entity)
    {
        if (!_veins.TryGetValue(entity, out var vein))
        {
            vein = _proto.Index<EntityPrototype>(entity).TryGetComponent<OreVeinComponent>(out var ore, _factory)
                   && ore.CurrentOre != null;
            _veins[entity] = vein;
        }

        return vein;
    }

    /// <summary>Whether an entity hurts whoever stands in it: lava and liquid plasma set you alight, digestive acid burns.</summary>
    public bool IsHazard(string entity)
    {
        if (!_hazards.TryGetValue(entity, out var hazard))
        {
            var proto = _proto.Index<EntityPrototype>(entity);
            hazard = proto.TryGetComponent<TileEntityEffectComponent>(out var effects, _factory)
                     && effects.Effects.Any(effect => effect is Ignite)
                     || proto.TryGetComponent<DamageContactsComponent>(out _, _factory)
                     || proto.TryGetComponent<WFDigestiveAcidComponent>(out _, _factory);
            _hazards[entity] = hazard;
        }

        return hazard;
    }

    /// <summary>The floor a biome lays first, under everything else: the one its tunnels run on.</summary>
    public static string? TunnelFloor(List<IBiomeLayer> layers)
    {
        return layers.Count > 0 && layers[0] is BiomeTileLayer substrate ? substrate.Tile.Id : null;
    }

    /// <summary>Whether an entity gives off light: glow flora, crystals, glow-worms.</summary>
    public bool IsLight(string entity)
    {
        if (!_lights.TryGetValue(entity, out var light))
        {
            light = _proto.Index<EntityPrototype>(entity).TryGetComponent<PointLightComponent>(out _, _factory);
            _lights[entity] = light;
        }

        return light;
    }

    /// <summary>Fills a sample a row at a time within the job budget.</summary>
    private sealed class SampleJob : Job<WFCavernSample>
    {
        private readonly WFCavernSampler _sampler;
        private readonly EntityUid _cavern;
        private readonly WFCavernSample _sample;

        public SampleJob(WFCavernSampler sampler, EntityUid cavern, WFCavernSample sample) : base(JobTime)
        {
            _sampler = sampler;
            _cavern = cavern;
            _sample = sample;
        }

        protected override async Task<WFCavernSample?> Process()
        {
            for (var y = 0; y < _sample.Width; y++)
            {
                if (!_sampler.TryComp<BiomeComponent>(_cavern, out var biome))
                    return null;

                _sampler.SampleRow(biome.Layers, biome.Seed, _sample, y);
                await SuspendIfOutOfTime();
            }

            return _sample;
        }
    }
}

/// <summary>A square of natural cavern terrain, row by row from its bottom-left sample.</summary>
public sealed class WFCavernSample
{
    /// <summary>The bottom-left sampled tile.</summary>
    public readonly Vector2i Origin;

    /// <summary>Samples per row and per column.</summary>
    public readonly int Width;

    /// <summary>Tiles between neighbouring samples.</summary>
    public readonly int Step;

    /// <summary>Each sample's natural tile id, or null where the noise gives none.</summary>
    public readonly string?[] Tiles;

    /// <summary>Each sample's natural entity id, or null.</summary>
    public readonly string?[] Entities;

    /// <summary>Whether each sample's entity is rock.</summary>
    public readonly bool[] Solid;

    /// <summary>Whether each sample's entity is an ore vein.</summary>
    public readonly bool[] Vein;

    /// <summary>Whether each sample's entity hurts whoever stands in it (lava, liquid plasma, digestive acid).</summary>
    public readonly bool[] Hazard;

    /// <summary>Whether each sample's entity gives off light.</summary>
    public readonly bool[] Light;

    /// <summary>An empty sample of a square.</summary>
    public WFCavernSample(Vector2i origin, int width, int step)
    {
        Origin = origin;
        Width = width;
        Step = step;
        Tiles = new string?[width * width];
        Entities = new string?[width * width];
        Solid = new bool[width * width];
        Vein = new bool[width * width];
        Hazard = new bool[width * width];
        Light = new bool[width * width];
    }

    /// <summary>How many samples there are.</summary>
    public int Count => Tiles.Length;

    /// <summary>The tile a sample stands on.</summary>
    public Vector2i IndexOf(int sample)
    {
        return Origin + new Vector2i(sample % Width * Step, sample / Width * Step);
    }

    /// <summary>Whether a sample is open ground: it has a floor and no rock on it. Liquids and decor count as open.</summary>
    public bool IsOpen(int sample)
    {
        return Tiles[sample] != null && !Solid[sample];
    }

    /// <summary>Whether a sample can be crossed on foot: open, and not lava, liquid plasma or acid.</summary>
    public bool IsWalkable(int sample)
    {
        return IsOpen(sample) && !Hazard[sample];
    }

    /// <summary>The share of samples that are open ground.</summary>
    public float OpenFraction()
    {
        var open = 0;
        for (var i = 0; i < Count; i++)
        {
            if (IsOpen(i))
                open++;
        }

        return Count == 0 ? 0f : (float) open / Count;
    }

    /// <summary>The share of rock samples that are ore veins.</summary>
    public float VeinFraction()
    {
        var rock = Solid.Count(solid => solid);
        var veins = Vein.Count(vein => vein);
        return rock == 0 ? 0f : (float) veins / rock;
    }

    /// <summary>How many samples give off light.</summary>
    public int LightCount()
    {
        return Light.Count(light => light);
    }

    /// <summary>
    /// The walk in tiles from each walkable sample to the nearest light, stepping between 4-connected walkable samples,
    /// so rock and hazards lengthen the way; -1 where no light in the sample can be reached, and on unwalkable samples.
    /// </summary>
    public int[] LightWalks()
    {
        var walks = new int[Count];
        Array.Fill(walks, -1);
        var queue = new Queue<int>();

        for (var i = 0; i < Count; i++)
        {
            if (!Light[i])
                continue;

            walks[i] = 0;
            queue.Enqueue(i);
        }

        while (queue.TryDequeue(out var current))
        {
            var x = current % Width;
            var y = current / Width;

            Visit(x - 1, y);
            Visit(x + 1, y);
            Visit(x, y - 1);
            Visit(x, y + 1);

            void Visit(int nx, int ny)
            {
                if (nx < 0 || ny < 0 || nx >= Width || ny >= Width)
                    return;

                var next = ny * Width + nx;
                if (walks[next] >= 0 || !IsWalkable(next))
                    return;

                walks[next] = walks[current] + Step;
                queue.Enqueue(next);
            }
        }

        for (var i = 0; i < Count; i++)
        {
            if (!IsWalkable(i))
                walks[i] = -1;
        }

        return walks;
    }

    /// <summary>
    /// The walk to the nearest light (<see cref="LightWalks"/>) from each sample of the largest walkable region on
    /// <paramref name="floor"/> (any floor when null), leaving out pockets sealed in rock and the samples within
    /// <paramref name="margin"/> of the edge, whose nearest light may lie outside the sample; int.MaxValue where none
    /// can be reached.
    /// </summary>
    public List<int> LightWalksOn(string? floor, int margin = 0)
    {
        var walks = LightWalks();
        var web = LargestRegion();
        var result = new List<int>();

        for (var i = 0; i < Count; i++)
        {
            var x = i % Width;
            var y = i / Width;

            if (!web[i]
                || floor != null && Tiles[i] != floor
                || x < margin || y < margin || x >= Width - margin || y >= Width - margin)
                continue;

            result.Add(walks[i] < 0 ? int.MaxValue : walks[i]);
        }

        return result;
    }

    /// <summary>The share of <see cref="LightWalksOn"/> that reach a light within a walk of <paramref name="walk"/> tiles.</summary>
    public float LightWalkShare(int walk, string? floor = null, int margin = 0)
    {
        var walks = LightWalksOn(floor, margin);
        return walks.Count == 0 ? 0f : (float) walks.Count(w => w <= walk) / walks.Count;
    }

    /// <summary>The share of walkable samples in the largest 4-connected walkable region; lava, plasma and acid divide regions.</summary>
    public float LargestRegionShare()
    {
        var web = LargestRegion();
        var walkable = 0;
        var largest = 0;

        for (var i = 0; i < Count; i++)
        {
            if (IsWalkable(i))
                walkable++;

            if (web[i])
                largest++;
        }

        return walkable == 0 ? 0f : (float) largest / walkable;
    }

    /// <summary>Whether each sample lies in the largest 4-connected walkable region; lava, plasma and acid divide regions.</summary>
    public bool[] LargestRegion()
    {
        var region = new int[Count];
        Array.Fill(region, -1);
        var queue = new Queue<int>();
        var regions = 0;
        var largest = -1;
        var largestSize = 0;

        for (var start = 0; start < Count; start++)
        {
            if (!IsWalkable(start) || region[start] >= 0)
                continue;

            var id = regions++;
            var size = 0;
            region[start] = id;
            queue.Enqueue(start);

            while (queue.TryDequeue(out var current))
            {
                size++;
                var x = current % Width;
                var y = current / Width;

                Visit(x - 1, y, id);
                Visit(x + 1, y, id);
                Visit(x, y - 1, id);
                Visit(x, y + 1, id);
            }

            if (size <= largestSize)
                continue;

            largestSize = size;
            largest = id;
        }

        var web = new bool[Count];
        for (var i = 0; i < Count; i++)
        {
            web[i] = largest >= 0 && region[i] == largest;
        }

        return web;

        void Visit(int x, int y, int id)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Width)
                return;

            var next = y * Width + x;
            if (region[next] >= 0 || !IsWalkable(next))
                return;

            region[next] = id;
            queue.Enqueue(next);
        }
    }
}
