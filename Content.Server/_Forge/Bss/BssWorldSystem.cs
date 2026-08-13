using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server._NF.Shuttles.Components;
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
        SpawnGates();
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

        foreach (var link in network.Links)
        {
            SpawnGate(network, link.From, link.To.Sector, existing);
            SpawnGate(network, link.To, link.From.Sector, existing);
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
        Dirty(grid.Owner, gate);

        var persistent = EnsureComp<PersistentGridComponent>(grid.Owner);
        persistent.Id = $"bss-gate-{endpoint.Sector}-{endpoint.Gate}";
        persistent.MapKey = endpoint.Sector;

        EnsureComp<ForceAnchorComponent>(grid.Owner);
        var protectedGrid = EnsureComp<ProtectedGridComponent>(grid.Owner);
        protectedGrid.PreventTeleportation = true;
        protectedGrid.PreventFloorRemoval = true;

        var gravity = EnsureComp<GravityComponent>(grid.Owner);
        gravity.Enabled = true;
        gravity.Inherent = true;
        Dirty(grid.Owner, gravity);

        _shuttles.SetIFFColor(grid.Owner, source.Color.A > 0.01f ? source.Color : Color.FromHex("#35c8ff"));
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

    private bool TryGetNetwork([NotNullWhen(true)] out BssNetworkPrototype? network)
    {
        return _prototypes.TryIndex(_cfg.GetCVar(ForgeVars.BssNetwork), out network) && network != null;
    }

    private static BssSectorDefinition GetSector(BssNetworkPrototype network, string id)
    {
        return network.Sectors.First(sector => sector.Id == id);
    }
}

public readonly record struct BssLoadedSector(string Id, string Name, MapId MapId, bool Primary);
