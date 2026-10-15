using Content.Server._Forge.KIAS;
using Content.Server._Forge.KIAS.Controllers;
using Content.Server.Power.Components;
using Content.Server.NPC.HTN;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasFaunaTests
{
    [TestCase("MobCarp", false)]
    [TestCase("MobQuartzCrab", false)]
    [TestCase("MobSyndicateFootsoldier", false)]
    [TestCase("MobCarp", true)]
    [TestCase("MobCleanBotSyndie", false)]
    public async Task AdvancedScannerDetectsHostilesWithoutNearbyPlayers(string prototype, bool installed)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid scanner = default, creature = default;
        var pulses = 0;
        void Output(EntityUid device, string profile, string port, KiasGraphValue value)
        {
            if (device == scanner && profile == "RoomScanner" && port == "FaunaThreat") pulses++;
        }
        await pair.Server.WaitAssertion(() =>
        {
            for (var x = -2; x < 5; x++)
            for (var y = -2; y < 5; y++)
                em.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            var coordinates = new EntityCoordinates(map.Grid, .5f, .5f);
            EntityUid Spawn(string id)
            {
                var uid = em.SpawnEntity(id, coordinates);
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                return uid;
            }
            Spawn("KiasCore"); Spawn("KiasDataCable"); Spawn("KiasCrewServer");
            scanner = Spawn("KiasAdvancedRoomScanner");
            if (installed)
            {
                var slots = em.System<ItemSlotsSystem>();
                slots.TryEject(scanner, "kias-module-3", null, out _);
                Assert.That(slots.TryInsert(scanner, "kias-module-3", Spawn("KiasThreatModule"), null), Is.True);
            }
            creature = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, 1.5f, 1.5f));
            em.RemoveComponent<HTNComponent>(creature);
            em.System<KiasControllerIoSystem>().Emitted += Output;
            em.System<KiasSystem>().Rebuild(map.Grid);
        });
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<TransformComponent>(creature).GridUid, Is.EqualTo(map.Grid.Owner));
            Assert.That(em.System<KiasSystem>().IsOnline(scanner), Is.True);
            Assert.That(em.GetComponent<KiasRoomScannerComponent>(scanner).Modules.HasFlag(KiasScannerModules.Threat), Is.True);
            em.System<KiasCrewSystem>().RefreshCounts(map.Grid);
            Assert.That(pulses, Is.GreaterThan(0), "An empty compartment must still detect hostile fauna.");
            pulses = 0;
            em.System<MobStateSystem>().ChangeMobState(creature, MobState.Dead);
            em.System<KiasCrewSystem>().RefreshCounts(map.Grid);
            Assert.That(pulses, Is.Zero);
            em.DeleteEntity(creature);
            var friendly = em.SpawnEntity("MobCat", new EntityCoordinates(map.Grid, 1.5f, 1.5f));
            em.System<KiasCrewSystem>().RefreshCounts(map.Grid);
            Assert.That(pulses, Is.Zero, "Friendly animals must not raise the fauna output.");
            var extra = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, 1.5f, 1.5f));
            em.RemoveComponent<HTNComponent>(extra);
            Assert.That(em.System<ItemSlotsSystem>().TryEject(scanner, "kias-module-3", null, out _), Is.True);
            em.System<KiasCrewSystem>().RefreshCounts(map.Grid);
            Assert.That(pulses, Is.Zero, "Removing the threat module must disable fauna detection.");
            em.System<KiasControllerIoSystem>().Emitted -= Output;
        });
        await pair.CleanReturnAsync();
    }
}
