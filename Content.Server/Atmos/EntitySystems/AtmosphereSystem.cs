using Content.Server.Administration.Logs;
using Content.Server.Atmos.Components;
using Content.Server.Body.Systems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Shared.Atmos.Components; // Forge-Change
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.Decals;
using Content.Shared.Doors.Components;
using Content.Shared.Maps;
using JetBrains.Annotations;
using Prometheus; // Forge-Change
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components; // Forge-Change
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Server.Atmos.EntitySystems;

/// <summary>
///     This is our SSAir equivalent, if you need to interact with or query atmos in any way, go through this.
/// </summary>
[UsedImplicitly]
public sealed partial class AtmosphereSystem : SharedAtmosphereSystem
{
    // Forge-Change-start
    private static readonly Gauge AtmosGridCountGauge = Metrics.CreateGauge(
        "forge_atmos_grids",
        "Number of simulated grids with GridAtmosphereComponent.");

    private static readonly Gauge AtmosTileCountGauge = Metrics.CreateGauge(
        "forge_atmos_tiles_total",
        "Total tile entries tracked by atmos.");

    private static readonly Gauge AtmosActiveTileCountGauge = Metrics.CreateGauge(
        "forge_atmos_tiles_active",
        "Active atmos tiles queued for processing.");

    private static readonly Gauge AtmosInvalidatedTileCountGauge = Metrics.CreateGauge(
        "forge_atmos_tiles_invalidated",
        "Invalidated atmos tiles waiting for revalidation.");

    private static readonly Gauge AtmosHotspotTileCountGauge = Metrics.CreateGauge(
        "forge_atmos_hotspots",
        "Tiles with active hotspot simulation.");

    private static readonly Gauge AtmosChunkCountGauge = Metrics.CreateGauge(
        "forge_atmos_chunks_total",
        "Total atmos chunks tracked in all grids.");

    private static readonly Gauge AtmosDirtyChunkCountGauge = Metrics.CreateGauge(
        "forge_atmos_chunks_dirty",
        "Atmos chunks with pending revalidation work.");

    private static readonly Gauge AtmosHotChunkCountGauge = Metrics.CreateGauge(
        "forge_atmos_chunks_hot",
        "Atmos chunks with active simulation sets.");

    private static readonly Gauge AtmosColdChunkCountGauge = Metrics.CreateGauge(
        "forge_atmos_chunks_cold",
        "Atmos chunks that are idle this sample.");

    private const float MetricsUpdateInterval = 30f;
    private float _metricsTimer;
    // Forge-Change-end
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefinitionManager = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly InternalsSystem _internals = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly GasTileOverlaySystem _gasTileOverlaySystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly TileSystem _tile = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] public readonly PuddleSystem Puddle = default!;

    private const float ExposedUpdateDelay = 1f;
    private float _exposedTimer = 0f;

    private EntityQuery<GridAtmosphereComponent> _atmosQuery;
    private EntityQuery<MapAtmosphereComponent> _mapAtmosQuery;
    private EntityQuery<AirtightComponent> _airtightQuery;
    private EntityQuery<FirelockComponent> _firelockQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery; // Forge-Change
    private EntityQuery<TransformComponent> _xformQuery; // Forge-Change
    private EntityQuery<MetaDataComponent> _metaQuery; // Forge-Change
    private EntityQuery<MovedByPressureComponent> _movedByPressureQuery; // Forge-Change
    private HashSet<EntityUid> _entSet = new();

    private string[] _burntDecals = [];

    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(NodeGroupSystem));

        InitializeBreathTool();
        InitializeGases();
        InitializeCommands();
        InitializeCVars();
        InitializeGridAtmosphere();
        InitializeMap();

        _mapAtmosQuery = GetEntityQuery<MapAtmosphereComponent>();
        _atmosQuery = GetEntityQuery<GridAtmosphereComponent>();
        _airtightQuery = GetEntityQuery<AirtightComponent>();
        _firelockQuery = GetEntityQuery<FirelockComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>(); // Forge-Change
        _xformQuery = GetEntityQuery<TransformComponent>(); // Forge-Change
        _metaQuery = GetEntityQuery<MetaDataComponent>(); // Forge-Change
        _movedByPressureQuery = GetEntityQuery<MovedByPressureComponent>(); // Forge-Change

        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        CacheDecals();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        ShutdownCommands();
    }

    private void OnTileChanged(ref TileChangedEvent ev)
    {
        foreach (var change in ev.Changes)
        {
            InvalidateTile(ev.Entity.Owner, change.GridIndices);
        }
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs ev)
    {
        if (ev.WasModified<DecalPrototype>())
            CacheDecals();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateProcessing(frameTime);
        UpdateHighPressure(frameTime);

        // Forge-Change-start
        _metricsTimer += frameTime;
        if (_metricsTimer >= MetricsUpdateInterval)
        {
            _metricsTimer -= MetricsUpdateInterval;
            UpdateAtmosMetrics();
        }
        // Forge-Change-end

        _exposedTimer += frameTime;

        if (_exposedTimer < ExposedUpdateDelay)
            return;

        var query = EntityQueryEnumerator<AtmosExposedComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transform))
        {
            var air = GetContainingMixture((uid, transform));

            if (air == null)
                continue;

            var updateEvent = new AtmosExposedUpdateEvent(transform.Coordinates, air, transform);
            RaiseLocalEvent(uid, ref updateEvent);
        }

        _exposedTimer -= ExposedUpdateDelay;
    }

    private void CacheDecals()
    {
        _burntDecals = _protoMan.EnumeratePrototypes<DecalPrototype>().Where(x => x.Tags.Contains("burnt")).Select(x => x.ID).ToArray();
    }
    // Forge-Change-start
    private void UpdateAtmosMetrics()
    {
        var grids = 0;
        var tiles = 0;
        var activeTiles = 0;
        var invalidated = 0;
        var hotspots = 0;
        var chunks = 0;
        var dirtyChunks = 0;
        var hotChunks = 0;
        var coldChunks = 0;

        var query = EntityQueryEnumerator<GridAtmosphereComponent>();
        while (query.MoveNext(out _, out var atmos))
        {
            grids++;
            tiles += atmos.Tiles.Count;
            activeTiles += atmos.ActiveTiles.Count;
            invalidated += atmos.InvalidatedCoords.Count;
            hotspots += atmos.HotspotTiles.Count;
            chunks += atmos.Chunks.Count;

            foreach (var chunk in atmos.Chunks.Values)
            {
                var isDirty = chunk.InvalidatedCoords.Count > 0;
                var isHot = chunk.ActiveTiles.Count > 0
                    || chunk.HighPressureTiles.Count > 0
                    || chunk.HotspotTiles.Count > 0
                    || chunk.SuperconductivityTiles.Count > 0;

                if (isDirty)
                    dirtyChunks++;

                if (isHot)
                    hotChunks++;
                else
                    coldChunks++;
            }
        }

        AtmosGridCountGauge.Set(grids);
        AtmosTileCountGauge.Set(tiles);
        AtmosActiveTileCountGauge.Set(activeTiles);
        AtmosInvalidatedTileCountGauge.Set(invalidated);
        AtmosHotspotTileCountGauge.Set(hotspots);
        AtmosChunkCountGauge.Set(chunks);
        AtmosDirtyChunkCountGauge.Set(dirtyChunks);
        AtmosHotChunkCountGauge.Set(hotChunks);
        AtmosColdChunkCountGauge.Set(coldChunks);
    }
    // Forge-Change-end
}
