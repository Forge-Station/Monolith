using System.Collections.Generic;
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._Mono.NPC.HTN.Operators;
using Content.Server.NPC;
using Content.Shared._Forge.KIAS;
using Content.Server.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasAutopilotCompletionTests
{
    public sealed class ArrivalObserverSystem : EntitySystem
    {
        public readonly Dictionary<EntityUid, int> Arrivals = new();

        public override void Initialize()
        {
            SubscribeLocalEvent<KiasAutopilotArrivedEvent>(OnArrival);
        }

        private void OnArrival(ref KiasAutopilotArrivedEvent args)
        {
            Arrivals[args.Console] = Arrivals.GetValueOrDefault(args.Console) + 1;
        }
    }

    [Test]
    public async Task AutopilotCompletionIsPerShipPerTripAndCancellationDoesNotArrive()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            em.EnsureComponent<ShuttleComponent>(map.Grid);
            var observer = em.System<ArrivalObserverSystem>();
            var shared = new ShipMoveToOperator();
            shared.Initialize(em.EntitySysManager);
            var first = em.SpawnEntity(null, new EntityCoordinates(map.Grid, .5f, .5f));
            var maps = em.System<SharedMapSystem>();
            var secondGrid = maps.CreateGridEntity(map.MapId);
            maps.SetTile(secondGrid, secondGrid.Comp, Vector2i.Zero, map.Tile.Tile);
            em.System<SharedTransformSystem>().SetWorldPosition(secondGrid, new Vector2(100, 0));
            em.EnsureComponent<ShuttleComponent>(secondGrid);
            var second = em.SpawnEntity(null, new EntityCoordinates(secondGrid, .5f, .5f));
            em.EnsureComponent<ShuttleConsoleComponent>(first);
            em.EnsureComponent<ShuttleConsoleComponent>(second);
            NPCBlackboard Board(EntityUid owner)
            {
                var board = new NPCBlackboard();
                board.SetValue(NPCBlackboard.Owner, owner);
                board.SetValue(shared.TargetKey, new EntityCoordinates(em.GetComponent<TransformComponent>(owner).GridUid!.Value, 3.5f, .5f));
                return board;
            }

            var firstTrip = Board(first);
            var secondTrip = Board(second);
            shared.Startup(firstTrip);
            shared.Startup(secondTrip);
            em.GetComponent<ShipSteererComponent>(first).Status = ShipSteeringStatus.InRange;
            em.GetComponent<ShipSteererComponent>(second).Status = ShipSteeringStatus.InRange;
            shared.ConditionalShutdown(firstTrip);
            shared.PlanShutdown(firstTrip);
            shared.ConditionalShutdown(secondTrip);
            Assert.That(observer.Arrivals.GetValueOrDefault(first), Is.EqualTo(1));
            Assert.That(observer.Arrivals.GetValueOrDefault(second), Is.EqualTo(1));

            var repeat = Board(first);
            shared.Startup(repeat);
            em.GetComponent<ShipSteererComponent>(first).Status = ShipSteeringStatus.InRange;
            shared.ConditionalShutdown(repeat);
            shared.PlanShutdown(repeat);
            Assert.That(observer.Arrivals[first], Is.EqualTo(2));

            var cancelled = Board(second);
            shared.Startup(cancelled);
            Assert.That(em.GetComponent<ShipSteererComponent>(second).Status, Is.EqualTo(ShipSteeringStatus.Moving));
            shared.PlanShutdown(cancelled);
            Assert.That(observer.Arrivals[second], Is.EqualTo(1));
        });
        await pair.CleanReturnAsync();
    }
}
