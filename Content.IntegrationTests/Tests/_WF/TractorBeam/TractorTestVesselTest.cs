using System.Linq;
using System.Numerics;
using Content.Server._WF.Administration.Systems;
using Content.Server._WF.TractorBeam;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.TractorBeam;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.TractorBeam;

public sealed class TractorTestVesselTest
{
    [Test]
    public async Task AdminSpawnProducesFlyablePoweredArrestorTestVessel()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        EntityUid ship = default;
        EntityUid emitter = default;
        EntityUid console = default;
        EntityUid helm = default;
        EntityUid[] thrusters = [];

        await server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var vessel = prototypes.Index<VesselPrototype>("WFTractorTest");
            Assert.Multiple(() =>
            {
                Assert.That(vessel.Name, Is.EqualTo("TRACTOR TEST"));
                Assert.That(vessel.Abstract, Is.False, "The Wolfgate spawn menu lists non-abstract vessels.");
                Assert.That(vessel.Purchasable, Is.False, "The administrative test reactor must stay out of shipyard sales.");
            });
            Assert.That(entities.System<AdminVesselSpawnSystem>()
                .TrySpawnVessel(vessel, map.MapId, Vector2.Zero, null, out var spawned), Is.True);
            ship = spawned!.Value;
            Assert.That(entities.GetComponent<MetaDataComponent>(ship).EntityName, Is.EqualTo("TRACTOR TEST"));
            Assert.That(entities.HasComponent<ShuttleComponent>(ship), Is.True);

            var machines = entities.GetComponent<TransformComponent>(ship).ChildEnumerator;
            var children = new System.Collections.Generic.List<EntityUid>();
            while (machines.MoveNext(out var child))
                children.Add(child);

            emitter = children.Single(uid => entities.HasComponent<TractorBeamEmitterComponent>(uid));
            console = children.Single(uid => entities.HasComponent<TractorBeamConsoleComponent>(uid));
            helm = children.Single(uid => entities.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "ComputerShuttle");
            thrusters = children.Where(uid => entities.HasComponent<ThrusterComponent>(uid)).ToArray();
            Assert.That(thrusters, Has.Length.EqualTo(9), "Eight linear thrusters and one gyroscope are required.");
            Assert.That(entities.GetComponent<TransformComponent>(emitter).Anchored, Is.True);
        });

        // Allow node groups, battery networks, APC extension cables, and thrusters to settle.
        await pair.RunSeconds(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<PowerConsumerComponent>(emitter).ReceivedPower,
                Is.GreaterThanOrEqualTo(entities.GetComponent<TractorBeamEmitterComponent>(emitter).IdlePower));
            Assert.That(entities.GetComponent<ApcPowerReceiverComponent>(console).Powered, Is.True);
            Assert.That(entities.GetComponent<ApcPowerReceiverComponent>(helm).Powered, Is.True);
            foreach (var uid in thrusters)
            {
                Assert.That(entities.System<ThrusterSystem>().CanEnable(uid, entities.GetComponent<ThrusterComponent>(uid)),
                    Is.True, "Every thruster must have power and an unobstructed exhaust.");
            }

            var air = entities.System<AtmosphereSystem>().GetContainingMixture((console, null));
            Assert.That(air, Is.Not.Null);
            Assert.That(air!.Pressure, Is.InRange(90f, 115f), "The enclosed bridge should retain breathable pressure.");

            // Load-test the actual HV network at worst-case dish demand without mocking received
            // power. The 2 MW reactor is shared with the ship: maximum strain should now stress
            // its supply rather than fit comfortably inside the old 500 kW demand.
            var beam = entities.GetComponent<TractorBeamEmitterComponent>(emitter);
            beam.IdlePower = beam.MaxPower;
        });

        await pair.RunSeconds(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<PowerConsumerComponent>(emitter).ReceivedPower,
                Is.InRange(entities.GetComponent<TractorBeamEmitterComponent>(emitter).MaxPower * 0.9f,
                    entities.GetComponent<TractorBeamEmitterComponent>(emitter).MaxPower),
                "The test reactor should approach full beam output while sharing power with ship systems.");
            Assert.That(entities.GetComponent<ApcPowerReceiverComponent>(console).Powered, Is.True);
            Assert.That(entities.GetComponent<ApcPowerReceiverComponent>(helm).Powered, Is.True);
            entities.DeleteEntity(ship);
        });

        await pair.CleanReturnAsync();
    }
}
