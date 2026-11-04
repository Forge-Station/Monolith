#nullable enable
using System.Numerics;
using Content.Shared._WF.Vehicles.Core;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>The headlight sits on the front bumper, the way the vehicle faces, whichever way that is.</summary>
[TestFixture]
public sealed class VehicleHeadlightTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task TheHeadlightIsAtTheFront(bool facingRight)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, startX: 10.5f, facingRight: facingRight);
        var server = pair.Server;
        var entMan = server.EntMan;
        await server.WaitPost(() =>
        {
            entMan.AddComponent<PointLightComponent>(rig.Vehicle);
            entMan.EnsureComponent<VehicleSpotlightComponent>(rig.Vehicle);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var light = entMan.GetComponent<PointLightComponent>(rig.Vehicle);
            var (position, rotation) = server.System<SharedTransformSystem>().GetWorldPositionRotation(rig.Vehicle);
            var lamp = position + rotation.RotateVec(light.Offset);
            var forward = rotation.ToWorldVec();
            Assert.That(Vector2.Dot(lamp - position, forward), Is.GreaterThan(0.3f),
                $"The lamp at {lamp} should be ahead of {position} facing {forward}.");
            Assert.That(MathF.Abs(Vector2.Dot(lamp - position, new Vector2(-forward.Y, forward.X))), Is.LessThan(0.3f),
                "The lamp should be in the middle of the bumper.");
        });

        await pair.CleanReturnAsync();
    }
}
