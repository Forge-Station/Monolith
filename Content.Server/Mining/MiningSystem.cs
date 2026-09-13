using Content.Server._Forge.OrePipe;
using Content.Server._Mono.Spawning;
using Content.Server.Gatherable;
using Content.Shared._Forge.OrePipe;
using Content.Shared.Destructible;
using Content.Shared.Mining;
using Content.Shared.Mining.Components;
using Content.Shared.Random;
using Content.Shared.Random.Helpers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Mining;

/// <summary>
/// This handles creating ores when the entity is destroyed.
/// </summary>
public sealed partial class MiningSystem : EntitySystem
{
    /// <summary>
    /// Asteroid rocks destroyed by grid tear-down / RequiresGrid often have no gatherer.
    /// Pull into a nearby ship-drill buffer instead of spawning Dynamic piles (client broadphase crash).
    /// </summary>
    private const float NearbyDrillBufferRange = 12f;

    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private SpawnCountSystem _spawnCount = default!; // Mono edit - ore consolidation
    [Dependency] private OrePipeSystem _orePipe = default!; // Forge-Change: abstract drill→storage ore pipe
    [Dependency] private EntityLookupSystem _lookup = default!; // Forge-Change
    [Dependency] private SharedTransformSystem _transform = default!; // Forge-Change

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OreVeinComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<OreVeinComponent, DestructionEventArgs>(OnDestruction);
        SubscribeLocalEvent<OreVeinComponent, GatheredEvent>(OnGather); // Mono edit
    }

    private void OnDestruction(EntityUid uid, OreVeinComponent component, DestructionEventArgs args)
    {
        Mine(uid, component); // mono
    }

    /// <summary>
    /// Monolith - Mining now also uses gathered event.
    /// </summary>
    private void OnGather(EntityUid uid, OreVeinComponent component, GatheredEvent args)
    {
        Mine(uid, component, args.Gatherer, args.TeleportLootToGatherer);
    }

    /// <summary>
    /// Monolith - Moved out method
    /// </summary>
    public void Mine(EntityUid uid, OreVeinComponent component, EntityUid? gatherer = null, bool spawnOnGatherer = false)
    {
        if (component.CurrentOre == null)
            return;

        if (component.PreventSpawning)
            return;

        var proto = _proto.Index(component.CurrentOre);

        if (proto.OreEntity == null)
            return;

        var yield = _random.Next(proto.MinOreYield, proto.MaxOreYield + 1);
        var oreEntity = proto.OreEntity.Value;

        // Forge-Change: mob/anomaly spawners must appear in the world, not in the abstract buffer.
        if (MustSpawnInWorld(oreEntity))
        {
            var worldCoords = gatherer != null && spawnOnGatherer
                ? Transform(gatherer.Value).Coordinates
                : Transform(uid).Coordinates;
            _spawnCount.SpawnCount(oreEntity, worldCoords.Offset(_random.NextVector2(0.2f)), yield);
            component.PreventSpawning = true;
            return;
        }

        // Forge-Change: ship drill gather → abstract buffer (never Dynamic ore piles).
        if (gatherer != null && HasComp<OrePipeBufferComponent>(gatherer.Value))
        {
            _orePipe.TryDepositOre(gatherer.Value, oreEntity, yield);
            component.PreventSpawning = true;
            return;
        }

        // Forge-Change: destruction without gatherer (grid remove / RequiresGrid) near a drill.
        if (gatherer == null && TryDepositToNearbyDrill(uid, oreEntity, yield))
        {
            component.PreventSpawning = true;
            return;
        }

        // Forge-Change: never spawn Dynamic ore with no gatherer — that path corrupts client broadphase
        // when asteroid grids are torn down mid-mining.
        if (gatherer == null)
        {
            component.PreventSpawning = true;
            return;
        }

        // Hand tools / projectiles without an ore-pipe buffer — normal world drop.
        var coords = spawnOnGatherer
            ? Transform(gatherer.Value).Coordinates
            : Transform(uid).Coordinates;

        _spawnCount.SpawnCount(oreEntity, coords.Offset(_random.NextVector2(0.2f)), yield);
        component.PreventSpawning = true;
    }

    private bool TryDepositToNearbyDrill(EntityUid vein, EntProtoId oreEntity, int yield)
    {
        var mapCoords = _transform.GetMapCoordinates(vein);
        if (mapCoords.MapId == MapId.Nullspace)
            return false;

        foreach (var ent in _lookup.GetEntitiesInRange<OrePipeBufferComponent>(mapCoords, NearbyDrillBufferRange, LookupFlags.Static | LookupFlags.Dynamic))
        {
            if (TerminatingOrDeleted(ent.Owner))
                continue;

            if (_orePipe.TryDepositOre(ent.Owner, oreEntity, yield, ent.Comp))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Ore veins that spawn living mobs / special effects must not go into the drill buffer.
    /// </summary>
    private bool MustSpawnInWorld(EntProtoId oreEntity)
    {
        var id = oreEntity.Id;
        return id.StartsWith("MobSpawn", StringComparison.Ordinal)
               || id.Contains("Spawner", StringComparison.Ordinal)
               || id.Contains("Anomaly", StringComparison.Ordinal);
    }

    private void OnMapInit(EntityUid uid, OreVeinComponent component, MapInitEvent args)
    {
        if (component.CurrentOre != null || component.OreRarityPrototypeId == null || !_random.Prob(component.OreChance))
            return;

        component.CurrentOre = _proto.Index<WeightedRandomOrePrototype>(component.OreRarityPrototypeId).Pick(_random);
    }
}
