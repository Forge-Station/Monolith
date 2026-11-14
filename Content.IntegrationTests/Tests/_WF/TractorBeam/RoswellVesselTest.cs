using System.Numerics;
using Content.Server._NF.Shipyard.Components;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.Administration.Systems;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.TractorBeam;
using Content.Server.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.TractorBeam;

public sealed class RoswellVesselTest
{
    [Test]
    public async Task StationGuardVoucherListsLoadableJudgeRoswellAtStaffShipyard()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            entities.DeleteEntity(map.Grid);
            var vessel = prototypes.Index<VesselPrototype>("WFRoswell");
            Assert.That(vessel.Purchasable, Is.False);
            Assert.That(vessel.Group, Is.EqualTo(ShipyardConsoleUiKey.Sr));
            Assert.That(entities.System<AdminVesselSpawnSystem>()
                .TrySpawnVessel(vessel, map.MapId, Vector2.Zero, null, out var spawned), Is.True);
            var ship = spawned!.Value;
            Assert.That(entities.HasComponent<ShuttleComponent>(ship), Is.True);
            var dishes = entities.EntityQueryEnumerator<TractorBeamEmitterComponent, TransformComponent, MetaDataComponent>();
            var smallDishes = 0;
            var otherDishes = 0;
            while (dishes.MoveNext(out _, out _, out var transform, out var metadata))
            {
                if (transform.GridUid != ship)
                    continue;
                if (metadata.EntityPrototype?.ID == "WFTractorBeamEmitterSmall")
                    smallDishes++;
                else
                    otherDishes++;
            }
            Assert.That(smallDishes, Is.EqualTo(1), "Roswell must load with exactly one compact dish.");
            Assert.That(otherDishes, Is.Zero, "Roswell must not retain the oversized dish from its earlier export.");
            var voucher = entities.SpawnEntity("ShipVoucherFrontierGuard", entities.GetComponent<TransformComponent>(ship).Coordinates);
            var component = entities.GetComponent<ShipyardVoucherComponent>(voucher);
            Assert.That(component.ConsoleType, Is.EqualTo(ShipyardConsoleUiKey.Sr));
            Assert.That(component.CompanyName, Is.Not.EqualTo("TSF"));
            Assert.That(component.Vessels, Has.Count.EqualTo(1));
            Assert.That(component.Vessels.Contains("WFRoswell"), Is.True);
            var shipyard = entities.System<ShipyardSystem>();
            Assert.That(shipyard.GetAvailableShuttles(ship, ShipyardConsoleUiKey.Sr, targetId: voucher).available,
                Does.Contain("WFRoswell"), "The standard guard voucher must offer Roswell at the staff shipyard.");
            Assert.That(shipyard.GetAvailableShuttles(ship, ShipyardConsoleUiKey.Security, targetId: voucher).available,
                Does.Not.Contain("WFRoswell"));
            entities.DeleteEntity(voucher);
            entities.DeleteEntity(ship);
        });
        await pair.CleanReturnAsync();
    }
}
