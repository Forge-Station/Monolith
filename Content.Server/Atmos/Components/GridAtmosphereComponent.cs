using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Atmos.Serialization;
using Content.Server.NodeContainer.NodeGroups;

namespace Content.Server.Atmos.Components
{
    /// <summary>
    ///     Internal Atmos class. Use <see cref="AtmosphereSystem"/> to interact with atmos instead.
    /// </summary>
    [RegisterComponent, Serializable,
     Access(typeof(AtmosphereSystem), typeof(GasTileOverlaySystem), typeof(AtmosDebugOverlaySystem))]
    public sealed partial class GridAtmosphereComponent : Component
    {
        [ViewVariables(VVAccess.ReadWrite)]
        public bool Simulated { get; set; } = true;

        [ViewVariables]
        public bool ProcessingPaused { get; set; } = false;

        [ViewVariables]
        public float Timer { get; set; } = 0f;

        [ViewVariables]
        public int UpdateCounter { get; set; } = 1; // DO NOT SET TO ZERO BY DEFAULT! It will break roundstart atmos...

        [ViewVariables]
        [IncludeDataField(customTypeSerializer:typeof(TileAtmosCollectionSerializer))]
        public Dictionary<Vector2i, TileAtmosphere> Tiles = new(128); // Forge-Change

        [ViewVariables]
        public readonly Dictionary<Vector2i, AtmosChunkState> Chunks = new(64);

        private HashSet<TileAtmosphere>? _mapTiles; // Forge-Change

        [ViewVariables]
        public HashSet<TileAtmosphere> MapTiles => _mapTiles ??= new HashSet<TileAtmosphere>(64); // Forge-Change

        [ViewVariables]
        public readonly HashSet<TileAtmosphere> ActiveTiles = new(128); // Forge-Change

        [ViewVariables]
        public int ActiveTilesCount => ActiveTiles.Count;

        [ViewVariables]
        public readonly HashSet<ExcitedGroup> ExcitedGroups = new(64); // Forge-Change

        [ViewVariables]
        public int ExcitedGroupCount => ExcitedGroups.Count;

        [ViewVariables]
        public readonly HashSet<TileAtmosphere> HotspotTiles = new(64); // Forge-Change

        [ViewVariables]
        public int HotspotTilesCount => HotspotTiles.Count;

        [ViewVariables]
        public readonly HashSet<TileAtmosphere> SuperconductivityTiles = new(64); // Forge-Change

        [ViewVariables]
        public int SuperconductivityTilesCount => SuperconductivityTiles.Count;

        private HashSet<TileAtmosphere>? _highPressureDelta; // Forge-Change

        [ViewVariables]
        public HashSet<TileAtmosphere> HighPressureDelta => _highPressureDelta ??= new HashSet<TileAtmosphere>(64); // Forge-Change

        [ViewVariables]
        public int HighPressureDeltaCount => HighPressureDelta.Count;

        [ViewVariables]
        public readonly HashSet<IPipeNet> PipeNets = new();

        [ViewVariables]
        public readonly HashSet<Entity<AtmosDeviceComponent>> AtmosDevices = new();

        [ViewVariables]
        public readonly List<TileAtmosphere> CurrentRunTiles = new(256); // Forge-Change

        [ViewVariables]
        public readonly List<Vector2i> CurrentRunChunks = new(64);

        [ViewVariables]
        public int CurrentRunChunkIndex;

        [ViewVariables]
        public int CurrentRunTileIndex; // Forge-Change

        [ViewVariables]
        public readonly List<ExcitedGroup> CurrentRunExcitedGroups = new(128); // Forge-Change

        [ViewVariables]
        public int CurrentRunExcitedGroupIndex; // Forge-Change

        [ViewVariables]
        public readonly List<IPipeNet> CurrentRunPipeNet = new(64); // Forge-Change

        [ViewVariables]
        public int CurrentRunPipeNetIndex; // Forge-Change

        [ViewVariables]
        public readonly List<Entity<AtmosDeviceComponent>> CurrentRunAtmosDevices = new(128); // Forge-Change

        [ViewVariables]
        public int CurrentRunAtmosDeviceIndex; // Forge-Change

        [ViewVariables]
        public readonly HashSet<Vector2i> InvalidatedCoords = new(128); // Forge-Change

        [ViewVariables]
        public readonly Queue<TileAtmosphere> CurrentRunInvalidatedTiles = new();

        [ViewVariables]
        public readonly List<TileAtmosphere> PossiblyDisconnectedTiles = new(100);

        [ViewVariables]
        public int InvalidatedCoordsCount => InvalidatedCoords.Count;

        [ViewVariables]
        public long EqualizationQueueCycleControl { get; set; }

        [ViewVariables]
        public AtmosphereProcessingState State { get; set; } = AtmosphereProcessingState.Revalidate;
    }

    [Serializable]
    public sealed class AtmosChunkState
    {
        [ViewVariables]
        public readonly HashSet<Vector2i> InvalidatedCoords = new(16);

        [ViewVariables]
        public readonly HashSet<TileAtmosphere> ActiveTiles = new(32);

        [ViewVariables]
        public readonly HashSet<TileAtmosphere> HighPressureTiles = new(16);

        [ViewVariables]
        public readonly HashSet<TileAtmosphere> HotspotTiles = new(16);

        [ViewVariables]
        public readonly HashSet<TileAtmosphere> SuperconductivityTiles = new(16);

        [ViewVariables]
        public int NextColdCycle;

        [ViewVariables]
        public int LastTouchedCycle;
    }
}
