using System.Numerics;
using System.IO;
using System.Linq;
using Content.Server._Forge.Bss;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.Maps;
using Content.Server.Station.Systems;
using Content.Shared._Forge;
using Content.Shared._Forge.Bss;
using Content.Shared._Forge.Persistence;
using Content.Shared.CCVar;
using Content.Shared.Maps;
using Content.Shared.Station.Components;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Content.Server._Forge.Persistence;

/// <summary>
/// Saves marked maps and grids and restores them across server restarts until the weekly wipe.
/// The manifest and all payloads are file-backed and do not use the database.
/// </summary>
public sealed class PersistentWorldSystem : EntitySystem
{
    private const string ManifestFileName = "manifest.yml";

    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly IGameMapManager _gameMap = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly GridPersistenceService _persistence = default!;
    [Dependency] private readonly BssWorldSystem _bssWorld = default!;
    [Dependency] private readonly StationSystem _station = default!;

    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private PersistenceManifest? _loadedManifest;
    private bool _restoredWorld;

    private ResPath ManifestPath => _persistence.MapsPath / ManifestFileName;

    /// <summary>
    /// True when this round loaded a persisted world instead of spawning a fresh one.
    /// </summary>
    public bool HasLoadedWorld => _restoredWorld;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LoadingMapsEvent>(OnLoadingMaps);
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<BeforeRoundRestartPersistenceEvent>(OnBeforeRoundRestart);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _loadedManifest = null;
            _restoredWorld = false;
        });
    }

    public override void Shutdown()
    {
        // Process stop must keep the weekly world, not wait for the next round restart.
        if (Enabled && HasPersistentEntities())
            SaveWorld();

        base.Shutdown();
    }

    private bool Enabled => _configuration.GetCVar(ForgeVars.PersistenceEnabled);

    private TimeSpan CycleLength
    {
        get
        {
            var days = Math.Max(1, _configuration.GetCVar(ForgeVars.PersistenceCycleDays));
            return TimeSpan.FromDays(days);
        }
    }

    private void OnLoadingMaps(LoadingMapsEvent ev)
    {
        _restoredWorld = false;

        if (!Enabled)
            return;

        if (!TryReadManifest(out var manifest) || manifest.Entries.Count == 0)
        {
            if (!TryRecoverManifestFromFiles(out manifest))
            {
                Log.Info("No persisted world to restore; a fresh map will be spawned.");
                return;
            }
        }

        if (IsCycleExpired(manifest))
        {
            Log.Info($"Persistence cycle started {GetCycleStart(manifest):u} has expired; wiping world.");
            ClearPersistence();
            return;
        }

        var mapEntries = manifest.Entries
            .Where(entry => entry.Kind == PersistenceEntryKind.Map && _persistence.Exists(new ResPath(entry.SavePath)))
            .ToList();

        if (mapEntries.Count == 0)
        {
            Log.Warning("Persistence manifest has no loadable map files; spawning a fresh world.");
            return;
        }

        _loadedManifest = manifest;

        var template = _gameMap.GetSelectedMap();
        if (template == null &&
            !_prototypes.TryIndex<GameMapPrototype>(_configuration.GetCVar(CCVars.PersistenceMap), out template))
        {
            Log.Error("Cannot restore persistent maps: no selected game map or persistence template.");
            return;
        }

        var primaryKey = _bssWorld.GetPrimarySectorId();
        var primary = mapEntries.FirstOrDefault(entry => entry.MapKey == primaryKey) ?? mapEntries[0];

        // Persistence() does not copy offset/rotation flags; defaults would randomly
        // displace the saved world on every restart.
        var persisted = template!.Persistence(new ResPath(primary.SavePath));
        persisted.MaxRandomOffset = 0f;
        persisted.RandomRotation = false;

        ev.Maps.Clear();
        ev.Maps.Add(persisted);
        _restoredWorld = true;
        Log.Info($"Restoring primary persistent map '{primary.Id}' from {primary.SavePath} (cycle started {GetCycleStart(manifest):u}).");
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        if (!Enabled)
            return;

        LoadExtraPersistedMaps();
        _bssWorld.EnsureSectorMaps();

        _loadedManifest ??= TryReadManifest(out var manifest) ? manifest : null;
        if (_loadedManifest == null)
        {
            RestoreMissingStations();
            return;
        }

        var mapsByKey = CollectMapsByKey();
        var existingGridIds = new HashSet<string>(StringComparer.Ordinal);
        var existingGrids = AllEntityQuery<PersistentGridComponent>();
        while (existingGrids.MoveNext(out _, out var persistentGrid))
        {
            if (!string.IsNullOrEmpty(persistentGrid.Id))
                existingGridIds.Add(persistentGrid.Id);
        }

        var fallbackMap = mapsByKey.Values.FirstOrDefault();
        if (fallbackMap == MapId.Nullspace)
        {
            var anyMap = AllEntityQuery<MapComponent>();
            if (anyMap.MoveNext(out _, out var map))
                fallbackMap = map.MapId;
        }

        foreach (var entry in _loadedManifest.Entries.Where(entry => entry.Kind == PersistenceEntryKind.Grid))
        {
            if (!_persistence.Exists(new ResPath(entry.SavePath)))
                continue;

            if (existingGridIds.Contains(entry.Id))
                continue;

            var targetMap = mapsByKey.GetValueOrDefault(entry.MapKey, fallbackMap);
            if (targetMap == MapId.Nullspace)
            {
                Log.Error($"Unable to restore persistent grid '{entry.Id}': no target map exists.");
                continue;
            }

            if (!_persistence.LoadGrid(
                    targetMap,
                    new ResPath(entry.SavePath),
                    out var loaded,
                    new Vector2(entry.PositionX, entry.PositionY),
                    new Angle(entry.Rotation)))
            {
                Log.Error($"Unable to restore persistent grid '{entry.Id}' from {entry.SavePath}.");
                continue;
            }

            if (loaded is { } grid)
                RestoreStation(grid.Owner, entry.Id);
        }

        RestoreMissingStations();
    }

    private void LoadExtraPersistedMaps()
    {
        if (_loadedManifest == null)
            return;

        var primaryKey = _bssWorld.GetPrimarySectorId();
        foreach (var entry in _loadedManifest.Entries.Where(entry => entry.Kind == PersistenceEntryKind.Map))
        {
            if (entry.MapKey == primaryKey)
                continue;

            var path = new ResPath(entry.SavePath);
            if (!_persistence.Exists(path))
                continue;

            if (!_persistence.LoadMap(path, out var map, out var grids))
            {
                Log.Error($"Unable to restore persistent map '{entry.Id}' from {entry.SavePath}.");
                continue;
            }

            if (map is { } loadedMap)
            {
                var sector = EnsureComp<BssSectorMapComponent>(loadedMap.Owner);
                if (string.IsNullOrEmpty(sector.Sector))
                    sector.Sector = entry.MapKey;

                var persistent = EnsureComp<PersistentMapComponent>(loadedMap.Owner);
                persistent.Id = entry.Id;
            }

            if (grids == null)
                continue;

            foreach (var grid in grids)
            {
                if (TryComp<PersistentGridComponent>(grid.Owner, out var persistentGrid))
                    RestoreStation(grid.Owner, persistentGrid.Id);
            }

            Log.Info($"Restored extra persistent map '{entry.Id}' from {entry.SavePath}.");
        }
    }

    private Dictionary<string, MapId> CollectMapsByKey()
    {
        var mapsByKey = new Dictionary<string, MapId>(StringComparer.Ordinal);
        var mapQuery = AllEntityQuery<PersistentMapComponent, MapComponent>();
        while (mapQuery.MoveNext(out _, out var persistent, out var map))
        {
            mapsByKey[persistent.Id] = map.MapId;
        }

        var sectorQuery = AllEntityQuery<BssSectorMapComponent, MapComponent>();
        while (sectorQuery.MoveNext(out _, out var sector, out var map))
        {
            mapsByKey[sector.Sector] = map.MapId;
        }

        return mapsByKey;
    }

    private void RestoreMissingStations()
    {
        var query = AllEntityQuery<PersistentGridComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var persistent, out _))
        {
            if (TryComp<StationMemberComponent>(uid, out var member) &&
                member.Station.IsValid() &&
                Exists(member.Station))
            {
                continue;
            }

            RestoreStation(uid, persistent.Id);
        }
    }

    private void RestoreStation(EntityUid grid, string persistenceId)
    {
        RemComp<StationMemberComponent>(grid);

        var stationKey = persistenceId;
        if (!_prototypes.TryIndex<GameMapPrototype>(stationKey, out var gameMap))
        {
            var separator = stationKey.LastIndexOf('-');
            if (separator <= 0 || !_prototypes.TryIndex(stationKey[..separator], out gameMap))
                return;

            stationKey = stationKey[..separator];
        }

        if (!gameMap.Stations.TryGetValue(stationKey, out var config))
        {
            if (gameMap.Stations.Count != 1)
                return;

            config = gameMap.Stations.Values.First();
        }

        _station.InitializeNewStation(config, [grid], MetaData(grid).EntityName);
    }

    private void OnBeforeRoundRestart(BeforeRoundRestartPersistenceEvent ev)
    {
        if (!Enabled)
            return;

        if (TryReadManifest(out var stored) && IsCycleExpired(stored))
        {
            Log.Info($"Persistence cycle started {GetCycleStart(stored):u} has expired; wiping world instead of saving.");
            ClearPersistence();
            return;
        }

        // GameTicker.PostInitialize always calls RestartRound before any maps exist.
        // Saving that empty snapshot used to overwrite last week's manifest, so the
        // YAML files stayed on disk but were never loaded on the next start.
        if (!HasPersistentEntities())
        {
            Log.Debug("Skipping persistence save: no persistent maps or grids are loaded.");
            return;
        }

        SaveWorld();
    }

    public bool SaveWorld()
    {
        if (!HasPersistentEntities())
        {
            Log.Debug("Skipping persistence save: no persistent maps or grids are loaded.");
            return true;
        }

        var entries = new List<PersistenceManifestEntry>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var persistentMaps = new Dictionary<MapId, string>();

        var mapQuery = AllEntityQuery<PersistentMapComponent, MapComponent>();
        while (mapQuery.MoveNext(out var uid, out var persistent, out var map))
        {
            if (!TryReserveId(persistent.Id, ids, out var id))
                continue;

            var path = _persistence.GetMapSavePath(id);
            if (!_persistence.SaveMap(uid, path))
            {
                Log.Error($"Failed to persist map '{id}'.");
                continue;
            }

            persistentMaps[map.MapId] = id;
            entries.Add(new PersistenceManifestEntry
            {
                Id = id,
                Kind = PersistenceEntryKind.Map,
                MapKey = id,
                SavePath = path.ToString(),
                SavedAt = DateTimeOffset.UtcNow,
            });
        }

        var gridQuery = AllEntityQuery<PersistentGridComponent, MapGridComponent, TransformComponent>();
        while (gridQuery.MoveNext(out var uid, out var persistent, out _, out var transform))
        {
            if (persistentMaps.ContainsKey(transform.MapID))
                continue; // Already recursively included in a persisted map.

            if (!TryReserveId(persistent.Id, ids, out var id))
                continue;

            var path = _persistence.GetMapSavePath(id);
            if (!_persistence.SaveGrid(uid, path))
            {
                Log.Error($"Failed to persist grid '{id}'.");
                continue;
            }

            entries.Add(new PersistenceManifestEntry
            {
                Id = id,
                Kind = PersistenceEntryKind.Grid,
                MapKey = persistent.MapKey,
                SavePath = path.ToString(),
                PositionX = transform.LocalPosition.X,
                PositionY = transform.LocalPosition.Y,
                Rotation = (float) transform.LocalRotation.Theta,
                SavedAt = DateTimeOffset.UtcNow,
            });
        }

        var manifest = new PersistenceManifest
        {
            Entries = entries,
            CycleStartedAt = ResolveCycleStart(),
        };
        if (!TryWriteManifest(manifest))
            return false;

        _loadedManifest = manifest;
        Log.Info($"Persisted {entries.Count} maps and grids (cycle started {manifest.CycleStartedAt:u}, wipe after {manifest.CycleStartedAt + CycleLength:u}).");
        return true;
    }

    public IReadOnlyList<PersistenceManifestEntry> GetManifestEntries()
    {
        return TryReadManifest(out var manifest) ? manifest.Entries : [];
    }

    public (DateTimeOffset StartedAt, DateTimeOffset WipeAt)? GetCycleInfo()
    {
        if (!TryReadManifest(out var manifest) || manifest.Entries.Count == 0)
            return null;

        var started = GetCycleStart(manifest);
        return (started, started + CycleLength);
    }

    public void ClearPersistence()
    {
        _resources.UserData.Delete(_persistence.MapsPath);
        _resources.UserData.CreateDir(_persistence.MapsPath);
        _loadedManifest = null;
        _restoredWorld = false;
    }

    private bool TryReadManifest(out PersistenceManifest manifest)
    {
        manifest = new PersistenceManifest();
        if (!_resources.UserData.Exists(ManifestPath))
            return false;

        try
        {
            using var stream = _resources.UserData.Open(
                ManifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using var reader = new StreamReader(stream);
            manifest = _deserializer.Deserialize<PersistenceManifest>(reader) ?? new PersistenceManifest();
            return true;
        }
        catch (Exception exception)
        {
            Log.Error($"Failed to read persistence manifest {ManifestPath}: {exception}");
            return false;
        }
    }

    private bool TryWriteManifest(PersistenceManifest manifest)
    {
        var temporary = _persistence.MapsPath / $"{ManifestFileName}.tmp";
        try
        {
            _resources.UserData.CreateDir(_persistence.MapsPath);
            using (var stream = _resources.UserData.Open(
                       temporary,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                _serializer.Serialize(writer, manifest);
            }

            _resources.UserData.Delete(ManifestPath);
            _resources.UserData.Rename(temporary, ManifestPath);
            return true;
        }
        catch (Exception exception)
        {
            Log.Error($"Failed to write persistence manifest {ManifestPath}: {exception}");
            _resources.UserData.Delete(temporary);
            return false;
        }
    }

    private bool TryReserveId(string source, HashSet<string> ids, out string id)
    {
        try
        {
            id = GridPersistenceService.SanitizeId(source);
        }
        catch (ArgumentException exception)
        {
            Log.Error(exception.Message);
            id = string.Empty;
            return false;
        }

        if (ids.Add(id))
            return true;

        Log.Error($"Duplicate persistence id '{id}'; only the first entity will be saved.");
        return false;
    }

    private bool HasPersistentEntities()
    {
        var maps = AllEntityQuery<PersistentMapComponent, MapComponent>();
        if (maps.MoveNext(out _, out _, out _))
            return true;

        var grids = AllEntityQuery<PersistentGridComponent, MapGridComponent>();
        return grids.MoveNext(out _, out _, out _);
    }

    private bool IsCycleExpired(PersistenceManifest manifest)
    {
        if (manifest.Entries.Count == 0)
            return false;

        return DateTimeOffset.UtcNow >= GetCycleStart(manifest) + CycleLength;
    }

    private DateTimeOffset GetCycleStart(PersistenceManifest manifest)
    {
        if (manifest.CycleStartedAt != default)
            return manifest.CycleStartedAt;

        var fromEntries = manifest.Entries
            .Select(entry => entry.SavedAt)
            .Where(stamp => stamp != default)
            .ToList();

        return fromEntries.Count > 0 ? fromEntries.Min() : DateTimeOffset.UtcNow;
    }

    private DateTimeOffset ResolveCycleStart()
    {
        if (_loadedManifest != null && _loadedManifest.CycleStartedAt != default && !IsCycleExpired(_loadedManifest))
            return GetCycleStart(_loadedManifest);

        if (TryReadManifest(out var stored) && stored.Entries.Count > 0 && !IsCycleExpired(stored))
            return GetCycleStart(stored);

        return DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Rebuilds a manifest from leftover map YAML files after an empty snapshot
    /// overwrote the previous manifest (server startup RestartRound).
    /// </summary>
    private bool TryRecoverManifestFromFiles(out PersistenceManifest manifest)
    {
        manifest = new PersistenceManifest();
        if (!_resources.UserData.Exists(_persistence.MapsPath) || !_resources.UserData.IsDir(_persistence.MapsPath))
            return false;

        foreach (var entry in _resources.UserData.DirectoryEntries(_persistence.MapsPath))
        {
            if (!entry.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                continue;

            if (entry.StartsWith("manifest", StringComparison.OrdinalIgnoreCase))
                continue;

            var path = _persistence.MapsPath / entry;
            var id = new ResPath(entry).FilenameWithoutExtension;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            manifest.Entries.Add(new PersistenceManifestEntry
            {
                Id = id,
                Kind = PersistenceEntryKind.Map,
                MapKey = id,
                SavePath = path.ToString(),
                SavedAt = DateTimeOffset.UtcNow,
            });
        }

        if (manifest.Entries.Count == 0)
            return false;

        manifest.CycleStartedAt = DateTimeOffset.UtcNow;
        Log.Warning($"Recovered {manifest.Entries.Count} persistence map files after an empty manifest.");
        TryWriteManifest(manifest);
        return true;
    }
}

public sealed class PersistenceManifest
{
    public int Version { get; set; } = 2;
    public DateTimeOffset CycleStartedAt { get; set; }
    public List<PersistenceManifestEntry> Entries { get; set; } = [];
}

public sealed class PersistenceManifestEntry
{
    public string Id { get; set; } = string.Empty;
    public PersistenceEntryKind Kind { get; set; }
    public string MapKey { get; set; } = "default";
    public string SavePath { get; set; } = string.Empty;
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float Rotation { get; set; }
    public DateTimeOffset SavedAt { get; set; }
}

public enum PersistenceEntryKind : byte
{
    Map,
    Grid,
}
