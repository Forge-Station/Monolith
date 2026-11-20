#nullable enable annotations

using System.Numerics;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Shuttles;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Events;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Shuttles;

/// <summary>Checks the server tells the helm about the ship's FTL drive, keeps that current, and still refuses a jump without one.</summary>
[TestFixture]
public sealed class WFFtlDriveTest
{
    private const string Drive = "MachineFTLDrive";

    [Test]
    public async Task StatusFollowsTheTestTheJumpUses()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var other = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var shuttles = entities.System<ShuttleSystem>();
            var grid = map.Grid.Owner;

            void AssertStatus(WFFtlDriveStatus expected)
            {
                Assert.That(shuttles.GetFtlDriveStatus(grid), Is.EqualTo(expected));
                Assert.That(shuttles.GetFTLRange(grid) > 0, Is.EqualTo(expected == WFFtlDriveStatus.Ready),
                    "Only a ship the page calls ready has any FTL range to jump with.");
            }

            AssertStatus(WFFtlDriveStatus.Missing);

            var first = entities.SpawnEntity(Drive, map.GridCoords);
            var firstPower = entities.GetComponent<ApcPowerReceiverComponent>(first);
            firstPower.Powered = false;
            AssertStatus(WFFtlDriveStatus.Unpowered);

            firstPower.Powered = true;
            AssertStatus(WFFtlDriveStatus.Ready);

            var second = entities.SpawnEntity(Drive, map.GridCoords);
            var secondPower = entities.GetComponent<ApcPowerReceiverComponent>(second);
            secondPower.Powered = false;
            AssertStatus(WFFtlDriveStatus.Ready);

            firstPower.Powered = false;
            AssertStatus(WFFtlDriveStatus.Unpowered);

            secondPower.Powered = true;
            AssertStatus(WFFtlDriveStatus.Ready);

            entities.DeleteEntity(first);
            entities.DeleteEntity(second);
            AssertStatus(WFFtlDriveStatus.Missing);

            var elsewhere = entities.SpawnEntity(Drive, other.GridCoords);
            entities.GetComponent<ApcPowerReceiverComponent>(elsewhere).Powered = true;
            AssertStatus(WFFtlDriveStatus.Missing);
            entities.DeleteEntity(elsewhere);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OpenHelmsAreUpdatedWhenTheDriveChanges()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var helms = entities.System<ShuttleConsoleSystem>();
            var ui = entities.System<SharedUserInterfaceSystem>();
            var console = entities.SpawnEntity("ComputerShuttle", map.GridCoords);
            var actor = entities.SpawnEntity("MobHuman", map.GridCoords);

            WFFtlDriveStatus Reported()
            {
                Assert.That(ui.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var state), Is.True);
                return state!.MapState.FtlDrive;
            }

            Assert.That(Reported(), Is.EqualTo(WFFtlDriveStatus.Missing), "A ship with no drive reports none.");

            var drive = entities.SpawnEntity(Drive, map.GridCoords);
            var power = entities.GetComponent<ApcPowerReceiverComponent>(drive);
            power.Powered = false;
            ui.OpenUi(console, ShuttleConsoleUiKey.Key, actor);
            Assert.That(Reported(), Is.EqualTo(WFFtlDriveStatus.Unpowered),
                "Opening the helm must not show a snapshot older than the drive.");

            power.Powered = true;
            helms.Update(0.3f);
            Assert.That(Reported(), Is.EqualTo(WFFtlDriveStatus.Ready), "Power returning reaches an open helm.");

            power.Powered = false;
            helms.Update(0.3f);
            Assert.That(Reported(), Is.EqualTo(WFFtlDriveStatus.Unpowered), "Power failing reaches an open helm.");

            entities.DeleteEntity(drive);
            helms.Update(0.3f);
            Assert.That(Reported(), Is.EqualTo(WFFtlDriveStatus.Missing), "A drive removed from the ship reaches an open helm.");

            drive = entities.SpawnEntity(Drive, map.GridCoords);
            entities.GetComponent<ApcPowerReceiverComponent>(drive).Powered = true;
            helms.Update(0.3f);
            Assert.That(Reported(), Is.EqualTo(WFFtlDriveStatus.Ready), "A drive built on the ship reaches an open helm.");

            ui.CloseUi(console, ShuttleConsoleUiKey.Key, actor);
            entities.DeleteEntity(drive);
            entities.DeleteEntity(console);
            entities.DeleteEntity(actor);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task JumpRequestsWithoutAWorkingDriveAreRefused()
    {
        // The accepted request starts an FTL jump, which leaves a hyperspace map behind.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var ui = entities.System<SharedUserInterfaceSystem>();
            var console = entities.SpawnEntity("ComputerShuttle", map.GridCoords);
            var actor = entities.SpawnEntity("MobHuman", map.GridCoords);
            var grid = map.Grid.Owner;
            ui.OpenUi(console, ShuttleConsoleUiKey.Key, actor);

            // A modified client can send the request whatever the page allows.
            void RequestJump() => ui.RaiseUiMessage(console, ShuttleConsoleUiKey.Key, new ShuttleConsoleFTLPositionMessage
            {
                Coordinates = new MapCoordinates(new Vector2(200, 0), map.MapId),
                Angle = Angle.Zero,
                Actor = actor,
            });

            RequestJump();
            Assert.That(entities.HasComponent<FTLComponent>(grid), Is.False, "A ship with no drive must not jump.");

            var drive = entities.SpawnEntity(Drive, map.GridCoords);
            var power = entities.GetComponent<ApcPowerReceiverComponent>(drive);
            power.Powered = false;
            RequestJump();
            Assert.That(entities.HasComponent<FTLComponent>(grid), Is.False, "A ship with an unpowered drive must not jump.");

            power.Powered = true;
            RequestJump();
            Assert.That(entities.HasComponent<FTLComponent>(grid), Is.True,
                "The same request must start a jump once the drive works, or the refusals above prove nothing.");
        });
        await pair.CleanReturnAsync();
    }
}
