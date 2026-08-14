using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server._NF.Shuttles.Components;
using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Prototypes;
using Content.Shared._Forge;
using Content.Shared._Forge.Bss;
using Content.Shared._Forge.Persistence;
using Content.Shared.GameTicking;
using Content.Shared.Gravity;
using Content.Shared.Maps;
using Content.Shared.Parallax;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Tiles;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Forge.Bss;

/// <summary>
/// Creates extra sector maps and linear BSS gate grids for the configured network.
/// </summary>
public sealed class BssWorldSystem : EntitySystem
{
    public const string GatePrototype = "BssWarpGateCore";
    private const float GateJumpRange = 90f;
    private const float ArrivalDistance = 70f;
    private const int PlatformRadius = 5;
    private const int PlatformDeckRadius = 2;

    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly ITileDefinitionManager _tiles = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedShuttleSystem _shuttles = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private readonly Dictionary<string, MapId> _sectorMaps = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, MapId> SectorMaps => _sectorMaps;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting, after: [typeof(Persistence.PersistentWorldSystem)]);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _sectorMaps.Clear());
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        EnsureSectorMaps();
        PurgeTransientRoutes();
        ApplyWorldgen();
        SpawnGates();
        PruneObsoleteGates();
    }

    public IReadOnlyList<BssLoadedSector> GetLoadedSectors()
    {
        if (_sectorMaps.Count == 0)
            EnsureSectorMaps();

        if (!TryGetNetwork(out var network))
            return [];

        var result = new List<BssLoadedSector>(network.Sectors.Count);
        foreach (var sector in network.Sectors)
        {
            if (!_sectorMaps.TryGetValue(sector.Id, out var mapId))
                continue;

            result.Add(new BssLoadedSector(sector.Id, sector.Name, mapId, sector.Primary));
        }

        return result;
    }

    public bool TryGetMap(string sector, out MapId mapId)
    {
        return _sectorMaps.TryGetValue(sector, out mapId);
    }

    public string? GetPrimarySectorId()
    {
        return TryGetNetwork(out var network)
            ? (network.Sectors.FirstOrDefault(sector => sector.Primary) ?? network.Sectors.FirstOrDefault())?.Id
            : null;
    }

    public void EnsureSectorMaps()
    {
        if (!TryGetNetwork(out var network) || _ticker.DefaultMap == MapId.Nullspace)
            return;

        // Include paused maps. Extra sectors are created with runMapInit:false and
        // EntityQueryEnumerator would miss them, causing a second set of empty maps.
        _sectorMaps.Clear();
        var query = AllEntityQuery<BssSectorMapComponent, TransformComponent>();
        while (query.MoveNext(out _, out var sectorMap, out var xform))
        {
            if (xform.MapID == MapId.Nullspace || string.IsNullOrEmpty(sectorMap.Sector))
                continue;

            if (_sectorMaps.ContainsKey(sectorMap.Sector))
                continue;

            _sectorMaps[sectorMap.Sector] = xform.MapID;
        }

        var primary = network.Sectors.FirstOrDefault(sector => sector.Primary) ?? network.Sectors.FirstOrDefault();
        if (primary == null)
            return;

        BindSector(_ticker.DefaultMap, network, primary);

        foreach (var sector in network.Sectors)
        {
            if (sector.Id == primary.Id)
                continue;

            if (_sectorMaps.TryGetValue(sector.Id, out var existing) && _maps.MapExists(existing))
                continue;

            var mapUid = _maps.CreateMap(out var mapId, runMapInit: false);
            BindSector(mapId, network, sector, mapUid);
            Log.Info($"Created BSS sector map '{sector.Id}' ({mapId}).");
        }
    }

    public void SpawnGates()
    {
        if (!TryGetNetwork(out var network))
            return;

        var existing = new HashSet<(string Sector, string Gate)>();
        var query = AllEntityQuery<BssGateComponent>();
        while (query.MoveNext(out _, out var gate))
        {
            if (gate.Network == network.ID)
                existing.Add((gate.Sector, gate.Gate));
        }

        foreach (var link in GetAllLinks(network))
        {
            SpawnGate(network, link.From, link.To.Sector, existing);
            SpawnGate(network, link.To, link.From.Sector, existing);
        }
    }

    /// <summary>
    /// Drops leftover gates whose endpoints are no longer in the prototype graph
    /// (e.g. old green-to-green hops after a topology retune).
    /// </summary>
    private void PruneObsoleteGates()
    {
        if (!TryGetNetwork(out var network))
            return;

        var valid = new HashSet<(string Sector, string Gate)>();
        foreach (var link in GetAllLinks(network))
        {
            valid.Add((link.From.Sector, link.From.Gate));
            valid.Add((link.To.Sector, link.To.Gate));
        }

        var doomed = new List<EntityUid>();
        var query = AllEntityQuery<BssGateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var gate, out var xform))
        {
            if (gate.Network != network.ID)
                continue;

            if (valid.Contains((gate.Sector, gate.Gate)))
                continue;

            doomed.Add(xform.GridUid is { Valid: true } grid ? grid : uid);
        }

        foreach (var uid in doomed.Distinct())
        {
            if (Exists(uid))
                Del(uid);
        }
    }

    private void SpawnGate(
        BssNetworkPrototype network,
        BssGateEndpoint endpoint,
        string neighborSector,
        HashSet<(string Sector, string Gate)> existing)
    {
        if (!existing.Add((endpoint.Sector, endpoint.Gate)))
            return;

        if (!_sectorMaps.TryGetValue(endpoint.Sector, out var mapId) || !_maps.MapExists(mapId))
            return;

        var source = GetSector(network, endpoint.Sector);
        var neighbor = GetSector(network, neighborSector);
        var direction = neighbor.Position - source.Position;
        if (direction.LengthSquared() < 0.001f)
            direction = new Vector2(1f, 0f);
        direction = Vector2.Normalize(direction);

        var minDistance = MathF.Max(0f, _cfg.GetCVar(ForgeVars.BssGateDistance));
        var maxDistance = MathF.Max(minDistance, _cfg.GetCVar(ForgeVars.BssGateDistanceMax));
        var distance = minDistance >= maxDistance
            ? minDistance
            : _random.NextFloat(minDistance, maxDistance);
        var worldPosition = direction * distance;
        var grid = _mapManager.CreateGridEntity(mapId);
        _transform.SetWorldPosition(grid.Owner, worldPosition);
        _transform.SetWorldRotation(grid.Owner, direction.ToWorldAngle());

        var lattice = _tiles["Lattice"];
        var steel = _tiles["FloorSteel"];
        var tiles = new List<(Vector2i, Tile)>( (PlatformRadius * 2 + 1) * (PlatformRadius * 2 + 1) );
        for (var x = -PlatformRadius; x <= PlatformRadius; x++)
        {
            for (var y = -PlatformRadius; y <= PlatformRadius; y++)
            {
                var radius = Math.Max(Math.Abs(x), Math.Abs(y));
                var tile = radius <= PlatformDeckRadius ? steel : lattice;
                tiles.Add((new Vector2i(x, y), new Tile(tile.TileId)));
            }
        }

        _maps.SetTiles(grid.Owner, grid.Comp, tiles);

        if (TryComp(grid.Owner, out PhysicsComponent? physics))
        {
            _physics.SetBodyType(grid.Owner, BodyType.Static, body: physics);
            _physics.SetBodyStatus(grid.Owner, physics, BodyStatus.OnGround);
            _physics.SetFixedRotation(grid.Owner, true, body: physics);
        }

        var name = Loc.GetString("shuttle-console-bss-gate-name",
            ("sector", source.Name),
            ("destination", neighbor.Name));
        _meta.SetEntityName(grid.Owner, name);

        var gate = EnsureComp<BssGateComponent>(grid.Owner);
        gate.Network = network.ID;
        gate.Sector = endpoint.Sector;
        gate.Gate = endpoint.Gate;
        gate.JumpRange = GateJumpRange;
        gate.ArrivalOffset = new Vector2(ArrivalDistance, 0f);
        gate.Enabled = true;
        gate.Persistent = source.Access != BssSystemAccess.Hidden
                          && neighbor.Access != BssSystemAccess.Hidden;
        Dirty(grid.Owner, gate);

        if (gate.Persistent)
        {
            var persistent = EnsureComp<PersistentGridComponent>(grid.Owner);
            persistent.Id = $"bss-gate-{endpoint.Sector}-{endpoint.Gate}";
            persistent.MapKey = endpoint.Sector;
        }

        EnsureComp<ForceAnchorComponent>(grid.Owner);
        var protectedGrid = EnsureComp<ProtectedGridComponent>(grid.Owner);
        protectedGrid.PreventTeleportation = true;
        protectedGrid.PreventFloorRemoval = true;

        var gravity = EnsureComp<GravityComponent>(grid.Owner);
        gravity.Enabled = true;
        gravity.Inherent = true;
        Dirty(grid.Owner, gravity);

        _shuttles.SetIFFColor(grid.Owner, network.ResolveColor(source));
        _shuttles.SetIFFReadOnly(grid.Owner, true);

        Spawn(GatePrototype, new EntityCoordinates(grid.Owner, Vector2.Zero));
        Log.Info($"Spawned BSS gate '{endpoint.Gate}' on sector '{endpoint.Sector}' at {worldPosition}.");
    }

    private void BindSector(
        MapId mapId,
        BssNetworkPrototype network,
        BssSectorDefinition sector,
        EntityUid? mapUid = null)
    {
        mapUid ??= _maps.GetMap(mapId);
        var sectorMap = EnsureComp<BssSectorMapComponent>(mapUid.Value);
        sectorMap.Network = network.ID;
        sectorMap.Sector = sector.Id;
        sectorMap.Primary = sector.Primary;
        Dirty(mapUid.Value, sectorMap);

        var persistent = EnsureComp<PersistentMapComponent>(mapUid.Value);
        persistent.Id = sector.Id;

        if (sector.Primary)
            EnsureComp<BssDynamicRoutesComponent>(mapUid.Value);

        if (!string.IsNullOrEmpty(sector.Parallax))
        {
            var parallax = EnsureComp<ParallaxComponent>(mapUid.Value);
            parallax.Parallax = sector.Parallax;
            Dirty(mapUid.Value, parallax);
        }

        if (!sector.Primary)
            RemComp<FTLDestinationComponent>(mapUid.Value);

        _meta.SetEntityName(mapUid.Value, sector.Name);
        _sectorMaps[sector.Id] = mapId;
    }

    public bool TryGetNetwork([NotNullWhen(true)] out BssNetworkPrototype? network)
    {
        return _prototypes.TryIndex(_cfg.GetCVar(ForgeVars.BssNetwork), out network) && network != null;
    }

    public IReadOnlyList<BssGateLinkDefinition> GetAllLinks(BssNetworkPrototype network)
    {
        if (!TryGetDynamicRoutes(out var routes) || routes.Links.Count == 0)
            return network.Links;

        var links = new List<BssGateLinkDefinition>(network.Links.Count + routes.Links.Count);
        links.AddRange(network.Links);
        links.AddRange(routes.Links);
        return links;
    }

    public bool IsSystemDiscovered(BssNetworkPrototype network, string systemId)
    {
        var system = network.GetSystem(systemId);
        if (system == null)
            return false;

        if (system.Access != BssSystemAccess.Hidden)
            return true;

        return GetAllLinks(network).Any(link =>
            link.From.Sector == systemId || link.To.Sector == systemId);
    }

    public bool TryUnlockHiddenRoute(
        string? preferredFrom,
        string? preferredTo,
        [NotNullWhen(true)] out string? fromId,
        [NotNullWhen(true)] out string? toId,
        out string reason)
    {
        fromId = null;
        toId = null;
        reason = "bss-void-no-network";

        if (!TryGetNetwork(out var network))
            return false;

        EnsureSectorMaps();

        var hidden = network.Sectors
            .Where(system => system.Access == BssSystemAccess.Hidden && !IsSystemDiscovered(network, system.Id))
            .ToList();
        if (hidden.Count == 0)
        {
            reason = "bss-void-none-left";
            return false;
        }

        var destination = hidden.FirstOrDefault(system => system.Id == preferredTo) ?? _random.Pick(hidden);
        var redOrigins = network.Sectors
            .Where(system => system.Group == "red" && system.Access != BssSystemAccess.Hidden)
            .ToList();
        if (redOrigins.Count == 0)
        {
            reason = "bss-void-no-origin";
            return false;
        }

        var origin = redOrigins.FirstOrDefault(system => system.Id == preferredFrom) ?? _random.Pick(redOrigins);
        var link = new BssGateLinkDefinition
        {
            From = new BssGateEndpoint { Sector = origin.Id, Gate = $"{origin.Id}-{destination.Id}" },
            To = new BssGateEndpoint { Sector = destination.Id, Gate = $"{destination.Id}-{origin.Id}" },
            Bidirectional = true,
        };

        var routes = EnsureDynamicRoutes();
        if (routes.Links.Any(existing =>
                existing.From.Sector == link.From.Sector && existing.To.Sector == link.To.Sector ||
                existing.From.Sector == link.To.Sector && existing.To.Sector == link.From.Sector))
        {
            reason = "bss-void-already-open";
            return false;
        }

        routes.Links.Add(link);
        Dirty(GetRoutesEntity(), routes);
        SpawnGates();

        fromId = origin.Id;
        toId = destination.Id;
        reason = string.Empty;
        Log.Info($"Opened hidden BSS route {origin.Id} -> {destination.Id}.");
        return true;
    }

    public void PreparePersistenceSave()
    {
        PurgeTransientRoutes();
    }

    private void PurgeTransientRoutes()
    {
        if (TryGetDynamicRoutes(out var routes) && routes.Links.Count > 0)
        {
            routes.Links.Clear();
            Dirty(GetRoutesEntity(), routes);
        }

        if (!TryGetNetwork(out var network))
            return;

        var doomed = new List<EntityUid>();
        var query = AllEntityQuery<BssGateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var gate, out var xform))
        {
            if (gate.Persistent && !IsTransientGate(network, gate))
                continue;

            doomed.Add(xform.GridUid is { Valid: true } grid ? grid : uid);
        }

        foreach (var uid in doomed.Distinct())
        {
            if (Exists(uid))
                Del(uid);
        }
    }

    private static bool IsTransientGate(BssNetworkPrototype network, BssGateComponent gate)
    {
        if (!gate.Persistent)
            return true;

        var system = network.GetSystem(gate.Sector);
        if (system?.Access == BssSystemAccess.Hidden)
            return true;

        return network.Sectors.Any(candidate =>
            candidate.Access == BssSystemAccess.Hidden &&
            (gate.Sector == candidate.Id ||
             gate.Gate.Contains(candidate.Id, StringComparison.Ordinal)));
    }

    private void ApplyWorldgen()
    {
        if (!TryGetNetwork(out var network))
            return;

        foreach (var system in network.Sectors)
        {
            if (!_sectorMaps.TryGetValue(system.Id, out var mapId) || !_maps.MapExists(mapId))
                continue;

            var mapUid = _maps.GetMap(mapId);
            var biomes = ResolveBiomes(system);
            if (biomes.Count == 0)
            {
                Log.Error($"No usable worldgen biomes for BSS system '{system.Id}'.");
                continue;
            }

            RemComp<WorldControllerComponent>(mapUid);
            RemComp<BiomeSelectionComponent>(mapUid);
            EnsureComp<WorldControllerComponent>(mapUid);
            AddComp(mapUid, new BiomeSelectionComponent { Biomes = biomes });
        }
    }

    private List<string> ResolveBiomes(BssSectorDefinition system)
    {
        var biomes = new List<string>();
        var configId = string.IsNullOrEmpty(system.Worldgen)
            ? DefaultWorldgen(system.Group)
            : system.Worldgen;

        if (_prototypes.TryIndex<WorldgenConfigPrototype>(configId, out var config))
        {
            foreach (var data in config.Components.Values)
            {
                if (data.Component is not BiomeSelectionComponent selection)
                    continue;

                foreach (var biome in selection.Biomes)
                {
                    if (_prototypes.HasIndex<BiomePrototype>(biome))
                        biomes.Add(biome);
                    else
                        Log.Warning($"Skipping missing biome '{biome}' for BSS system '{system.Id}'.");
                }
            }
        }
        else
        {
            Log.Error($"Missing worldgen config '{configId}' for BSS system '{system.Id}'.");
        }

        if (biomes.Count > 0)
            return biomes;

        foreach (var biome in FallbackBiomes(system.Group))
        {
            if (_prototypes.HasIndex<BiomePrototype>(biome))
                biomes.Add(biome);
        }

        return biomes;
    }

    private static IEnumerable<string> FallbackBiomes(string group)
    {
        return group switch
        {
            "yellow" => ["MonoEmptyFallback", "MonoWorldgenAsteroidRing9km"],
            "red" => ["MonoEmptyFallback", "MonoWildsCore"],
            "black" => ["MonoEmptyFallback", "MonoWorldgenHyperwarAsteroidRing"],
            _ => ["MonoEmptyFallback", "NFAsteroidsNear"],
        };
    }

    private static string DefaultWorldgen(string group)
    {
        return group switch
        {
            "yellow" => "ForgeWorldgenYellow",
            "red" => "ForgeWorldgenRed",
            "black" => "ForgeWorldgenBlack",
            _ => "ForgeWorldgenGreen",
        };
    }

    private bool TryGetDynamicRoutes([NotNullWhen(true)] out BssDynamicRoutesComponent? routes)
    {
        var uid = GetRoutesEntity();
        return TryComp(uid, out routes);
    }

    private BssDynamicRoutesComponent EnsureDynamicRoutes()
    {
        return EnsureComp<BssDynamicRoutesComponent>(GetRoutesEntity());
    }

    private EntityUid GetRoutesEntity()
    {
        var primary = GetPrimarySectorId();
        if (primary != null && _sectorMaps.TryGetValue(primary, out var mapId) && _maps.MapExists(mapId))
            return _maps.GetMap(mapId);

        return _maps.GetMap(_ticker.DefaultMap);
    }

    private static BssSectorDefinition GetSector(BssNetworkPrototype network, string id)
    {
        return network.Sectors.First(sector => sector.Id == id);
    }
}

public readonly record struct BssLoadedSector(string Id, string Name, MapId MapId, bool Primary);
