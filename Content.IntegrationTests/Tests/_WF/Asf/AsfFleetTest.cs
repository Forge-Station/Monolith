using System.Collections.Generic;
using System.Linq;
using Content.Server.Power.Components;
using Content.Server.Power.Generator;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Power.Generator;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Asf;

/// <summary>Checks the hulls sold at the ASF shipyard: each one stays powered with its generators running and its shield up.</summary>
[TestFixture]
public sealed class AsfFleetTest
{
    private static readonly EntProtoId ShipyardConsole = "WFAsfComputerShipyard";

    /// <summary>Every ASF hull, with its generators at full output, powers every LV machine and raises its shield at idle.</summary>
    [Test]
    public async Task HullsArePoweredWithShieldsUp()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var loader = entities.System<MapLoaderSystem>();
        var maps = entities.System<SharedMapSystem>();
        var generators = entities.System<GeneratorSystem>();

        var hulls = new Dictionary<string, EntityUid>();
        var mapIds = new List<MapId>();

        await server.WaitAssertion(() =>
        {
            Assert.That(prototypes.Index(ShipyardConsole).TryGetComponent<ShipyardListingComponent>(out var listing, factory));
            foreach (var id in listing!.Shuttles)
            {
                var vessel = prototypes.Index<VesselPrototype>(id);
                maps.CreateMap(out var mapId);
                mapIds.Add(mapId);
                Assert.That(loader.TryLoadGrid(mapId, vessel.ShuttlePath, out var grid), Is.True, $"{id} loads as a grid.");
                hulls[id] = grid!.Value.Owner;
            }

            var genQuery = entities.EntityQueryEnumerator<FuelGeneratorComponent>();
            while (genQuery.MoveNext(out var uid, out var generator))
            {
                entities.EventBus.RaiseLocalEvent(uid, new PortableGeneratorSetTargetPowerMessage((int) (generator.MaxTargetPower / 1000)));
                generators.SetFuelGeneratorOn(uid, true, generator);
            }
        });

        // generators, SMES and APC supply ramp up over several seconds
        await server.WaitRunTicks(1800);

        await server.WaitAssertion(() =>
        {
            foreach (var (id, gridUid) in hulls)
            {
                var unpowered = new List<string>();
                var query = entities.EntityQueryEnumerator<ApcPowerReceiverComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var receiver, out var xform))
                {
                    if (xform.GridUid != gridUid || !xform.Anchored || !receiver.NeedsPower || receiver.PowerDisabled || receiver.Powered)
                        continue;
                    // hardpoints take no LV; their guns run on their own batteries
                    if (!entities.HasComponent<ExtensionCableReceiverComponent>(uid))
                        continue;
                    unpowered.Add($"{entities.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID} at {xform.Coordinates.Position}");
                }

                Assert.That(unpowered, Is.Empty, $"Unpowered on {id}: " + string.Join(", ", unpowered));

                var shieldQuery = entities.EntityQueryEnumerator<ShipShieldEmitterComponent, TransformComponent>();
                while (shieldQuery.MoveNext(out _, out var emitter, out var xform))
                {
                    if (xform.GridUid == gridUid)
                        Assert.That(emitter.Shield, Is.Not.Null, $"{id}'s shield is down at idle.");
                }
            }

            foreach (var mapId in mapIds)
            {
                maps.DeleteMap(mapId);
            }
        });

        await pair.CleanReturnAsync();
    }
}
