using System.Linq;
using System.Numerics;
using Content.Server._WF.TractorBeam;
using Content.Server.Power.Components;
using Content.Shared._WF.TractorBeam;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.TractorBeam;

public sealed class TractorBeamRangeTest
{
    private const float Step = 1f / 60f;

    [TestCase(false)]
    [TestCase(true)]
    public async Task SetRangeReelsExistingHoldOrPinAtConfiguredSpeedThenHoldsWithoutRecapturing(bool pinned)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();
        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, console, actor, _) = TractorBeamConsoleTest.CreateConsole(entities, maps, map.MapId);
            var system = entities.System<TractorBeamSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var control = entities.GetComponent<TractorBeamConsoleComponent>(console);
            TractorBeamConsoleTest.Send(entities, console, actor, emitter, target);
            system.UpdateBeforeSolve(false, Step);
            if (pinned)
            {
                control.NextCommand = TimeSpan.Zero;
                TractorBeamConsoleTest.Send(entities, console, actor, emitter, target, lockInPlace: true);
                Assert.That(beam.LockedInPlace, Is.True);
            }
            var initialDistance = beam.HoldDistance;
            var requested = initialDistance - 0.1f;
            var initialPosition = transform.GetWorldPosition(target);
            var bearing = beam.HoldDirection;
            var orientation = beam.HoldAngle;
            var grace = beam.PowerGraceUntil;
            control.NextCommand = TimeSpan.Zero;
            TractorBeamConsoleTest.Send(entities, console, actor, emitter, target, desiredRange: requested);
            Assert.Multiple(() =>
            {
                Assert.That(beam.RequestedDistance, Is.EqualTo(requested));
                Assert.That(beam.Pulling, Is.True);
                Assert.That(beam.LockedInPlace, Is.False, "A pin must give way to physical reeling.");
                Assert.That(beam.HoldDistance, Is.EqualTo(initialDistance), "The command cannot instantly shorten the spring.");
                Assert.That(transform.GetWorldPosition(target), Is.EqualTo(initialPosition), "A range command never teleports a ship.");
                Assert.That(beam.HoldDirection, Is.EqualTo(bearing));
                Assert.That(beam.HoldAngle, Is.EqualTo(orientation));
                Assert.That(beam.PowerGraceUntil, Is.EqualTo(grace));
            });
            Assert.That(entities.System<SharedUserInterfaceSystem>().TryGetUiState<TractorBeamConsoleBoundUserInterfaceState>(
                console, TractorBeamUiKey.Key, out var state), Is.True);
            var displayed = state!.Emitters.Single(entry => entry.Entity == entities.GetNetEntity(emitter));
            Assert.That(displayed.DesiredRange, Is.EqualTo(requested));
            Assert.That(displayed.CurrentDistance, Is.GreaterThanOrEqualTo(requested));
            Assert.That(displayed.MinimumDistance, Is.GreaterThan(0f));

            system.UpdateBeforeSolve(false, Step);
            Assert.That(beam.HoldDistance, Is.EqualTo(initialDistance - beam.ReelSpeed * Step).Within(0.0001f));
            var partiallyReeled = beam.HoldDistance;
            control.NextCommand = TimeSpan.Zero;
            TractorBeamConsoleTest.Send(entities, console, actor, emitter, target, desiredRange: requested);
            Assert.That(beam.HoldDistance, Is.EqualTo(partiallyReeled), "Repeated orders cannot ratchet or recapture range.");
            Assert.That(beam.PowerGraceUntil, Is.EqualTo(grace));
            for (var i = 0; i < 12; i++)
                system.UpdateBeforeSolve(false, Step);
            Assert.Multiple(() =>
            {
                Assert.That(beam.HoldDistance, Is.EqualTo(requested).Within(0.0001f));
                Assert.That(beam.Pulling, Is.False, "On reaching the ordered rest distance, normal holding resumes.");
                Assert.That(beam.RequestedDistance, Is.EqualTo(requested), "The ordered distance remains visible after arrival.");
                Assert.That(beam.HoldDirection, Is.EqualTo(bearing));
                Assert.That(beam.HoldAngle, Is.EqualTo(orientation));
                Assert.That(entities.GetComponent<PowerConsumerComponent>(emitter).DrawRate, Is.GreaterThan(beam.HoldingPower));
            });
            TractorBeamConsoleTest.Send(entities, console, actor, emitter, null);
            Assert.That(beam.RequestedDistance, Is.Null);
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("unlocked")]
    [TestCase("other-target")]
    [TestCase("unpowered")]
    [TestCase("nan")]
    [TestCase("infinite")]
    [TestCase("negative")]
    [TestCase("zero")]
    [TestCase("unsafe")]
    [TestCase("outward")]
    [TestCase("too-far")]
    [TestCase("pin-and-reel")]
    public async Task InvalidRangeCommandsCannotAcquireOrChangeCapture(string reason)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();
        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, console, actor, _) = TractorBeamConsoleTest.CreateConsole(entities, maps, map.MapId);
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var control = entities.GetComponent<TractorBeamConsoleComponent>(console);
            if (reason != "unlocked")
            {
                TractorBeamConsoleTest.Send(entities, console, actor, emitter, target);
                entities.System<TractorBeamSystem>().UpdateBeforeSolve(false, Step);
            }
            var originalTarget = beam.Target;
            var originalDistance = beam.HoldDistance;
            var grace = beam.PowerGraceUntil;
            var requested = 30f;
            var requestedTarget = target;
            EntityUid? extraSource = null;
            switch (reason)
            {
                case "other-target":
                    var extra = TractorBeamTest.CreateLock(entities, maps, map.MapId, target, new Vector2(75, 10));
                    extraSource = extra.Source;
                    requestedTarget = extra.Source;
                    break;
                case "unpowered":
                    entities.GetComponent<PowerConsumerComponent>(emitter).NetworkLoad.ReceivingPower = beam.HoldingPower * 0.5f;
                    break;
                case "nan": requested = float.NaN; break;
                case "infinite": requested = float.PositiveInfinity; break;
                case "negative": requested = -10; break;
                case "zero": requested = 0; break;
                case "unsafe": requested = 0.1f; break;
                case "outward": requested = originalDistance + 10; break;
                case "too-far": requested = beam.MaxRange + 1; break;
            }
            control.NextCommand = TimeSpan.Zero;
            TractorBeamConsoleTest.Send(entities, console, actor, emitter, requestedTarget,
                lockInPlace: reason == "pin-and-reel", desiredRange: requested);
            Assert.Multiple(() =>
            {
                Assert.That(beam.Target, Is.EqualTo(originalTarget));
                Assert.That(beam.HoldDistance, Is.EqualTo(originalDistance));
                Assert.That(beam.PowerGraceUntil, Is.EqualTo(grace));
                Assert.That(beam.RequestedDistance, Is.Null);
                Assert.That(beam.Pulling, Is.False);
                Assert.That(beam.LockedInPlace, Is.False);
            });
            if (extraSource is { } extraGrid)
                entities.DeleteEntity(extraGrid);
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }
}
