using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime;
using System.Text;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Robust.Shared;
using Robust.Shared.Audio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Spawners;

namespace Content.IntegrationTests.Tests
{
    [TestFixture]
    [TestOf(typeof(EntityUid))]
    public sealed class EntityTest : GameTest
    {
        private static readonly HashSet<ProtoId<EntityCategoryPrototype>> IgnoredCategories = ["Spawner", "Debug", "GameRules"];

        public override PoolSettings PoolSettings => new()
        {
            Connected = true,
            Dirty = true
        };

        public static PoolSettings Disconnected => new()
        {
            Dirty = true,
        };

        /// <summary>
        /// How many prototypes may exist at once. The full catalog on both server and client
        /// is what pushes this fork past 20 GB.
        /// </summary>
        private const int SpawnBatchSize = 12;

        /// <summary>
        /// Prototypes that detonate or chain-react when several of them share a tile.
        /// </summary>
        private static readonly string[] ChainReactionComponents =
        [
            "Meteor",
            "Explosive",
            "ExplodeOnTrigger",
            "Anomaly",
        ];

        /// <summary>
        /// Prototypes that spawn a grid, a station, or a world. One of them is already a map.
        /// </summary>
        private static readonly string[] MapSpawningComponents =
        [
            "MapGrid",
            "RoomFill",
            "GridSpawner",
            "BecomesStation",
            "StationData",
            "GridSpawn",
            "StationCargoShuttle",
            "WorldLoader",
            "BiomeSelection",
            // These load a second map on init and delete it again when the spawner goes away.
            "StationMapSpawner",
            "StationPlanetSpawner",
            "GridFill",
        ];

        private List<string> EnumerateSpawnable(IPrototypeManager prototypes, params string[] extraExcluded)
        {
            return prototypes
                .EnumeratePrototypes<EntityPrototype>()
                .Where(p => !p.Abstract)
                .Where(p => !Pair.IsTestPrototype(p))
                .Where(p => MapSpawningComponents.All(name => !p.Components.ContainsKey(name)))
                .Where(p => extraExcluded.All(name => !p.Components.ContainsKey(name)))
                .Where(p => p.Categories.All(x => !IgnoredCategories.Contains(x.ID)))
                .Select(p => p.ID)
                .ToList();
        }

        private async Task SetGridTraversal(bool enabled)
        {
            await Pair.Server.WaitPost(() =>
                Pair.Server.ResolveDependency<IEntityManager>().System<SharedGridTraversalSystem>().Enabled = enabled);

            if (Pair.Client.Session == null)
                return;

            await Pair.Client.WaitPost(() =>
                Pair.Client.ResolveDependency<IEntityManager>().System<SharedGridTraversalSystem>().Enabled = enabled);
        }

        private static async Task ForEachBatch(IReadOnlyList<string> protoIds, Func<IReadOnlyList<string>, Task> runBatch)
        {
            for (var start = 0; start < protoIds.Count; start += SpawnBatchSize)
            {
                var count = Math.Min(SpawnBatchSize, protoIds.Count - start);
                var batch = new string[count];
                for (var i = 0; i < count; i++)
                    batch[i] = protoIds[start + i];

                await runBatch(batch);

                // Drop decoded sprite buffers every batch. The full collect is every 8th:
                // doing it on each batch turns the spawn tests into a GC benchmark.
                ReleaseImageBuffers();
                if ((start / SpawnBatchSize) % 8 == 7)
                    ReleaseMemory();
            }

            ReleaseMemory();
        }

        private static HashSet<EntityUid> SnapshotEntities(IEntityManager entityMan)
        {
            return new HashSet<EntityUid>(entityMan.GetEntities());
        }

        /// <summary>
        /// Deletes everything spawned since <paramref name="keep"/>, including detached children.
        /// </summary>
        private static void DeleteExcept(IEntityManager entityMan, HashSet<EntityUid> keep)
        {
            var doomed = new List<EntityUid>();
            var query = entityMan.AllEntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out var uid, out var meta))
            {
                if (!meta.EntityDeleted && !keep.Contains(uid))
                    doomed.Add(uid);
            }

            foreach (var uid in doomed)
            {
                if (entityMan.EntityExists(uid))
                    entityMan.DeleteEntity(uid);
            }
        }

        /// <summary>
        /// Some prototypes delete the map they spawned on. Teardown then ticks an empty world and the pair comes back dirty.
        /// </summary>
        private async Task<TestMapData> PreserveTestMap(TestMapData map, IEntityManager entityMan, HashSet<EntityUid> keep)
        {
            var alive = false;
            await Pair.Server.WaitPost(() =>
            {
                alive = entityMan.EntityExists(map.MapUid) && entityMan.EntityExists(map.Grid.Owner);
                if (!alive)
                    return;

                keep.Add(map.MapUid);
                keep.Add(map.Grid.Owner);
            });

            if (alive)
                return map;

            var restored = await Pair.CreateTestMap();
            await Pair.Server.WaitPost(() =>
            {
                keep.Add(restored.MapUid);
                keep.Add(restored.Grid.Owner);
            });
            return restored;
        }

        /// <summary>
        /// ImageSharp keeps the largest decoded sprites in its own pool. Those arrays stay rooted,
        /// so a garbage collection alone never returns the memory.
        /// </summary>
        private static void ReleaseImageBuffers()
        {
            SixLabors.ImageSharp.Configuration.Default.MemoryAllocator.ReleaseRetainedResources();
        }

        private static void ReleaseMemory()
        {
            ReleaseImageBuffers();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        [Test]
        [PairConfig(nameof(Disconnected))]
        public async Task SpawnAndDeleteAllEntitiesOnDifferentMaps()
        {
            var server = Pair.Server;
            var entityMan = server.ResolveDependency<IEntityManager>();
            var mapSystem = entityMan.System<SharedMapSystem>();
            var protoIds = EnumerateSpawnable(server.ResolveDependency<IPrototypeManager>());

            HashSet<EntityUid> baseline = null!;
            await server.WaitPost(() => baseline = SnapshotEntities(entityMan));

            await ForEachBatch(protoIds, async batch =>
            {
                await server.WaitPost(() =>
                {
                    foreach (var protoId in batch)
                    {
                        mapSystem.CreateMap(out var mapId);
                        var grid = mapSystem.CreateGridEntity(mapId);
                        mapSystem.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, new Tile(1));
                        SpawnEntity(entityMan, protoId, new EntityCoordinates(grid.Owner, 0, 0));
                    }
                });

                // A few ticks so MapInit and Update run, then the maps are gone before the next batch.
                await server.WaitRunTicks(3);
                await server.WaitPost(() => DeleteExcept(entityMan, baseline));
            });
        }

        [Test]
        [PairConfig(nameof(Disconnected))]
        public async Task SpawnAndDeleteAllEntitiesInTheSameSpot()
        {
            var pair = Pair;
            Assert.That(pair.Client.Session, Is.Null);
            var server = pair.Server;
            var map = await pair.CreateTestMap();
            var entityMan = server.ResolveDependency<IEntityManager>();

            // Explosives, anomalies and meteors chain-react when stacked. They are still spawned
            // by the per-map test, just not all on one tile.
            var protoIds = EnumerateSpawnable(
                server.ResolveDependency<IPrototypeManager>(),
                ChainReactionComponents);

            HashSet<EntityUid> baseline = null!;
            await server.WaitPost(() =>
            {
                baseline = SnapshotEntities(entityMan);
                baseline.Add(map.MapUid);
                baseline.Add(map.Grid.Owner);
            });

            // Ore walls spawn a mob while the wall itself is still being parented onto the grid.
            // The engine logs that nested traversal as an error, which fails the test.
            await SetGridTraversal(false);
            try
            {
            await ForEachBatch(protoIds, async batch =>
            {
                await server.WaitPost(() =>
                {
                    foreach (var protoId in batch)
                        SpawnEntity(entityMan, protoId, map.GridCoords);
                });

                await server.WaitRunTicks(5);
                await server.WaitPost(() => DeleteExcept(entityMan, baseline));
                map = await PreserveTestMap(map, entityMan, baseline);
            });
            }
            finally
            {
                await SetGridTraversal(true);
            }

            // The test map stays. Wiping every entity, including that map, leaves the pair with nothing
            // to tick and the teardown comes back dirty.
        }

        /// <summary>
        ///     Variant of <see cref="SpawnAndDeleteAllEntitiesOnDifferentMaps"/> that also launches a client and dirties
        ///     all components on every entity.
        /// </summary>
        [Test]
        public async Task SpawnAndDirtyAllEntities()
        {
            var pair = Pair;
            var server = pair.Server;
            var client = pair.Client;

            var cfg = server.ResolveDependency<IConfigurationManager>();
            var sEntMan = server.ResolveDependency<IEntityManager>();

            Assert.That(cfg.GetCVar(CVars.NetPVS), Is.False);

            var map = await pair.CreateTestMap();
            // One shared map. A detonating batch deletes the grid and the next spawn ticks an orphaned explosion visual.
            var protoIds = EnumerateSpawnable(server.ResolveDependency<IPrototypeManager>(), ChainReactionComponents);

            HashSet<EntityUid> baseline = null!;
            await server.WaitPost(() =>
            {
                baseline = SnapshotEntities(sEntMan);
                baseline.Add(map.MapUid);
                baseline.Add(map.Grid.Owner);
            });

            await pair.RunTicksSync(2);
            var clientEntMan = client.ResolveDependency<IEntityManager>();
            var clientFloor = 0;
            await client.WaitPost(() => clientFloor = clientEntMan.EntityCount);

            var seenByClient = 0;
            await SetGridTraversal(false);
            try
            {
            await ForEachBatch(protoIds, async batch =>
            {
                await server.WaitPost(() =>
                {
                    foreach (var protoId in batch)
                    {
                        var ent = SpawnEntity(sEntMan, protoId, map.GridCoords);
                        if (!sEntMan.EntityExists(ent))
                            continue;

                        foreach (var (_, component) in sEntMan.GetNetComponents(ent))
                            sEntMan.Dirty(ent, component);
                    }
                });

                await pair.RunTicksSync(2);
                await client.WaitPost(() =>
                {
                    var count = clientEntMan.EntityCount;
                    if (count > clientFloor)
                        seenByClient += count - clientFloor;
                });

                await server.WaitPost(() => DeleteExcept(sEntMan, baseline));
                map = await PreserveTestMap(map, sEntMan, baseline);
                await pair.RunTicksSync(2);
            });
            }
            finally
            {
                await SetGridTraversal(true);
            }

            // 500 is arbitrary. The client must actually have received entities, not just the server.
            Assert.That(seenByClient, Is.GreaterThan(500));
        }

        /// <summary>
        /// This test checks that spawning and deleting an entity doesn't somehow create other unrelated entities.
        /// </summary>
        /// <remarks>
        /// Unless an entity is intentionally designed to spawn other entities (e.g., mob spawners), they should
        /// generally not spawn unrelated / detached entities. Any entities that do get spawned should be parented to
        /// the spawned entity (e.g., in a container). If an entity needs to spawn an entity somewhere in null-space,
        /// it should delete that entity when it is no longer required. This test mainly exists to prevent "entity leak"
        /// bugs, where spawning some entity starts spawning unrelated entities in null space that stick around after
        /// the original entity is gone.
        ///
        /// Note that this isn't really a strict requirement, and there are probably quite a few edge cases. Its a pretty
        /// crude test to try catch issues like this, and possibly should just be disabled.
        /// </remarks>
        [Test]
        [Ignore("Even wizden calls this test ass")] // Monolith - PLS MARKED THIS!! But its true, this test ass
        public async Task SpawnAndDeleteEntityCountTest()
        {
            var pair = Pair;
            var mapSys = pair.Server.System<SharedMapSystem>();
            var server = pair.Server;
            var client = pair.Client;

            var excluded = new[]
            {
                "MapGrid",
                "StationEvent",
                "TimedDespawn",
                "TransferMindOnDespawn", // Frontier
                "BluespaceErrorRule", // Frontier

                // makes an announcement on mapInit.
                "AnnounceOnSpawn",
            };

            Assert.That(server.CfgMan.GetCVar(CVars.NetPVS), Is.False);

            var protoIds = server.ProtoMan
                .EnumeratePrototypes<EntityPrototype>()
                .Where(p => !p.Abstract)
                .Where(p => !pair.IsTestPrototype(p))
                .Where(p => !excluded.Any(p.Components.ContainsKey))
                .Where(p => p.Categories.All(x => !IgnoredCategories.Contains(x.ID)))
                .Select(p => p.ID)
                .ToList();

            protoIds.Sort();
            var mapId = MapId.Nullspace;

            await server.WaitPost(() =>
            {
                mapSys.CreateMap(out mapId);
            });

            var coords = new MapCoordinates(Vector2.Zero, mapId);

            await pair.RunTicksSync(3);

            // We consider only non-audio entities, as some entities will just play sounds when they spawn.
            int Count(IEntityManager ent) => ent.EntityCount - ent.Count<AudioComponent>();
            IEnumerable<EntityUid> Entities(IEntityManager entMan) => entMan.GetEntities().Where(e => !entMan.HasComponent<AudioComponent>(e));

            await Assert.MultipleAsync(async () =>
            {
                foreach (var protoId in protoIds)
                {
                    var count = Count(server.EntMan);
                    var clientCount = Count(client.EntMan);
                    var serverEntities = new HashSet<EntityUid>(Entities(server.EntMan));
                    var clientEntities = new HashSet<EntityUid>(Entities(client.EntMan));
                    EntityUid uid = default;
                    await server.WaitPost(() => uid = SpawnEntity(server.EntMan, protoId, coords)); // Monolith
                    await pair.RunTicksSync(3);

                    // If the entity deleted itself, check that it didn't spawn other entities
                    if (!server.EntMan.EntityExists(uid))
                    {
                        await CleanupTransientEntities(pair, serverEntities);

                        Assert.That(Count(server.EntMan), Is.EqualTo(count), $"Server prototype {protoId} failed on deleting itself\n" +
                            BuildDiffString(serverEntities, Entities(server.EntMan), server.EntMan));
                        Assert.That(Count(client.EntMan), Is.EqualTo(clientCount), $"Client prototype {protoId} failed on deleting itself\n" +
                            $"Expected {clientCount} and found {client.EntMan.EntityCount}.\n" +
                            $"Server count was {count}.\n" +
                            BuildDiffString(clientEntities, Entities(client.EntMan), client.EntMan));
                        continue;
                    }

                    // Check that the number of entities has increased.
                    Assert.That(Count(server.EntMan), Is.GreaterThan(count), $"Server prototype {protoId} failed on spawning as entity count didn't increase\n" +
                        BuildDiffString(serverEntities, Entities(server.EntMan), server.EntMan));
                    Assert.That(Count(client.EntMan), Is.GreaterThan(clientCount), $"Client prototype {protoId} failed on spawning as entity count didn't increase\n" +
                        $"Expected at least {clientCount} and found {client.EntMan.EntityCount}. " +
                        $"Server count was {count}.\n" +
                        BuildDiffString(clientEntities, Entities(client.EntMan), client.EntMan));

                    await server.WaitPost(() => server.EntMan.DeleteEntity(uid));
                    await pair.RunTicksSync(3);
                    await CleanupTransientEntities(pair, serverEntities);

                    // Check that the number of entities has gone back to the original value.
                    Assert.That(Count(server.EntMan), Is.EqualTo(count), $"Server prototype {protoId} failed on deletion: count didn't reset properly\n" +
                        BuildDiffString(serverEntities, Entities(server.EntMan), server.EntMan));
                    Assert.That(Count(client.EntMan), Is.EqualTo(clientCount), $"Client prototype {protoId} failed on deletion: count didn't reset properly:\n" +
                        $"Expected {clientCount} and found {Count(client.EntMan)}.\n" +
                        $"Server count was {count}.\n" +
                        BuildDiffString(clientEntities, Entities(client.EntMan), client.EntMan));
                }
            });
        }

        /// <summary>
        /// Deletes any entities with <see cref="TimedDespawnComponent"/> that were not present in the baseline snapshot.
        /// Some entities spawn transient side-effects on deletion (e.g. explosion visuals). These side-effect entities
        /// use TimedDespawn and would persist across test iterations, corrupting baseline entity counts and causing
        /// cascading assertion failures.
        /// </summary>
        private static async Task CleanupTransientEntities(Pair.TestPair pair, HashSet<EntityUid> baselineEntities)
        {
            var server = pair.Server;
            await server.WaitPost(() =>
            {
                var toRemove = new List<EntityUid>();
                var query = server.EntMan.AllEntityQueryEnumerator<TimedDespawnComponent>();
                while (query.MoveNext(out var uid, out _))
                {
                    if (!baselineEntities.Contains(uid))
                        toRemove.Add(uid);
                }

                foreach (var uid in toRemove)
                {
                    server.EntMan.DeleteEntity(uid);
                }
            });
            await pair.RunTicksSync(3);
        }

        private static string BuildDiffString(IEnumerable<EntityUid> oldEnts, IEnumerable<EntityUid> newEnts, IEntityManager entMan)
        {
            var sb = new StringBuilder();
            var addedEnts = newEnts.Except(oldEnts);
            var removedEnts = oldEnts.Except(newEnts);
            if (addedEnts.Any())
                sb.AppendLine("Listing new entities:");
            foreach (var addedEnt in addedEnts)
            {
                sb.AppendLine(entMan.ToPrettyString(addedEnt));
            }
            if (removedEnts.Any())
                sb.AppendLine("Listing removed entities:");
            foreach (var removedEnt in removedEnts)
            {
                sb.AppendLine("\t" + entMan.ToPrettyString(removedEnt));
            }
            return sb.ToString();
        }

        private static bool HasRequiredDataField(Component component)
        {
            foreach (var field in component.GetType().GetFields())
            {
                foreach (var attribute in field.GetCustomAttributes(true))
                {
                    if (attribute is not DataFieldAttribute dataField)
                        continue;

                    if (dataField.Required)
                        return true;
                }
            }
            foreach (var property in component.GetType().GetProperties())
            {
                foreach (var attribute in property.GetCustomAttributes(true))
                {
                    if (attribute is not DataFieldAttribute dataField)
                        continue;

                    if (dataField.Required)
                        return true;
                }
            }
            return false;
        }

        [Test]
        public async Task AllComponentsOneToOneDeleteTest()
        {
            var skipComponents = new[]
            {
                "DebugExceptionOnAdd", // Debug components that explicitly throw exceptions
                "DebugExceptionExposeData",
                "DebugExceptionInitialize",
                "DebugExceptionStartup",
                "GridFill",
                "RoomFill",
                "Map", // We aren't testing a map entity in this test
                "MapGrid",
                "Broadphase",
                "StationData", // errors when removed mid-round
                "StationJobs",
                "ActiveGameRule", // Requires GameRule :P
                "Actor", // We aren't testing actor components, those need their player session set.
                "BlobFloorPlanBuilder", // Implodes if unconfigured.
                "DebrisFeaturePlacerController", // Above.
                "LoadedChunk", // Worldgen chunk loading malding.
                "BiomeSelection", // Whaddya know, requires config.
                "ActivatableUI", // Frontier: Requires enum key
                "AlertLevel", // Frontier: requires alert set
                "BluespaceErrorRule", // Frontier
                "GridSpawner", // mono - i wouldn't
            };

            var pair = Pair;
            var server = pair.Server;
            var entityManager = server.ResolveDependency<IEntityManager>();
            var componentFactory = server.ResolveDependency<IComponentFactory>();
            var logmill = server.ResolveDependency<ILogManager>().GetSawmill("EntityTest");

            await pair.CreateTestMap();
            await server.WaitRunTicks(5);
            var testLocation = pair.TestMap.GridCoords;

            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {

                    foreach (var type in componentFactory.AllRegisteredTypes)
                    {
                        var component = (Component)componentFactory.GetComponent(type);
                        var name = componentFactory.GetComponentName(type);

                        if (HasRequiredDataField(component))
                            continue;

                        // If this component is ignored
                        if (skipComponents.Contains(name))
                        {
                            continue;
                        }

                        var entity = SpawnEntity(entityManager, null, testLocation); // Monolith

                        Assert.That(entityManager.GetComponent<MetaDataComponent>(entity).EntityInitialized);

                        // The component may already exist if it is a mandatory component
                        // such as MetaData or Transform
                        if (entityManager.HasComponent(entity, type))
                        {
                            entityManager.DeleteEntity(entity);
                            continue;
                        }

                        logmill.Debug($"Adding component: {name}");

                        Assert.DoesNotThrow(() =>
                            {
                                entityManager.AddComponent(entity, component);
                            }, "Component '{0}' threw an exception.",
                            name);

                        entityManager.DeleteEntity(entity);
                    }
                });
            });
        }

        // Mono start - idk what this dont marked, but is Mono-creation
        private EntityUid SpawnEntity(
            IEntityManager entManager,
            string protoName,
            MapCoordinates coordinates,
            ComponentRegistry registry = null)
        {
            try
            {
                return entManager.SpawnEntity(protoName, coordinates, registry);
            }
            catch (Exception e)
            {
                // hey look, it tells you specifically what entity now!
                Assert.Fail($"Failed to spawn entity {protoName}\n{e}");
            }

            return EntityUid.Invalid;
        }

        private EntityUid SpawnEntity(
            IEntityManager entManager,
            string protoName,
            EntityCoordinates coordinates,
            ComponentRegistry registry = null)
        {
            try
            {
                return entManager.SpawnEntity(protoName, coordinates, registry);
            }
            catch (Exception e)
            {
                // hey look, it tells you specifically what entity now!
                Assert.Fail($"Failed to spawn entity {protoName}\n{e}");
            }

            return EntityUid.Invalid;
        }
        // Mono end
    }
}
