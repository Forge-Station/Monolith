using System.Linq;
using Content.Server._Forge.BiomeSystem;
using Content.Server._Forge.Worldgen.Components; // Forge-Change
// Forge-Change
using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Server.Worldgen.Systems.Biomes;

/// <summary>
///     This handles biome selection, evaluating which biome to apply to a chunk based on noise channels.
/// </summary>
public sealed partial class BiomeSelectionSystem : BaseWorldSystem
{
    [Dependency] private NoiseIndexSystem _noiseIdx = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ISerializationManager _ser = default!;

    /// <inheritdoc />
    public override void Initialize()
    {
        SubscribeLocalEvent<BiomeSelectionComponent, ComponentStartup>(OnBiomeSelectionStartup);
        SubscribeLocalEvent<BiomeSelectionComponent, WorldChunkAddedEvent>(OnWorldChunkAdded);
        SubscribeLocalEvent<BiomeSelectionComponent, BiomeControlChangedEvent>(OnRefresh); //Forge-Change
    }

    //Forge-Change-Start
    private void OnRefresh(EntityUid uid, BiomeSelectionComponent component, BiomeControlChangedEvent args)
    {
        RefreshBiomeChunks(uid);
    }
    //Forge-Change-End

    private void OnWorldChunkAdded(EntityUid uid, BiomeSelectionComponent component, ref WorldChunkAddedEvent args)
    {
        //Forge-Change-Start
        var biome = SelectBiome(args.Chunk, args.Coords, component);

        if (biome == null)
        {
            Log.Error($"Biome selection ran out of biomes to select? See biomes list: {component.Biomes}");
            return;
        }

        biome.Apply(args.Chunk, _ser, EntityManager);
        AddComp(args.Chunk, new CurrentBiomeComponent { BiomeId = biome.ID });
        //Forge-Change-End
    }


    private void OnBiomeSelectionStartup(EntityUid uid, BiomeSelectionComponent component, ComponentStartup args) =>
        component.Biomes = component.Biomes
            .Select(x => (Id: x, _proto.Index<BiomePrototype>(x).Priority))
            .OrderByDescending(x => x.Priority)
            .Select(x => x.Id)
            .ToList();

    //Forge-Change-Start
    private BiomePrototype? SelectBiome(EntityUid chunk, Vector2i coords, BiomeSelectionComponent selection)
    {
        var lengthSquared = WorldGen.ChunkToWorldCoordsCentered(coords).LengthSquared();
        return selection.Biomes
            .Select(biomeId => _proto.Index<BiomePrototype>(biomeId))
            .Where(biome => CheckBiomeRange(biome, lengthSquared))
            .FirstOrDefault(biome => CheckBiomeValidity(chunk, biome, coords));
    }

    private void RefreshBiomeChunks(EntityUid map)
    {
        if (!map.IsValid())
            return;

        if (!TryComp<BiomeSelectionComponent>(map, out var selection))
            return;

        if (!TryComp<WorldControllerComponent>(map, out var controller))
            return;

        foreach (var (coords, chunk) in controller.Chunks)
        {
            if (SelectBiome(chunk, coords, selection) is not { } biome)
                continue;

            if (TryComp<CurrentBiomeComponent>(chunk, out var appliedBiomeComponent) && appliedBiomeComponent.BiomeId != biome.ID)
                QueueDel(chunk);
        }
    }
    // Forge-Change-End

    // Frontier: check that a given point (passed as the square of its length) meets the range requirements of a biome
    private bool CheckBiomeRange(BiomePrototype biome, float centerLengthSquared)
    {
        if (biome.DistanceRangeSquared == null)
            return true;

        return centerLengthSquared >= biome.DistanceRangeSquared.Value.X
            && centerLengthSquared <= biome.DistanceRangeSquared.Value.Y;
    }
    // End Frontier

    // Mono / EE - change like this entire function
    private bool CheckBiomeValidity(EntityUid chunk, BiomePrototype biome, Vector2i coords) =>
        (biome.MinX is null || biome.MaxX is null || biome.MinY is null || biome.MaxY is null)
            ? CheckNoiseRanges(chunk, biome, coords) : CheckSpecificChunkRange(biome, coords);

    private bool CheckNoiseRanges(EntityUid chunk, BiomePrototype biome, Vector2i coords)
    {
        foreach (var (noise, ranges) in biome.NoiseRanges)
        {
            var value = _noiseIdx.Evaluate(chunk, noise, coords);
            var anyValid = false;
            foreach (var range in ranges)
            {
                if (range.X < value && value < range.Y)
                {
                    anyValid = true;
                    break;
                }
            }

            if (!anyValid)
                return false;
        }
        return true;
    }

    private bool CheckSpecificChunkRange(BiomePrototype biome, Vector2i coords) =>
        coords.X >= biome.MinX && coords.X <= biome.MaxX && coords.Y >= biome.MinY && coords.Y <= biome.MaxY;
}
