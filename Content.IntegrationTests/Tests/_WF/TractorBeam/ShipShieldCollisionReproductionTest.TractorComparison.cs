using System.Numerics;
using Content.Server._WF.TractorBeam;
using Content.Shared._WF.TractorBeam;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.TractorBeam;

public sealed partial class ShipShieldCollisionReproductionTest
{
    [TestCase("ShipMissileASM150Unguided")]
    [TestCase("220mmBulletAPHE")]
    public async Task AdjacentShieldEdgesHaveTheSameOutcomeWithAnActiveTractorBeam(string prototype)
    {
        await ReproduceShieldContact(prototype, false, (entities, maps, map, target) =>
        {
            entities.System<SharedPhysicsSystem>().SetBodyType(target, BodyType.Dynamic);
            var (source, _, emitter, _) = TractorBeamTest.CreateLock(entities, maps, map, target, new Vector2(-50, 0));
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            entities.System<TractorBeamSystem>().UpdateBeforeSolve(false, 1f / 60f);
            Assert.That(beam.Active, Is.True);
            Assert.That(beam.Target, Is.EqualTo(target));
            return source;
        });
    }
}
