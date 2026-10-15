using System.Collections.Generic;
using System.Linq;
using Content.Server._Forge.KIAS;
using Content.Server._Forge.KIAS.Controllers;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared.Interaction;
using Content.Shared.Wires;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasDeviceConfigurationTests
{
    [Test]
    public async Task NamedAndNumberedRoomsRemainSeparatedByDoorsAndWalls()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            for (var x = 0; x < 5; x++)
            for (var y = 0; y < 3; y++) em.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            for (var y = 0; y < 3; y++) em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, 2.5f, y + .5f));
            var named = em.SpawnEntity("KiasRoomScanner", new EntityCoordinates(map.Grid, .5f, .5f));
            var same = em.SpawnEntity("KiasRotarySwitch", new EntityCoordinates(map.Grid, 1.5f, .5f));
            var other = em.SpawnEntity("KiasRoomScanner", new EntityCoordinates(map.Grid, 3.5f, .5f));
            em.GetComponent<KiasDeviceComponent>(named).Room = "Bridge";
            var labels = em.System<KiasDeviceIdentitySystem>().Rooms(map.Grid, new[] { named, same, other });
            Assert.That(labels[named].Label, Is.EqualTo("Bridge"));
            Assert.That(labels[same].Label, Is.EqualTo("Bridge"));
            Assert.That(labels[same].Named, Is.True);
            Assert.That(labels[other].Named, Is.False);
            Assert.That(labels[other].Order, Is.Not.EqualTo(labels[named].Order));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CableMasksFollowNeighborsWithoutAWorkingCore()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            for (var x = -2; x <= 2; x++)
            for (var y = -2; y <= 2; y++) em.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            var center = em.SpawnEntity("KiasDataCable", new EntityCoordinates(map.Grid, .5f, .5f));
            var offsets = new[] { new Vector2i(0, 1), new Vector2i(0, -1), new Vector2i(1, 0), new Vector2i(-1, 0) };
            for (var mask = 0; mask < 16; mask++)
            {
                var neighbors = new List<EntityUid>();
                for (var bit = 0; bit < 4; bit++)
                    if ((mask & (1 << bit)) != 0) neighbors.Add(em.SpawnEntity("KiasDataCable", new EntityCoordinates(map.Grid, offsets[bit].X + .5f, offsets[bit].Y + .5f)));
                em.System<KiasSystem>().Rebuild(map.Grid);
                Assert.That(em.System<SharedAppearanceSystem>().TryGetData<WireVisDirFlags>(center, WireVisVisuals.ConnectedMask, out var flags), Is.True);
                Assert.That((int) flags, Is.EqualTo(mask));
                foreach (var neighbor in neighbors) em.DeleteEntity(neighbor);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task IdentitySurvivesRebuildAndRotaryRejectsInvalidOrUnauthorizedSettings()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var coords = new EntityCoordinates(map.Grid, .5f, .5f);
            var rotary = em.SpawnEntity("KiasRotarySwitch", coords);
            var other = em.SpawnEntity("KiasRotarySwitch", coords);
            var tool = em.SpawnEntity("KiasServiceTool", coords);
            var actor = em.SpawnEntity("MobHuman", coords);
            var core = em.SpawnEntity("KiasCore", coords);
            em.RemoveComponent<ApcPowerReceiverComponent>(core);
            var identities = em.System<KiasDeviceIdentitySystem>();
            var id = identities.Identifier(rotary);
            Assert.That(identities.Identifier(other), Is.Not.EqualTo(id));
            em.System<KiasSystem>().Rebuild(map.Grid);
            Assert.That(identities.Identifier(rotary), Is.EqualTo(id));
            var component = em.GetComponent<KiasServiceToolComponent>(tool);
            component.Mode = KiasServiceMode.Diagnose; component.Target = rotary;
            void Settings(int count, params int[] signals)
            {
                var message = new KiasDeviceSettingsMessage { Actor = actor, RotaryPositions = count, RotarySignals = signals.ToList() };
                em.EventBus.RaiseLocalEvent(tool, message);
            }
            Settings(2, 3, 0);
            var config = em.GetComponent<KiasRotaryComponent>(rotary);
            Assert.That(config.Positions, Is.EqualTo(2));
            Assert.That(config.Signals, Is.EqualTo(new[] { 3, 0 }));
            Settings(5, 0, 1, 2, 3, 0);
            Settings(2, 0, 99);
            Assert.That(config.Signals, Is.EqualTo(new[] { 3, 0 }));
            em.EnsureComponent<KiasClaimComponent>(map.Grid).Owner = new Robust.Shared.Network.NetUserId(Guid.NewGuid());
            Settings(3, 0, 1, 2);
            Assert.That(config.Positions, Is.EqualTo(2));
            em.RemoveComponent<KiasClaimComponent>(map.Grid);
            var state = (KiasServiceState) em.System<KiasDisplaySystem>().BuildLocalState(tool);
            Assert.That(state.TargetName, Does.Contain(id));
            Assert.That(state.RotarySignals, Is.EqualTo(new[] { 3, 0 }));
            em.RemoveComponent<ApcPowerReceiverComponent>(rotary);
            em.SpawnEntity("KiasDataCable", coords);
            em.System<KiasSystem>().Rebuild(map.Grid);
            em.EventBus.RaiseLocalEvent(rotary, new InteractHandEvent(actor, rotary));
            var io = em.System<KiasControllerIoSystem>();
            var profile = io.Profiles(rotary).Single(id => id.StartsWith("Link."));
            Assert.That(io.TryOutput(rotary, profile, "out:KiasPosition0", out _, out _), Is.True);
            em.EventBus.RaiseLocalEvent(rotary, new InteractHandEvent(actor, rotary));
            Assert.That(io.TryOutput(rotary, profile, "out:KiasPosition3", out _, out _), Is.True);
            var scanner = em.SpawnEntity("KiasRoomScanner", coords);
            component.Target = scanner;
            em.GetComponent<KiasDeviceComponent>(scanner).Room = "Keep room";
            em.EventBus.RaiseLocalEvent(tool, new KiasDeviceSettingsMessage { Actor = actor, Range = 999 });
            Assert.That(em.GetComponent<KiasRoomScannerComponent>(scanner).Range, Is.EqualTo(7));
            Assert.That(em.GetComponent<KiasDeviceComponent>(scanner).Room, Is.EqualTo("Keep room"));
            em.EventBus.RaiseLocalEvent(tool, new KiasDeviceSettingsMessage { Actor = actor, Range = float.NaN });
            Assert.That(em.GetComponent<KiasRoomScannerComponent>(scanner).Range, Is.EqualTo(7));
        });
        await pair.CleanReturnAsync();
    }
}
