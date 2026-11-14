#nullable enable
using System.Numerics;
using System.Reflection;
using Content.Server.Movement.Components;
using Content.Server.Movement.Systems;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// Moving a pulled thing with the pointer, when the pointer isn't anywhere the puller can reach. The pointer is wherever
/// the client's eye is, and the eye is not always on the puller's map:
/// a vehicle operator looks out of the hull from its interior map, a drone console's camera is on the drone's hull,
/// a queen's eye and a watched xeno are elsewhere, a key pressed with the mouse off the viewport sends no position,
/// and a puller that has just changed layer is still seen from the old one until the client's input catches up.
/// </summary>
[TestFixture]
public sealed class PullPointerTest
{
    /// <summary>A pointer on another map or at a NaN position is ignored; one on the puller's map still works.</summary>
    [Test]
    public async Task APointerOffThePullersMapIsIgnored()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var otherMap = await pair.CreateTestMap();
        var session = await server.AddDummySession();

        var puller = EntityUid.Invalid;
        var pulled = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            puller = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.MapUid, Vector2.Zero));
            pulled = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.MapUid, new Vector2(1f, 0f)));
            server.PlayerMan.SetAttachedEntity(session, puller);
            Assert.That(entMan.System<PullingSystem>().TryStartPull(puller, pulled), Is.True, "The puller could not start pulling.");
        });
        await server.WaitRunTicks(2);

        var handler = typeof(PullController).GetMethod("OnRequestMovePulledObject", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var controller = entMan.System<PullController>();
        void Point(EntityCoordinates coords) => handler.Invoke(controller, new object?[] { session, coords, puller });

        await server.WaitAssertion(() =>
        {
            Point(new EntityCoordinates(otherMap.MapUid, new Vector2(3f, 3f)));
            Assert.That(entMan.HasComponent<PullMovingComponent>(pulled), Is.False, "A pointer on another map moved the pulled mob.");

            Point(new EntityCoordinates(puller, new Vector2(float.NaN, 0f)));
            Assert.That(entMan.HasComponent<PullMovingComponent>(pulled), Is.False, "A NaN pointer moved the pulled mob.");

            Point(new EntityCoordinates(puller, new Vector2(5f, 0f)));
            Assert.That(entMan.HasComponent<PullMovingComponent>(pulled), Is.True, "A pointer on the puller's map no longer moves the pulled mob.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A pointer sent with no position, or from an eye that is held elsewhere, moves nothing.</summary>
    [Test]
    public async Task APointerFromAnEyeElsewhereOrNowhereIsIgnored()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var otherMap = await pair.CreateTestMap();
        var session = await server.AddDummySession();

        var puller = EntityUid.Invalid;
        var pulled = EntityUid.Invalid;
        var camera = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            puller = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.MapUid, Vector2.Zero));
            pulled = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.MapUid, new Vector2(1f, 0f)));
            camera = entMan.SpawnEntity(null, new EntityCoordinates(otherMap.MapUid, new Vector2(8f, 8f)));
            server.PlayerMan.SetAttachedEntity(session, puller);
            entMan.System<SharedEyeSystem>().SetTarget(puller, camera);
            Assert.That(entMan.System<PullingSystem>().TryStartPull(puller, pulled), Is.True, "The puller could not start pulling.");
        });
        await server.WaitRunTicks(2);

        var handler = typeof(PullController).GetMethod("OnRequestMovePulledObject", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var controller = entMan.System<PullController>();
        void Point(EntityCoordinates coords) => handler.Invoke(controller, new object?[] { session, coords, puller });

        await server.WaitAssertion(() =>
        {
            // Where the pointer is when the mouse is off the viewport.
            Point(EntityCoordinates.Invalid);
            Assert.That(entMan.HasComponent<PullMovingComponent>(pulled), Is.False, "A pointer with no position moved the pulled mob.");

            // A pointer on the map the eye is held on, not the puller's.
            Point(new EntityCoordinates(camera, Vector2.Zero));
            Assert.That(entMan.HasComponent<PullMovingComponent>(pulled), Is.False, "A pointer from the eye's map moved the pulled mob.");

            // The puller changes map after the input was sent from the old one.
            entMan.System<SharedTransformSystem>().SetCoordinates(puller, new EntityCoordinates(otherMap.MapUid, Vector2.Zero));
            Point(new EntityCoordinates(map.MapUid, new Vector2(3f, 0f)));
            Assert.That(entMan.HasComponent<PullMovingComponent>(pulled), Is.False, "A pointer sent from the puller's old map moved the pulled mob.");
        });

        await pair.CleanReturnAsync();
    }
}
