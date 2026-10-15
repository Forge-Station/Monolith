using Content.Server._Forge.KIAS;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasScannerReconciliationTests
{
    [Test]
    public async Task I01_I05_I06_I09_PhysicalRoomsHandOffAndPreserveDirectBindings()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid first = default, second = default, alarm = default, direct = default;
        await pair.Server.WaitAssertion(() =>
        {
            for (var x = 0; x < 42; x++)
            for (var y = 0; y < 7; y++)
                em.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            EntityUid Spawn(string id, float x = 1.5f, float y = 3.5f)
            {
                var uid = em.SpawnEntity(id, new EntityCoordinates(map.Grid, x, y));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                return uid;
            }
            for (var x = 0; x < 42; x++)
            {
                Spawn("WallSolid", x + .5f, .5f);
                Spawn("WallSolid", x + .5f, 6.5f);
                Spawn("WallSolid", x + .5f);
            }
            for (var y = 1; y < 6; y++)
            {
                if (y == 3) continue;
                Spawn("WallSolid", .5f, y + .5f);
                Spawn("WallSolid", 41.5f, y + .5f);
            }
            Spawn("KiasDataCable"); Spawn("KiasCore"); Spawn("KiasAtmosServer"); Spawn("KiasCrewServer");
            first = Spawn("KiasRoomScanner"); second = Spawn("KiasRoomScanner");
            foreach (var scanner in new[] { first, second })
            {
                Assert.That(em.System<ItemSlotsSystem>().TryEject(scanner, "kias-module-3", null, out var previousModule), Is.True);
                em.DeleteEntity(previousModule!.Value);
                Assert.That(em.System<ItemSlotsSystem>().TryInsert(scanner, "kias-module-3", Spawn("KiasConnectorModule"), null), Is.True);
            }
            alarm = Spawn("AirAlarm", 38.5f, 1.5f);
            direct = Spawn("AirAlarm", 39.5f, 1.5f);
            em.EnsureComponent<KiasIntegratedComponent>(direct).Direct = true;
            em.EnsureComponent<KiasDeviceComponent>(direct).Role = KiasDeviceRole.Adapter;
        });
        await pair.RunTicksSync(60);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasIntegratedComponent>(alarm).Scanner, Is.EqualTo(first));
            Assert.That(em.System<KiasIntegrationSystem>().CanControl(alarm), Is.True);
            em.System<SharedTransformSystem>().SetLocalRotation(first, Angle.FromDegrees(180));
        });
        await pair.RunTicksSync(60);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasIntegratedComponent>(alarm).Scanner, Is.EqualTo(second));
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            em.System<SharedTransformSystem>().SetLocalRotation(second, Angle.FromDegrees(180));
        });
        await pair.RunTicksSync(60);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasIntegratedComponent>(alarm).Scanner, Is.Null);
            Assert.That(em.System<KiasIntegrationSystem>().CanControl(alarm), Is.False);
            Assert.That(em.GetComponent<KiasIntegratedComponent>(direct).Direct, Is.True);
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            em.System<SharedTransformSystem>().SetLocalRotation(first, Angle.Zero);
        });
        await pair.RunTicksSync(60);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasIntegratedComponent>(alarm).Scanner, Is.EqualTo(first));
        });
        await pair.RunTicksSync(30);
        uint revision = 0;
        await pair.Server.WaitAssertion(() => revision = em.GetComponent<KiasGridComponent>(map.Grid).Revision);
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() => Assert.That(em.GetComponent<KiasGridComponent>(map.Grid).Revision, Is.EqualTo(revision)));
        await pair.Server.WaitAssertion(() => em.System<SharedTransformSystem>().Unanchor(first));
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasIntegratedComponent>(alarm).Scanner, Is.Null);
            em.System<SharedTransformSystem>().AnchorEntity(first);
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasIntegratedComponent>(alarm).Scanner, Is.EqualTo(first));
            Assert.That(em.System<ItemSlotsSystem>().TryEject(first, "kias-module-3", null, out _), Is.True);
            Assert.That(em.System<ItemSlotsSystem>().TryEject(first, "kias-module-6", null, out _), Is.True);
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasIntegratedComponent>(alarm).Scanner, Is.Null);
            Assert.That(em.GetComponent<KiasIntegratedComponent>(direct).Direct, Is.True);
        });
        await pair.CleanReturnAsync();
    }
}
