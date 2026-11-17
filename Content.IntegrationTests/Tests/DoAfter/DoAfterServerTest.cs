using System;
using Content.Shared.DoAfter;
using Robust.Shared.Exceptions;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Reflection;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.DoAfter
{
    [TestFixture]
    [TestOf(typeof(DoAfterComponent))]
    public sealed partial class DoAfterServerTest
    {
        [TestPrototypes]
        private const string Prototypes = @"
- type: entity
  name: DoAfterDummy
  id: DoAfterDummy
  components:
  - type: DoAfter
";

        [Serializable, NetSerializable]
        private sealed partial class TestDoAfterEvent : DoAfterEvent
        {
            public override DoAfterEvent Clone()
            {
                return this;
            }
        };

        [Test]
        public async Task TestSerializable()
        {
            await using var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            await server.WaitIdleAsync();
            var refMan = server.ResolveDependency<IReflectionManager>();

            await server.WaitPost(() =>
            {
                Assert.Multiple(() =>
                {
                    foreach (var type in refMan.GetAllChildren<DoAfterEvent>(true))
                    {
                        if (type.IsAbstract || type == typeof(TestDoAfterEvent))
                            continue;

                        Assert.That(type.HasCustomAttribute<NetSerializableAttribute>()
                                    && type.HasCustomAttribute<SerializableAttribute>(),
                            $"{nameof(DoAfterEvent)} is not NetSerializable. Event: {type.Name}");
                    }
                });
            });

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestFinished()
        {
            await using var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            await server.WaitIdleAsync();

            var entityManager = server.ResolveDependency<IEntityManager>();
            var timing = server.ResolveDependency<IGameTiming>();
            var doAfterSystem = entityManager.EntitySysManager.GetEntitySystem<SharedDoAfterSystem>();
            var ev = new TestDoAfterEvent();

            // That it finishes successfully
            await server.WaitPost(() =>
            {
                var tickTime = 1.0f / timing.TickRate;
                var mob = entityManager.SpawnEntity("DoAfterDummy", MapCoordinates.Nullspace);
                var args = new DoAfterArgs(entityManager, mob, tickTime / 2, ev, null) { Broadcast = true };
#pragma warning disable NUnit2045 // Interdependent assertions.
                Assert.That(doAfterSystem.TryStartDoAfter(args));
                Assert.That(ev.Cancelled, Is.False);
#pragma warning restore NUnit2045
            });

            await server.WaitRunTicks(1);
            Assert.That(ev.Cancelled, Is.False);

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestCancelled()
        {
            await using var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var entityManager = server.ResolveDependency<IEntityManager>();
            var timing = server.ResolveDependency<IGameTiming>();
            var doAfterSystem = entityManager.EntitySysManager.GetEntitySystem<SharedDoAfterSystem>();
            var ev = new TestDoAfterEvent();

            await server.WaitPost(() =>
            {
                var tickTime = 1.0f / timing.TickRate;

                var mob = entityManager.SpawnEntity("DoAfterDummy", MapCoordinates.Nullspace);
                var args = new DoAfterArgs(entityManager, mob, tickTime * 2, ev, null) { Broadcast = true };

                if (!doAfterSystem.TryStartDoAfter(args, out var id))
                {
                    Assert.Fail();
                    return;
                }

                Assert.That(!ev.Cancelled);
                doAfterSystem.Cancel(id);
                Assert.That(ev.Cancelled);

            });

            await server.WaitRunTicks(3);
            Assert.That(ev.Cancelled);

            await pair.CleanReturnAsync();
        }

        /// <summary>
        /// A do-after ending inside <see cref="SharedDoAfterSystem.Update(float)"/> can start another do-after
        /// or delete an entity that is still in the active query. That used to throw
        /// "Collection was modified" from the component-dictionary enumerator.
        /// </summary>
        [Test]
        public async Task TestUpdateSurvivesActiveQueryMutation()
        {
            await using var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            await server.WaitIdleAsync();

            var entityManager = server.ResolveDependency<IEntityManager>();
            var timing = server.ResolveDependency<IGameTiming>();
            var runtimeLog = server.ResolveDependency<IRuntimeLog>();
            var doAfterSystem = entityManager.EntitySysManager.GetEntitySystem<SharedDoAfterSystem>();
            var hook = entityManager.EntitySysManager.GetEntitySystem<DoAfterMutationTestSystem>();

            var performer = EntityUid.Invalid;
            var victim = EntityUid.Invalid;
            var fresh = EntityUid.Invalid;
            var exceptionsBefore = runtimeLog.ExceptionCount;

            try
            {
                await server.WaitPost(() =>
                {
                    var tickTime = 1.0f / timing.TickRate;
                    performer = entityManager.SpawnEntity("DoAfterDummy", MapCoordinates.Nullspace);
                    victim = entityManager.SpawnEntity("DoAfterDummy", MapCoordinates.Nullspace);
                    fresh = entityManager.SpawnEntity("DoAfterDummy", MapCoordinates.Nullspace);

                    var victimArgs = new DoAfterArgs(entityManager, victim, TimeSpan.FromSeconds(30), new TestDoAfterEvent(), null)
                    {
                        Broadcast = true,
                    };
                    Assert.That(doAfterSystem.TryStartDoAfter(victimArgs), Is.True);

                    hook.OnMutatingDoAfter = () =>
                    {
                        if (entityManager.TryGetComponent<ActiveDoAfterComponent>(victim, out _))
                            entityManager.DeleteEntity(victim);

                        if (!entityManager.HasComponent<ActiveDoAfterComponent>(fresh))
                        {
                            var freshArgs = new DoAfterArgs(entityManager, fresh, TimeSpan.FromSeconds(30), new TestDoAfterEvent(), null)
                            {
                                Broadcast = true,
                            };
                            Assert.That(doAfterSystem.TryStartDoAfter(freshArgs), Is.True);
                        }
                    };

                    var performerArgs = new DoAfterArgs(entityManager, performer, tickTime / 2, new MutatingDoAfterEvent(), null)
                    {
                        Broadcast = true,
                    };
                    Assert.That(doAfterSystem.TryStartDoAfter(performerArgs), Is.True);
                });

                await server.WaitRunTicks(2);

                await server.WaitAssertion(() =>
                {
                    Assert.That(entityManager.Deleted(victim), Is.True);
                    Assert.That(entityManager.HasComponent<ActiveDoAfterComponent>(fresh), Is.True);
                    Assert.That(runtimeLog.ExceptionCount, Is.EqualTo(exceptionsBefore), runtimeLog.Display());
                });
            }
            finally
            {
                hook.OnMutatingDoAfter = null;
            }

            await pair.CleanReturnAsync();
        }

        [Serializable, NetSerializable]
        private sealed partial class MutatingDoAfterEvent : DoAfterEvent
        {
            public override DoAfterEvent Clone()
            {
                return new MutatingDoAfterEvent();
            }
        }

        public sealed class DoAfterMutationTestSystem : EntitySystem
        {
            public Action OnMutatingDoAfter;

            public override void Initialize()
            {
                base.Initialize();
                SubscribeLocalEvent<MutatingDoAfterEvent>(ev =>
                {
                    if (!ev.Cancelled)
                        OnMutatingDoAfter?.Invoke();
                });
            }
        }
    }
}
