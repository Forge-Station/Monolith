using Content.Server.Atmos.Components;
using Content.Server.Atmos.Piping.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Maps;
using Prometheus; // Forge-Change
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Atmos.EntitySystems
{
    public sealed partial class AtmosphereSystem
    {
        [Dependency] private readonly IGameTiming _gameTiming = default!;

        private readonly Stopwatch _simulationStopwatch = new();
        // Forge-Change-start
        private static readonly Histogram AtmosPhaseDurationMs = Metrics.CreateHistogram(
            "atmos_phase_duration_ms",
            "Execution time per atmos processing phase (milliseconds).",
            new HistogramConfiguration
            {
                LabelNames = ["phase"],
                Buckets = Histogram.ExponentialBuckets(0.05, 2, 12),
            });

        private static readonly Counter AtmosPhaseProcessedCount = Metrics.CreateCounter(
            "atmos_phase_processed_total",
            "Processed item count per atmos processing phase.",
            new CounterConfiguration
            {
                LabelNames = ["phase"],
            });

        private static readonly Counter AtmosHighPressureLookupEntities = Metrics.CreateCounter(
            "atmos_highpressure_lookup_entities_total",
            "Entities returned by high-pressure tile lookups.");

        private static readonly Counter AtmosHighPressureImpulses = Metrics.CreateCounter(
            "atmos_highpressure_impulses_total",
            "Impulses applied by high-pressure movement.");
        // Forge-Change-end

        /// <summary>
        ///     Check current execution time every n instances processed.
        /// </summary>
        private const int LagCheckIterations = 30;

        /// <summary>
        ///     Check current execution time every n instances processed.
        /// </summary>
        private const int InvalidCoordinatesLagCheckIterations = 50;

        private int _currentRunAtmosphereIndex;
        private bool _simulationPaused;

        // Forge-Change-start
        private static string PhaseMetricLabel(AtmosphereProcessingState phase)
        {
            return phase switch
            {
                AtmosphereProcessingState.Revalidate => "revalidate",
                AtmosphereProcessingState.TileEqualize => "equalize",
                AtmosphereProcessingState.ActiveTiles => "active",
                AtmosphereProcessingState.ExcitedGroups => "excited",
                AtmosphereProcessingState.HighPressureDelta => "highpressure",
                AtmosphereProcessingState.Hotspots => "hotspot",
                AtmosphereProcessingState.Superconductivity => "superconduction",
                AtmosphereProcessingState.PipeNet => "pipenets",
                AtmosphereProcessingState.AtmosDevices => "devices",
                _ => "unknown",
            };
        }

        private static void ObservePhaseMetrics(AtmosphereProcessingState phase, double elapsedMs, int processed)
        {
            var label = PhaseMetricLabel(phase);
            AtmosPhaseDurationMs.WithLabels(label).Observe(elapsedMs);
            if (processed > 0)
                AtmosPhaseProcessedCount.WithLabels(label).Inc(processed);
        }

        private static void BeginRunList<T>(List<T> runList, ref int runIndex, HashSet<T> source)
            where T : notnull
        {
            runList.Clear();
            runList.EnsureCapacity(source.Count);
            foreach (var entry in source)
            {
                runList.Add(entry);
            }

            runIndex = 0;
        }

        private enum ChunkRunType : byte
        {
            Revalidate,
            Active,
            HighPressure,
            Hotspot,
            Superconductivity,
        }

        private void BeginRunChunks(GridAtmosphereComponent atmosphere, EntityUid gridUid, ChunkRunType type)
        {
            atmosphere.CurrentRunChunks.Clear();

            foreach (var (chunkIndex, chunkState) in atmosphere.Chunks)
            {
                var hasWork = type switch
                {
                    ChunkRunType.Revalidate => chunkState.InvalidatedCoords.Count > 0,
                    ChunkRunType.Active => chunkState.ActiveTiles.Count > 0 || chunkState.InvalidatedCoords.Count > 0,
                    ChunkRunType.HighPressure => chunkState.HighPressureTiles.Count > 0,
                    ChunkRunType.Hotspot => chunkState.HotspotTiles.Count > 0,
                    ChunkRunType.Superconductivity => chunkState.SuperconductivityTiles.Count > 0,
                    _ => false,
                };

                if (!hasWork)
                    continue;

                if (type != ChunkRunType.Revalidate && !ShouldProcessChunk(gridUid, atmosphere, chunkIndex, chunkState))
                    continue;

                atmosphere.CurrentRunChunks.Add(chunkIndex);
            }

            atmosphere.CurrentRunChunkIndex = 0;
        }

        private void BeginRunTilesFromChunks(
            GridAtmosphereComponent atmosphere,
            Func<AtmosChunkState, HashSet<TileAtmosphere>> selector)
        {
            atmosphere.CurrentRunTiles.Clear();

            for (var i = 0; i < atmosphere.CurrentRunChunks.Count; i++)
            {
                var chunkIndex = atmosphere.CurrentRunChunks[i];
                if (!TryGetChunkState(atmosphere, chunkIndex, out var chunk) || chunk == null)
                    continue;

                foreach (var tile in selector(chunk))
                {
                    atmosphere.CurrentRunTiles.Add(tile);
                }
            }

            atmosphere.CurrentRunTileIndex = 0;
        }
        // Forge-Change-end
        private TileAtmosphere GetOrNewTile(EntityUid owner, GridAtmosphereComponent atmosphere, Vector2i index, bool invalidateNew = true)
        {
            var tile = atmosphere.Tiles.GetOrNew(index, out var existing);
            if (existing)
                return tile;

            if (invalidateNew)
                AddInvalidatedTile(atmosphere, index);

            tile.GridIndex = owner;
            tile.GridIndices = index;
            TouchChunk(atmosphere, index);
            return tile;
        }

        private readonly List<Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent>> _currentRunAtmosphere = new();

        /// <summary>
        ///     Revalidates all invalid coordinates in a grid atmosphere.
        ///     I.e., process any tiles that have had their airtight blockers modified.
        /// </summary>
        /// <param name="ent">The grid atmosphere in question.</param>
        /// <returns>Whether the process succeeded or got paused due to time constrains.</returns>
        private bool ProcessRevalidate(Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent> ent, out int processed) // Forge-Change
        {
            processed = 0; // Forge-Change
            if (ent.Comp4.MapUid == null)
            {
                Log.Error($"Attempted to process atmosphere on a map-less grid? Grid: {ToPrettyString(ent)}");
                return true;
            }

            var (uid, atmosphere, visuals, grid, xform) = ent;
            var volume = GetVolumeForTiles(grid);
            TryComp(xform.MapUid, out MapAtmosphereComponent? mapAtmos);

            if (!atmosphere.ProcessingPaused)
            {
                BeginRunChunks(atmosphere, uid, ChunkRunType.Revalidate);
                atmosphere.CurrentRunInvalidatedTiles.Clear();
                atmosphere.CurrentRunInvalidatedTiles.EnsureCapacity(atmosphere.InvalidatedCoords.Count);

                for (var i = 0; i < atmosphere.CurrentRunChunks.Count; i++)
                {
                    var chunkIndex = atmosphere.CurrentRunChunks[i];
                    if (!TryGetChunkState(atmosphere, chunkIndex, out var chunk) || chunk == null)
                        continue;

                    foreach (var indices in chunk.InvalidatedCoords)
                    {
                        var tile = GetOrNewTile(uid, atmosphere, indices, invalidateNew: false);
                        atmosphere.CurrentRunInvalidatedTiles.Enqueue(tile);

                        // Update tile.IsSpace and tile.MapAtmosphere, and tile.AirtightData.
                        UpdateTileData(ent, mapAtmos, tile);
                    }

                    chunk.InvalidatedCoords.Clear();
                }

                atmosphere.InvalidatedCoords.Clear();

                if (_simulationStopwatch.Elapsed.TotalMilliseconds >= AtmosMaxProcessTime)
                    return false;
            }

            var number = 0;
            while (atmosphere.CurrentRunInvalidatedTiles.TryDequeue(out var tile))
            {
                DebugTools.Assert(atmosphere.Tiles.GetValueOrDefault(tile.GridIndices) == tile);
                UpdateAdjacentTiles(ent, tile, activate: true);
                UpdateTileAir(ent, tile, volume);
                InvalidateVisuals(ent, tile);
                processed++; // Forge-Change

                if (number++ < InvalidCoordinatesLagCheckIterations)
                    continue;

                number = 0;
                // Process the rest next time.
                if (_simulationStopwatch.Elapsed.TotalMilliseconds >= AtmosMaxProcessTime)
                    return false;
            }

            TrimDisconnectedMapTiles(ent);
            return true;
        }

        /// <summary>
        /// This method queued a tile and all of its neighbours up for processing by <see cref="TrimDisconnectedMapTiles"/>.
        /// </summary>
        public void QueueTileTrim(GridAtmosphereComponent atmos, TileAtmosphere tile)
        {
            if (!tile.TrimQueued)
            {
                tile.TrimQueued = true;
                atmos.PossiblyDisconnectedTiles.Add(tile);
            }

            for (var i = 0; i < Atmospherics.Directions; i++)
            {
                var direction = (AtmosDirection) (1 << i);
                var indices = tile.GridIndices.Offset(direction);
                if (atmos.Tiles.TryGetValue(indices, out var adj)
                    && adj.NoGridTile
                    && !adj.TrimQueued)
                {
                    adj.TrimQueued = true;
                    atmos.PossiblyDisconnectedTiles.Add(adj);
                }
            }
        }

        /// <summary>
        /// Tiles in a <see cref="GridAtmosphereComponent"/> are either grid-tiles, or they they should be are tiles
        /// adjacent to grid-tiles that represent the map's atmosphere. This method trims any map-tiles that are no longer
        /// adjacent to any grid-tiles.
        /// </summary>
        private void TrimDisconnectedMapTiles(
            Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent> ent)
        {
            var atmos = ent.Comp1;

            foreach (var tile in atmos.PossiblyDisconnectedTiles)
            {
                tile.TrimQueued = false;
                if (!tile.NoGridTile)
                    continue;

                var connected = false;
                for (var i = 0; i < Atmospherics.Directions; i++)
                {
                    var indices = tile.GridIndices.Offset((AtmosDirection) (1 << i));
                    if (_map.TryGetTile(ent.Comp3, indices, out var gridTile) && !gridTile.IsEmpty)
                    {
                        connected = true;
                        break;
                    }
                }

                if (!connected)
                {
                    RemoveActiveTile(atmos, tile);
                    if (TryGetChunkState(atmos, GetAtmosChunk(tile.GridIndices), out var chunk) && chunk != null)
                    {
                        chunk.HotspotTiles.Remove(tile);
                        chunk.HighPressureTiles.Remove(tile);
                        chunk.SuperconductivityTiles.Remove(tile);
                    }
                    atmos.HotspotTiles.Remove(tile);
                    atmos.HighPressureDelta.Remove(tile);
                    atmos.SuperconductivityTiles.Remove(tile);
                    atmos.Tiles.Remove(tile.GridIndices);
                }
            }

            atmos.PossiblyDisconnectedTiles.Clear();
        }

        /// <summary>
        /// Checks whether a tile has a corresponding grid-tile, or whether it is a "map" tile. Also checks whether the
        /// tile should be considered "space"
        /// </summary>
        private void UpdateTileData(
            Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent> ent,
            MapAtmosphereComponent? mapAtmos,
            TileAtmosphere tile)
        {
            var idx = tile.GridIndices;
            bool mapAtmosphere;
            if (_map.TryGetTile(ent.Comp3, idx, out var gTile) && !gTile.IsEmpty)
            {
                var contentDef = (ContentTileDefinition) _tileDefinitionManager[gTile.TypeId];
                mapAtmosphere = contentDef.MapAtmosphere;
                tile.ThermalConductivity = contentDef.ThermalConductivity;
                tile.HeatCapacity = contentDef.HeatCapacity;
                tile.NoGridTile = false;
            }
            else
            {
                mapAtmosphere = true;
                tile.ThermalConductivity =  0.5f;
                tile.HeatCapacity = float.PositiveInfinity;

                if (!tile.NoGridTile)
                {
                    tile.NoGridTile = true;

                    // This tile just became a non-grid atmos tile.
                    // It, or one of its neighbours, might now be completely disconnected from the grid.
                    QueueTileTrim(ent.Comp1, tile);
                }
            }

            UpdateAirtightData(ent.Owner, ent.Comp1, ent.Comp3, tile);

            if (mapAtmosphere)
            {
                if (!tile.MapAtmosphere)
                {
                    (tile.Air, tile.Space) = GetDefaultMapAtmosphere(mapAtmos);
                    tile.MapAtmosphere = true;
                    ent.Comp1.MapTiles.Add(tile);
                }

                DebugTools.AssertNotNull(tile.Air);
                DebugTools.Assert(tile.Air?.Immutable ?? false);
                return;
            }

            if (!tile.MapAtmosphere)
                return;

            // Tile used to be exposed to the map's atmosphere, but isn't anymore.
            RemoveMapAtmos(ent.Comp1, tile);
        }

        private void RemoveMapAtmos(GridAtmosphereComponent atmos, TileAtmosphere tile)
        {
            DebugTools.Assert(tile.MapAtmosphere);
            DebugTools.AssertNotNull(tile.Air);
            DebugTools.Assert(tile.Air?.Immutable ?? false);
            tile.MapAtmosphere = false;
            atmos.MapTiles.Remove(tile);
            tile.Air = null;
            tile.AirArchived = null;
            tile.ArchivedCycle = 0;
            tile.LastShare = 0f;
            tile.Space = false;
        }

        /// <summary>
        /// Check whether a grid-tile should have an air mixture, and give it one if it doesn't already have one.
        /// </summary>
        private void UpdateTileAir(
            Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent> ent,
            TileAtmosphere tile,
            float volume)
        {
            if (tile.MapAtmosphere)
            {
                DebugTools.AssertNotNull(tile.Air);
                DebugTools.Assert(tile.Air?.Immutable ?? false);
                return;
            }

            var data = tile.AirtightData;
            var fullyBlocked = data.BlockedDirections == AtmosDirection.All;

            if (fullyBlocked && data.NoAirWhenBlocked)
            {
                if (tile.Air == null)
                    return;

                tile.Air = null;
                tile.AirArchived = null;
                tile.ArchivedCycle = 0;
                tile.LastShare = 0f;
                tile.Hotspot = new Hotspot();
                return;
            }

            if (tile.Air != null)
                return;

            tile.Air = new GasMixture(volume){Temperature = Atmospherics.T20C};

            if (data.FixVacuum)
                GridFixTileVacuum(tile);
        }

        private bool ProcessTileEqualize(Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent> ent, out int processed) // Forge-Change
        {
            processed = 0; // Forge-Change
            var atmosphere = ent.Comp1;
            if (!atmosphere.ProcessingPaused)
            {
                if (AtmosForceFullGridDebug)
                {
                    BeginRunList(atmosphere.CurrentRunTiles, ref atmosphere.CurrentRunTileIndex, atmosphere.ActiveTiles);
                }
                else
                {
                    BeginRunChunks(atmosphere, ent.Owner, ChunkRunType.Active);
                    BeginRunTilesFromChunks(atmosphere, chunk => chunk.ActiveTiles);
                }
            }

            var number = 0;
            while (atmosphere.CurrentRunTileIndex < atmosphere.CurrentRunTiles.Count) // Forge-Change
            {
                var tile = atmosphere.CurrentRunTiles[atmosphere.CurrentRunTileIndex++]; // Forge-Change
                EqualizePressureInZone(ent, tile, atmosphere.UpdateCounter);
                processed++; // Forge-Change

                if (number++ < LagCheckIterations)
                    continue;

                number = 0;
                // Process the rest next time.
                if (_simulationStopwatch.Elapsed.TotalMilliseconds >= AtmosMaxProcessTime)
                {
                    return false;
                }
            }

            return true;
        }

        private bool ProcessActiveTiles(
            Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent> ent, out int processed) // Forge-Change
        {
            processed = 0; // Forge-Change
            var atmosphere = ent.Comp1;
            if(!atmosphere.ProcessingPaused)
            {
                if (AtmosForceFullGridDebug)
                {
                    BeginRunList(atmosphere.CurrentRunTiles, ref atmosphere.CurrentRunTileIndex, atmosphere.ActiveTiles);
                }
                else
                {
                    BeginRunChunks(atmosphere, ent.Owner, ChunkRunType.Active);
                    BeginRunTilesFromChunks(atmosphere, chunk => chunk.ActiveTiles);
                }
            }

            var number = 0;
            while (atmosphere.CurrentRunTileIndex < atmosphere.CurrentRunTiles.Count) // Forge-Change
            {
                var tile = atmosphere.CurrentRunTiles[atmosphere.CurrentRunTileIndex++]; // Forge-Change
                ProcessCell(ent, tile, atmosphere.UpdateCounter);
                processed++; // Forge-Change

                if (number++ < LagCheckIterations)
                    continue;

                number = 0;
                // Process the rest next time.
                if (_simulationStopwatch.Elapsed.TotalMilliseconds >= AtmosMaxProcessTime)
                {
                    return false;
                }
            }

            return true;
        }

        private bool ProcessExcitedGroups(
            Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent> ent, out int processed) // Forge-Change
        {
            processed = 0; // Forge-Change
            var gridAtmosphere = ent.Comp1;
            if (!gridAtmosphere.ProcessingPaused)
            {
                gridAtmosphere.CurrentRunExcitedGroups.Clear();
                gridAtmosphere.CurrentRunExcitedGroups.EnsureCapacity(gridAtmosphere.ExcitedGroups.Count);
                foreach (var group in gridAtmosphere.ExcitedGroups)
                {
                    gridAtmosphere.CurrentRunExcitedGroups.Add(group); // Forge-Change
                }
                gridAtmosphere.CurrentRunExcitedGroupIndex = 0; // Forge-Change
            }

            var number = 0;
            while (gridAtmosphere.CurrentRunExcitedGroupIndex < gridAtmosphere.CurrentRunExcitedGroups.Count) // Forge-Change
            {
                var excitedGroup = gridAtmosphere.CurrentRunExcitedGroups[gridAtmosphere.CurrentRunExcitedGroupIndex++]; // Forge-Change
                excitedGroup.BreakdownCooldown++;
                excitedGroup.DismantleCooldown++;
                processed++; // Forge-Change

                if (excitedGroup.BreakdownCooldown > Atmospherics.ExcitedGroupBreakdownCycles)
                    ExcitedGroupSelfBreakdown(ent, excitedGroup);
                else if (excitedGroup.DismantleCooldown > Atmospherics.ExcitedGroupsDismantleCycles)
                    DeactivateGroupTiles(gridAtmosphere, excitedGroup);
                // TODO ATMOS. What is the point of this? why is this only de-exciting the group? Shouldn't it also dismantle it?

                if (number++ < LagCheckIterations)
                    continue;

                number = 0;
                // Process the rest next time.
                if (_simulationStopwatch.Elapsed.TotalMilliseconds >= AtmosMaxProcessTime)
                {
                    return false;
                }
            }

            return true;
        }

        private bool ProcessHighPressureDelta(Entity<GridAtmosphereComponent> ent, out int processed) // Forge-Change
        {
            processed = 0; // Forge-Change
            var atmosphere = ent.Comp;
            if (!atmosphere.ProcessingPaused)
            {
                if (AtmosForceFullGridDebug)
                {
                    BeginRunList(atmosphere.CurrentRunTiles, ref atmosphere.CurrentRunTileIndex, atmosphere.HighPressureDelta);
                }
                else
                {
                    BeginRunChunks(atmosphere, ent.Owner, ChunkRunType.HighPressure);
                    BeginRunTilesFromChunks(atmosphere, chunk => chunk.HighPressureTiles);
                }
            }

            // Note: This is still processed even if space wind is turned off since this handles playing the sounds.

            var number = 0;
            while (atmosphere.CurrentRunTileIndex < atmosphere.CurrentRunTiles.Count) // Forge-Change
            {
                var tile = atmosphere.CurrentRunTiles[atmosphere.CurrentRunTileIndex++]; // Forge-Change
                var (lookups, impulses) = HighPressureMovements(ent, tile, _physicsQuery, _xformQuery, _movedByPressureQuery, _metaQuery); // Forge-Change
                if (lookups > 0) // Forge-Change
                    AtmosHighPressureLookupEntities.Inc(lookups); // Forge-Change
                if (impulses > 0) // Forge-Change
                    AtmosHighPressureImpulses.Inc(impulses); // Forge-Change

                tile.PressureDifference = 0f;
                tile.LastPressureDirection = tile.PressureDirection;
                tile.PressureDirection = AtmosDirection.Invalid;
                tile.PressureSpecificTarget = null;
                atmosphere.HighPressureDelta.Remove(tile);
                if (TryGetChunkState(atmosphere, GetAtmosChunk(tile.GridIndices), out var chunk) && chunk != null)
                    chunk.HighPressureTiles.Remove(tile);
                processed++; // Forge-Change

                if (number++ < LagCheckIterations)
                    continue;
                number = 0;
                // Process the rest next time.
                if (_simulationStopwatch.Elapsed.TotalMilliseconds >= AtmosMaxProcessTime)
                {
                    return false;
                }
            }

            return true;
        }

        private bool ProcessHotspots(
            Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent> ent, out int processed) // Forge-Change
        {
            processed = 0; // Forge-Change
            var atmosphere = ent.Comp1;
            if(!atmosphere.ProcessingPaused)
            {
                if (AtmosForceFullGridDebug)
                {
                    BeginRunList(atmosphere.CurrentRunTiles, ref atmosphere.CurrentRunTileIndex, atmosphere.HotspotTiles);
                }
                else
                {
                    BeginRunChunks(atmosphere, ent.Owner, ChunkRunType.Hotspot);
                    BeginRunTilesFromChunks(atmosphere, chunk => chunk.HotspotTiles);
                }
            }

            var number = 0;
            while (atmosphere.CurrentRunTileIndex < atmosphere.CurrentRunTiles.Count) // Forge-Change
            {
                var hotspot = atmosphere.CurrentRunTiles[atmosphere.CurrentRunTileIndex++]; // Forge-Change
                ProcessHotspot(ent, hotspot);
                processed++; // Forge-Change

                if (number++ < LagCheckIterations)
                    continue;

                number = 0;
                // Process the rest next time.
                if (_simulationStopwatch.Elapsed.TotalMilliseconds >= AtmosMaxProcessTime)
                {
                    return false;
                }
            }

            return true;
        }

        private bool ProcessSuperconductivity(Entity<GridAtmosphereComponent> ent, out int processed) // Forge-Change
        {
            processed = 0; // Forge-Change
            var atmosphere = ent.Comp;
            if(!atmosphere.ProcessingPaused)
            {
                if (AtmosForceFullGridDebug)
                {
                    BeginRunList(atmosphere.CurrentRunTiles, ref atmosphere.CurrentRunTileIndex, atmosphere.SuperconductivityTiles);
                }
                else
                {
                    BeginRunChunks(atmosphere, ent.Owner, ChunkRunType.Superconductivity);
                    BeginRunTilesFromChunks(atmosphere, chunk => chunk.SuperconductivityTiles);
                }
            }

            var number = 0;
            while (atmosphere.CurrentRunTileIndex < atmosphere.CurrentRunTiles.Count) // Forge-Change
            {
                var superconductivity = atmosphere.CurrentRunTiles[atmosphere.CurrentRunTileIndex++]; // Forge-Change
                Superconduct(atmosphere, superconductivity);
                processed++; // Forge-Change

                if (number++ < LagCheckIterations)
                    continue;

                number = 0;
                // Process the rest next time.
                if (_simulationStopwatch.Elapsed.TotalMilliseconds >= AtmosMaxProcessTime)
                {
                    return false;
                }
            }

            return true;
        }

        private bool ProcessPipeNets(GridAtmosphereComponent atmosphere, out int processed) // Forge-Change
        {
            processed = 0; // Forge-Change
            if (!atmosphere.ProcessingPaused)
            {
                atmosphere.CurrentRunPipeNet.Clear();
                atmosphere.CurrentRunPipeNet.EnsureCapacity(atmosphere.PipeNets.Count);
                foreach (var net in atmosphere.PipeNets)
                {
                    atmosphere.CurrentRunPipeNet.Add(net); // Forge-Change
                }
                atmosphere.CurrentRunPipeNetIndex = 0; // Forge-Change
            }

            var number = 0;
            while (atmosphere.CurrentRunPipeNetIndex < atmosphere.CurrentRunPipeNet.Count) // Forge-Change
            {
                var pipenet = atmosphere.CurrentRunPipeNet[atmosphere.CurrentRunPipeNetIndex++]; // Forge-Change
                pipenet.Update();
                processed++; // Forge-Change

                if (number++ < LagCheckIterations)
                    continue;

                number = 0;
                // Process the rest next time.
                if (_simulationStopwatch.Elapsed.TotalMilliseconds >= AtmosMaxProcessTime)
                {
                    return false;
                }
            }

            return true;
        }

        /**
         * UpdateProcessing() takes a different number of calls to go through all of atmos
         * processing depending on what options are enabled. This returns the actual effective time
         * between atmos updates that devices actually experience.
         */
        public float RealAtmosTime()
        {
            int num = (int)AtmosphereProcessingState.NumStates;
            if (!MonstermosEqualization)
                num--;
            if (!ExcitedGroups)
                num--;
            if (!Superconduction)
                num--;
            return num * AtmosTime;
        }

        private bool ProcessAtmosDevices(
            Entity<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent> ent,
            Entity<MapAtmosphereComponent?> map,
            out int processed) // Forge-Change
        {
            processed = 0; // Forge-Change
            var atmosphere = ent.Comp1;
            if (!atmosphere.ProcessingPaused)
            {
                atmosphere.CurrentRunAtmosDevices.Clear();
                atmosphere.CurrentRunAtmosDevices.EnsureCapacity(atmosphere.AtmosDevices.Count);
                foreach (var device in atmosphere.AtmosDevices)
                {
                    atmosphere.CurrentRunAtmosDevices.Add(device); // Forge-Change
                }
                atmosphere.CurrentRunAtmosDeviceIndex = 0; // Forge-Change
            }

            var time = _gameTiming.CurTime;
            var number = 0;
            var ev = new AtmosDeviceUpdateEvent(RealAtmosTime(), (ent, ent.Comp1, ent.Comp2), map);
            while (atmosphere.CurrentRunAtmosDeviceIndex < atmosphere.CurrentRunAtmosDevices.Count) // Forge-Change
            {
                var device = atmosphere.CurrentRunAtmosDevices[atmosphere.CurrentRunAtmosDeviceIndex++]; // Forge-Change
                RaiseLocalEvent(device, ref ev);
                device.Comp.LastProcess = time;
                processed++; // Forge-Change

                if (number++ < LagCheckIterations)
                    continue;

                number = 0;
                // Process the rest next time.
                if (_simulationStopwatch.Elapsed.TotalMilliseconds >= AtmosMaxProcessTime)
                {
                    return false;
                }
            }

            return true;
        }

        private void UpdateProcessing(float frameTime)
        {
            _simulationStopwatch.Restart();

            if (!_simulationPaused)
            {
                RefreshInterestChunks();
                _currentRunAtmosphereIndex = 0;
                _currentRunAtmosphere.Clear();

                var query = EntityQueryEnumerator<GridAtmosphereComponent, GasTileOverlayComponent, MapGridComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var atmos, out var overlay, out var grid, out var xform ))
                {
                    _currentRunAtmosphere.Add((uid, atmos, overlay, grid, xform));
                }
            }

            // We set this to true just in case we have to stop processing due to time constraints.
            _simulationPaused = true;

            for (; _currentRunAtmosphereIndex < _currentRunAtmosphere.Count; _currentRunAtmosphereIndex++)
            {
                var ent = _currentRunAtmosphere[_currentRunAtmosphereIndex];
                var (owner, atmosphere, visuals, grid, xform) = ent;

                if (xform.MapUid == null
                    || TerminatingOrDeleted(xform.MapUid.Value)
                    || xform.MapID == MapId.Nullspace)
                {
                    Log.Error($"Attempted to process atmos without a map? Entity: {ToPrettyString(owner)}. Map: {ToPrettyString(xform?.MapUid)}. MapId: {xform?.MapID}");
                    continue;
                }

                if (atmosphere.LifeStage >= ComponentLifeStage.Stopping || Paused(owner) || !atmosphere.Simulated)
                    continue;

                atmosphere.Timer += frameTime;

                if (atmosphere.Timer < AtmosTime)
                    continue;

                // We subtract it so it takes lost time into account.
                atmosphere.Timer -= AtmosTime;

                var map = new Entity<MapAtmosphereComponent?>(xform.MapUid.Value, _mapAtmosQuery.CompOrNull(xform.MapUid.Value));

                switch (atmosphere.State)
                {
                    case AtmosphereProcessingState.Revalidate:
                    {
                        var phaseStart = _simulationStopwatch.Elapsed.TotalMilliseconds; // Forge-Change
                        var success = ProcessRevalidate(ent, out var processed); // Forge-Change
                        ObservePhaseMetrics(AtmosphereProcessingState.Revalidate, _simulationStopwatch.Elapsed.TotalMilliseconds - phaseStart, processed); // Forge-Change
                        if (!success) // Forge-Change
                        {
                            atmosphere.ProcessingPaused = true;
                            return;
                        }

                        atmosphere.ProcessingPaused = false;

                        // Next state depends on whether monstermos equalization is enabled or not.
                        // Note: We do this here instead of on the tile equalization step to prevent ending it early.
                        //       Therefore, a change to this CVar might only be applied after that step is over.
                        atmosphere.State = MonstermosEqualization
                            ? AtmosphereProcessingState.TileEqualize
                            : AtmosphereProcessingState.ActiveTiles;
                        continue;
                    }
                    case AtmosphereProcessingState.TileEqualize:
                    {
                        var phaseStart = _simulationStopwatch.Elapsed.TotalMilliseconds; // Forge-Change
                        var success = ProcessTileEqualize(ent, out var processed); // Forge-Change
                        ObservePhaseMetrics(AtmosphereProcessingState.TileEqualize, _simulationStopwatch.Elapsed.TotalMilliseconds - phaseStart, processed); // Forge-Change
                        if (!success) // Forge-Change
                        {
                            atmosphere.ProcessingPaused = true;
                            return;
                        }

                        atmosphere.ProcessingPaused = false;
                        atmosphere.State = AtmosphereProcessingState.ActiveTiles;
                        continue;
                    }
                    case AtmosphereProcessingState.ActiveTiles:
                    {
                        var phaseStart = _simulationStopwatch.Elapsed.TotalMilliseconds; // Forge-Change
                        var success = ProcessActiveTiles(ent, out var processed); // Forge-Change
                        ObservePhaseMetrics(AtmosphereProcessingState.ActiveTiles, _simulationStopwatch.Elapsed.TotalMilliseconds - phaseStart, processed); // Forge-Change
                        if (!success) // Forge-Change
                        {
                            atmosphere.ProcessingPaused = true;
                            return;
                        }

                        atmosphere.ProcessingPaused = false;
                        // Next state depends on whether excited groups are enabled or not.
                        atmosphere.State = ExcitedGroups ? AtmosphereProcessingState.ExcitedGroups : AtmosphereProcessingState.HighPressureDelta;
                        continue;
                    }
                    case AtmosphereProcessingState.ExcitedGroups:
                    {
                        var phaseStart = _simulationStopwatch.Elapsed.TotalMilliseconds; // Forge-Change
                        var success = ProcessExcitedGroups(ent, out var processed); // Forge-Change
                        ObservePhaseMetrics(AtmosphereProcessingState.ExcitedGroups, _simulationStopwatch.Elapsed.TotalMilliseconds - phaseStart, processed); // Forge-Change
                        if (!success) // Forge-Change
                        {
                            atmosphere.ProcessingPaused = true;
                            return;
                        }

                        atmosphere.ProcessingPaused = false;
                        atmosphere.State = AtmosphereProcessingState.HighPressureDelta;
                        continue;
                    }
                    case AtmosphereProcessingState.HighPressureDelta:
                    {
                        var phaseStart = _simulationStopwatch.Elapsed.TotalMilliseconds; // Forge-Change
                        var success = ProcessHighPressureDelta((ent, ent), out var processed); // Forge-Change
                        ObservePhaseMetrics(AtmosphereProcessingState.HighPressureDelta, _simulationStopwatch.Elapsed.TotalMilliseconds - phaseStart, processed); // Forge-Change
                        if (!success) // Forge-Change
                        {
                            atmosphere.ProcessingPaused = true;
                            return;
                        }

                        atmosphere.ProcessingPaused = false;
                        atmosphere.State = AtmosphereProcessingState.Hotspots;
                        continue;
                    }
                    case AtmosphereProcessingState.Hotspots:
                    {
                        var phaseStart = _simulationStopwatch.Elapsed.TotalMilliseconds; // Forge-Change
                        var success = ProcessHotspots(ent, out var processed); // Forge-Change
                        ObservePhaseMetrics(AtmosphereProcessingState.Hotspots, _simulationStopwatch.Elapsed.TotalMilliseconds - phaseStart, processed); // Forge-Change
                        if (!success) // Forge-Change
                        {
                            atmosphere.ProcessingPaused = true;
                            return;
                        }

                        atmosphere.ProcessingPaused = false;
                        // Next state depends on whether superconduction is enabled or not.
                        // Note: We do this here instead of on the tile equalization step to prevent ending it early.
                        //       Therefore, a change to this CVar might only be applied after that step is over.
                        atmosphere.State = Superconduction
                            ? AtmosphereProcessingState.Superconductivity
                            : AtmosphereProcessingState.PipeNet;
                        continue;
                    }
                    case AtmosphereProcessingState.Superconductivity:
                    {
                        var phaseStart = _simulationStopwatch.Elapsed.TotalMilliseconds; // Forge-Change
                        var success = ProcessSuperconductivity((ent, ent), out var processed); // Forge-Change
                        ObservePhaseMetrics(AtmosphereProcessingState.Superconductivity, _simulationStopwatch.Elapsed.TotalMilliseconds - phaseStart, processed); // Forge-Change
                        if (!success) // Forge-Change
                        {
                            atmosphere.ProcessingPaused = true;
                            return;
                        }

                        atmosphere.ProcessingPaused = false;
                        atmosphere.State = AtmosphereProcessingState.PipeNet;
                        continue;
                    }
                    case AtmosphereProcessingState.PipeNet:
                    {
                        var phaseStart = _simulationStopwatch.Elapsed.TotalMilliseconds; // Forge-Change
                        var success = ProcessPipeNets(atmosphere, out var processed); // Forge-Change
                        ObservePhaseMetrics(AtmosphereProcessingState.PipeNet, _simulationStopwatch.Elapsed.TotalMilliseconds - phaseStart, processed); // Forge-Change
                        if (!success) // Forge-Change
                        {
                            atmosphere.ProcessingPaused = true;
                            return;
                        }

                        atmosphere.ProcessingPaused = false;
                        atmosphere.State = AtmosphereProcessingState.AtmosDevices;
                        continue;
                    }
                    case AtmosphereProcessingState.AtmosDevices:
                    {
                        var phaseStart = _simulationStopwatch.Elapsed.TotalMilliseconds; // Forge-Change
                        var success = ProcessAtmosDevices(ent, map, out var processed); // Forge-Change
                        ObservePhaseMetrics(AtmosphereProcessingState.AtmosDevices, _simulationStopwatch.Elapsed.TotalMilliseconds - phaseStart, processed); // Forge-Change
                        if (!success) // Forge-Change
                        {
                            atmosphere.ProcessingPaused = true;
                            return;
                        }

                        atmosphere.ProcessingPaused = false;
                        atmosphere.State = AtmosphereProcessingState.Revalidate;

                        // We reached the end of this atmosphere's update tick. Break out of the switch.
                        break;
                    }
                }

                // And increase the update counter.
                atmosphere.UpdateCounter++;
            }

            // We finished processing all atmospheres successfully, therefore we won't be paused next tick.
            _simulationPaused = false;
        }
    }

    public enum AtmosphereProcessingState : byte
    {
        Revalidate,
        TileEqualize,
        ActiveTiles,
        ExcitedGroups,
        HighPressureDelta,
        Hotspots,
        Superconductivity,
        PipeNet,
        AtmosDevices,
        NumStates
    }
}
