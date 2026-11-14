#nullable enable
using System.Linq;
using System.Numerics;
using Content.Client._Mono.FireControl.UI;
using Content.Client._WF.Cockpit;
using Content.Server._Crescent.ShipShields;
using Content.Server._WF.ShipShields;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.FireControl;
using Content.Shared._WF.ShipShields;
using Content.Shared.Access.Components;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.ShipShields;

/// <summary>Checks the gunnery console's simplified shield control: who may use it, and that it only shows with shields aboard.</summary>
[TestFixture]
public sealed class WFShipShieldGunneryControlTest
{
    [Test]
    public async Task GunnerWorksTheShieldsFromAPoweredConsole()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var generator = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            entities.GetComponent<ApcPowerReceiverComponent>(generator).Powered = true;
            var shields = entities.System<ShipShieldsSystem>();
            var controls = entities.System<WFShipShieldShuntSystem>();
            shields.Update(1.5f);
            Assert.That(entities.HasComponent<ShipShieldedComponent>(map.Grid.Owner), Is.True, "The ship needs its shield up to start.");

            var console = entities.SpawnEntity("ComputerGunneryConsole", map.GridCoords);
            entities.RemoveComponent<AccessReaderComponent>(console);
            var power = entities.GetComponent<ApcPowerReceiverComponent>(console);
            power.Powered = true;
            var actor = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.System<SharedUserInterfaceSystem>().OpenUi(console, FireControlConsoleUiKey.Key, actor);

            Send(new WFShipShieldSetShuntMessage(0.6f, 0.5f, 1.2f));
            Assert.That(controls.GetState(map.Grid.Owner).TargetConcentration, Is.EqualTo(0.5f), "The gunner should be able to aim the shields.");

            Send(new WFShipShieldSetEnabledMessage(false));
            Assert.That(controls.GetState(map.Grid.Owner).Enabled, Is.False, "And lower them.");

            power.Powered = false;
            Send(new WFShipShieldSetEnabledMessage(true));
            Assert.That(controls.GetState(map.Grid.Owner).Enabled, Is.False, "A dead gunnery console cannot work the shields.");

            power.Powered = true;
            Send(new WFShipShieldSetEnabledMessage(true));
            Assert.That(controls.GetState(map.Grid.Owner).Enabled, Is.True);

            void Send(BoundUserInterfaceMessage message)
            {
                message.Actor = actor;
                message.Entity = entities.GetNetEntity(console);
                message.UiKey = FireControlConsoleUiKey.Key;
                entities.EventBus.RaiseLocalEvent(console, (object) message, true);
            }
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShieldColumnShowsOnlyWithShieldsAndLeavesTheScopeRoom()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            using var gunnery = new FireControlWindow();
            var panel = WFCockpitLease.Descendants(gunnery).Single(control => control.Name == "WfGunneryShields");
            Assert.That(panel.Visible, Is.False, "A ship with no shield generator has no shield column.");

            gunnery.WfUpdateShields(new WFShipShieldShuntState(true, true, 0.8f, MathF.PI / 2f, 0.25f, MathF.PI / 2f));
            Assert.That(panel.Visible, Is.True);

            // The smallest the console goes.
            var size = gunnery.MinSize;
            gunnery.Measure(size);
            gunnery.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
            Assert.That(panel.Width, Is.EqualTo(FireControlWindow.WfShieldPanelWidth).Within(1f));

            foreach (var child in WFCockpitLease.Descendants(panel).Where(control => control != panel && control.VisibleInTree))
            {
                Assert.That(child.GlobalPosition.X, Is.GreaterThanOrEqualTo(panel.GlobalPosition.X - 1), $"{child.GetType().Name} sticks out of the shield column.");
                Assert.That(child.GlobalPosition.X + child.Width, Is.LessThanOrEqualTo(panel.GlobalPosition.X + panel.Width + 1),
                    $"{child.GetType().Name} sticks out of the shield column.");
            }

            var battery = WFCockpitLease.Descendants(gunnery).Single(control => control.Name == "WfWeaponBattery");
            var scope = panel.GlobalPosition.X - (battery.GlobalPosition.X + battery.Width);
            Assert.That(scope, Is.GreaterThanOrEqualTo(240), "The fire scope keeps its minimum width between the two columns.");

            gunnery.WfUpdateShields(new WFShipShieldShuntState(false, false, 0f, MathF.PI / 2f, 0f, MathF.PI / 2f));
            Assert.That(panel.Visible, Is.False, "Losing the generator takes the column away again.");
        });
        await pair.CleanReturnAsync();
    }
}
