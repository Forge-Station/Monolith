using System.Numerics;
using Content.Server._WF.TractorBeam;
using Content.Server.Power.Components;
using Content.Shared._WF.TractorBeam;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.TractorBeam;

public sealed class TractorBeamOverloadTest
{
    [TestCase("WFTractorBeamEmitter")]
    [TestCase("WFTractorBeamEmitterSmall")]
    public async Task SustainedOverloadBreaksCaptureButBriefSpikesRecover(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.ResolveDependency<IMapManager>();
        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var (source, target, emitter, _) = TractorBeamTest.CreateLock(entities, maps, map.MapId,
                sourcePosition: new Vector2(25, 0), emitterPrototype: prototype);
            var system = entities.System<TractorBeamSystem>();
            var physics = entities.System<SharedPhysicsSystem>();
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            var power = entities.GetComponent<PowerConsumerComponent>(emitter);
            const float step = 1f / 60f;

            void Resist()
            {
                physics.SetLinearVelocity(source, Vector2.Zero);
                physics.SetLinearVelocity(target, new Vector2(100000, 0));
                physics.SetAngularVelocity(source, 0);
                physics.SetAngularVelocity(target, 0);
                system.UpdateBeforeSolve(false, step);
            }

            for (var i = 0; i < 30; i++)
                Resist();
            Assert.That(beam.Strain, Is.EqualTo(1f));
            Assert.That(beam.Target, Is.EqualTo(target), "A brief overload must leave time to react.");
            Assert.That(beam.OverloadTime, Is.GreaterThan(0));

            physics.SetLinearVelocity(source, Vector2.Zero);
            physics.SetLinearVelocity(target, Vector2.Zero);
            physics.SetAngularVelocity(source, 0);
            physics.SetAngularVelocity(target, 0);
            system.UpdateBeforeSolve(false, step);
            Assert.That(beam.OverloadTime, Is.Zero, "Reducing resistance must reset the continuous overload timer.");

            for (var i = 0; i < (int) MathF.Ceiling(beam.OverloadDuration / step) + 2 && beam.Target != null; i++)
                Resist();
            Assert.Multiple(() =>
            {
                Assert.That(beam.Target, Is.Null, "Even full power cannot sustain maximum strain indefinitely.");
                Assert.That(beam.Active, Is.False);
                Assert.That(beam.Visual, Is.Null);
                Assert.That(beam.OverloadTime, Is.Zero);
                Assert.That(beam.CooldownRemaining, Is.EqualTo(12));
                Assert.That(beam.Strain, Is.Zero);
                Assert.That(power.DrawRate, Is.EqualTo(beam.IdlePower));
            });
            entities.DeleteEntity(source);
            entities.DeleteEntity(target);
        });
        await pair.CleanReturnAsync();
    }
}
