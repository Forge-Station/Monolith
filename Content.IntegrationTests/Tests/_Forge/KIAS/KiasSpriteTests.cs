#pragma warning disable RA0002
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Light;
using Content.Shared.Mind;
using Content.Shared.Radio.Components;
using Robust.Client.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasSpriteTests
{
    [Test]
    public async Task TallCoreSharesDepthWithDirectionalWindowAndHuman()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var targets = new Dictionary<string, NetEntity>();
        await pair.Server.WaitAssertion(() =>
        {
            var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
            var actor = server.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 1.5f, 0.5f));
            var mind = server.System<SharedMindSystem>().CreateMind(session.UserId);
            server.System<SharedMindSystem>().TransferTo(mind, actor);
            targets["MobHuman"] = server.GetNetEntity(actor);
            foreach (var id in new[] { "KiasCore", "WindowDirectional", "WallSolid", "Window" })
                targets[id] = server.GetNetEntity(server.SpawnEntity(id, new EntityCoordinates(map.Grid, 0.5f, 0.5f)));
        });
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
        {
            var em = pair.Client.ResolveDependency<IEntityManager>();
            SpriteComponent Sprite(string id) => em.GetComponent<SpriteComponent>(em.GetEntity(targets[id]));
            var core = Sprite("KiasCore");
            Assert.That(core.DrawDepth, Is.EqualTo(Sprite("MobHuman").DrawDepth), "A tall core must use spatial sorting with people, not always render underneath them.");
            Assert.That(core.DrawDepth, Is.EqualTo(Sprite("WindowDirectional").DrawDepth));
            Assert.That(core.DrawDepth, Is.GreaterThan(Sprite("WallSolid").DrawDepth));
            Assert.That(core.DrawDepth, Is.GreaterThan(Sprite("Window").DrawDepth));
            Assert.That(core.BaseRSI!.Size, Is.EqualTo(new Vector2i(32, 64)));
            Assert.That(core.Offset, Is.EqualTo(new Vector2(0, 0.5f)));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryKiasEntityLoadsNativeSpritesWithoutMissingStates()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        var targets = new Dictionary<string, NetEntity>();
        await pair.Server.WaitAssertion(() =>
        {
            var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
            var actor = server.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 1.5f, 0.5f));
            var mind = server.System<SharedMindSystem>().CreateMind(session.UserId);
            server.System<SharedMindSystem>().TransferTo(mind, actor);
            foreach (var prototype in pair.Server.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<EntityPrototype>()
                         .Where(p => !p.Abstract && p.ID.StartsWith("Kias")))
            {
                var uid = server.SpawnEntity(prototype.ID, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
                if (server.HasComponent<Content.Shared.Wall.WallMountComponent>(uid)
                    || prototype.ID is "KiasWeaponFlashDetector" or "KiasCollisionMonitor" or "KiasPdcRadar" or "KiasProximitySensor" or "KiasHorizon" or "KiasHullSensor")
                {
                    Assert.That(server.GetComponent<TransformComponent>(uid).NoLocalRotation, Is.False, prototype.ID);
                    Assert.That(server.HasComponent<Content.Shared.Rotatable.RotatableComponent>(uid), Is.True, prototype.ID);
                    server.System<SharedTransformSystem>().SetLocalRotation(uid, Angle.FromDegrees(90));
                }
                targets[prototype.ID] = server.GetNetEntity(uid);
            }
        });
        await pair.RunTicksSync(20);
        await pair.Client.WaitAssertion(() =>
        {
            var em = pair.Client.ResolveDependency<IEntityManager>();
            foreach (var (id, net) in targets)
            {
                var uid = em.GetEntity(net);
                var sprite = em.GetComponent<SpriteComponent>(uid);
                var size = id == "KiasCore" ? new Vector2i(32, 64)
                    : id is "KiasManagementConsole" or "KiasControllerProgrammer"
                        ? new Vector2i(64, 32) : new Vector2i(32, 32);
                Assert.That(sprite.BaseRSI, Is.Not.Null, id);
                Assert.That(sprite.BaseRSI!.Path.ToString(), Does.Contain("/Textures/_Forge/KIAS/"), id);
                Assert.That(sprite.BaseRSI.Size, Is.EqualTo(size), id);
                if (em.HasComponent<Content.Shared.Wall.WallMountComponent>(uid)
                    || id is "KiasWeaponFlashDetector" or "KiasCollisionMonitor" or "KiasPdcRadar" or "KiasProximitySensor" or "KiasHorizon" or "KiasHullSensor")
                    Assert.That(sprite.NoRotation, Is.False, id);
                foreach (var layer in sprite.AllLayers)
                {
                    Assert.That(layer.ActualRsi, Is.Not.Null, id);
                    Assert.That(layer.ActualRsi!.TryGetState(layer.RsiState, out _), Is.True, $"{id}: {layer.RsiState}");
                }
            }
            Assert.That(targets.Count, Is.GreaterThanOrEqualTo(91));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NativeClientChangesProgrammerSpriteFromAppearance()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid programmer = default, card = default;
        NetEntity target = default;
        await pair.Server.WaitAssertion(() =>
        {
            var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
            var actor = server.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 1.5f, 0.5f));
            var mind = server.System<SharedMindSystem>().CreateMind(session.UserId);
            server.System<SharedMindSystem>().TransferTo(mind, actor);
            programmer = server.SpawnEntity("KiasControllerProgrammer", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            card = server.SpawnEntity("KiasProgrammableController", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            target = server.GetNetEntity(programmer);
        });
        await pair.RunTicksSync(20);
        var em = pair.Client.ResolveDependency<IEntityManager>();
        await pair.Client.WaitAssertion(() => Assert.That(em.GetComponent<SpriteComponent>(em.GetEntity(target))[0].RsiState.Name, Is.EqualTo("empty")));
        await pair.Server.WaitAssertion(() => Assert.That(server.System<ItemSlotsSystem>().TryInsert(programmer, KiasControllerProgrammerComponent.SlotId, card, null), Is.True));
        await pair.RunTicksSync(10);
        await pair.Client.WaitAssertion(() => Assert.That(em.GetComponent<SpriteComponent>(em.GetEntity(target))[0].RsiState.Name, Is.EqualTo("card")));
        await pair.Server.WaitAssertion(() => Assert.That(server.System<ItemSlotsSystem>().TryEject(programmer, KiasControllerProgrammerComponent.SlotId, null, out _), Is.True));
        await pair.RunTicksSync(10);
        await pair.Client.WaitAssertion(() => Assert.That(em.GetComponent<SpriteComponent>(em.GetEntity(target))[0].RsiState.Name, Is.EqualTo("empty")));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NativeLightAndJammerAppearanceKeepTheirFunctionalStates()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid light = default, jammer = default;
        NetEntity lightNet = default, jammerNet = default;
        await pair.Server.WaitAssertion(() =>
        {
            var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
            var actor = server.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 1.5f, 0.5f));
            var mind = server.System<SharedMindSystem>().CreateMind(session.UserId);
            server.System<SharedMindSystem>().TransferTo(mind, actor);
            light = server.SpawnEntity("KiasNavigationLight", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            jammer = server.SpawnEntity("KiasJammer", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            lightNet = server.GetNetEntity(light);
            jammerNet = server.GetNetEntity(jammer);
        });
        await pair.RunTicksSync(20);
        var em = pair.Client.ResolveDependency<IEntityManager>();
        foreach (var (state, expected) in new[]
                 {
                     (PoweredLightState.Empty, "empty"), (PoweredLightState.Off, "off"),
                     (PoweredLightState.On, "base"), (PoweredLightState.Broken, "broken"),
                     (PoweredLightState.Burned, "burned"),
                 })
        {
            await pair.Server.WaitAssertion(() => server.System<SharedAppearanceSystem>().SetData(light, PoweredLightVisuals.BulbState, state));
            await pair.RunTicksSync(10);
            await pair.Client.WaitAssertion(() => Assert.That(em.GetComponent<SpriteComponent>(em.GetEntity(lightNet))[0].RsiState.Name, Is.EqualTo(expected)));
        }
        foreach (var level in Enum.GetValues<RadioJammerChargeLevel>())
        {
            await pair.Server.WaitAssertion(() =>
            {
                var appearance = server.System<SharedAppearanceSystem>();
                appearance.SetData(jammer, RadioJammerVisuals.LEDOn, true);
                appearance.SetData(jammer, RadioJammerVisuals.ChargeLevel, level);
            });
            await pair.RunTicksSync(10);
            await pair.Client.WaitAssertion(() =>
            {
                var sprite = em.GetComponent<SpriteComponent>(em.GetEntity(jammerNet));
                Assert.That(sprite[1].Visible, Is.True);
                Assert.That(sprite[1].RsiState.Name, Is.EqualTo($"jammer_{level.ToString().ToLowerInvariant()}_charge"));
            });
        }
        await pair.Server.WaitAssertion(() => server.System<SharedAppearanceSystem>().SetData(jammer, RadioJammerVisuals.LEDOn, false));
        await pair.RunTicksSync(10);
        await pair.Client.WaitAssertion(() => Assert.That(em.GetComponent<SpriteComponent>(em.GetEntity(jammerNet))[1].Visible, Is.False));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ProgrammerCardAppearanceFollowsPhysicalSlotAndRejectedEjection()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var position = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
            var programmer = em.SpawnEntity("KiasControllerProgrammer", position);
            var management = em.SpawnEntity("KiasManagementConsole", position);
            foreach (var machine in new[] { programmer, management })
            {
                var shape = em.GetComponent<FixturesComponent>(machine).Fixtures["fix1"].Shape;
                var bounds = shape.ComputeAABB(new Robust.Shared.Physics.Transform(Vector2.Zero, Angle.Zero), 0);
                Assert.That(bounds.Width - shape.Radius * 2, Is.EqualTo(1.9f).Within(0.001f));
                Assert.That(bounds.Height - shape.Radius * 2, Is.EqualTo(0.9f).Within(0.001f));
                Assert.That(bounds.Left + shape.Radius, Is.EqualTo(-0.45f).Within(0.001f));
            }
            foreach (var id in new[] { "KiasCore", "KiasDefenceServer", "KiasAtmosServer", "KiasPowerServer", "KiasCrewServer", "KiasNavigationServer" })
            {
                var server = em.SpawnEntity(id, position);
                var shape = em.GetComponent<FixturesComponent>(server).Fixtures["fix1"].Shape;
                var bounds = shape.ComputeAABB(new Robust.Shared.Physics.Transform(Vector2.Zero, Angle.Zero), 0);
                Assert.That(bounds.Width - shape.Radius * 2, Is.EqualTo(0.9f).Within(0.001f), id);
            }
            var card = em.SpawnEntity("KiasProgrammableController", position);
            var appearance = em.System<SharedAppearanceSystem>();
            var slots = em.System<ItemSlotsSystem>();
            void Check(bool inserted)
            {
                Assert.That(appearance.TryGetData<bool>(programmer, KiasVisuals.CardInserted, out var value), Is.True);
                Assert.That(value, Is.EqualTo(inserted));
            }
            Check(false);
            Assert.That(slots.TryInsert(programmer, KiasControllerProgrammerComponent.SlotId, card, null), Is.True);
            Check(true);
            var draft = em.GetComponent<KiasControllerProgrammerComponent>(programmer);
            draft.DraftDirty = true;
            Assert.That(slots.TryEject(programmer, KiasControllerProgrammerComponent.SlotId, null, out _), Is.False);
            Check(true);
            draft.DraftDirty = false;
            Assert.That(slots.TryEject(programmer, KiasControllerProgrammerComponent.SlotId, null, out _), Is.True);
            Check(false);
        });
        await pair.CleanReturnAsync();
    }
}
