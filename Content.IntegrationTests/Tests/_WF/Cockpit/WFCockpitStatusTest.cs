#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._WF.Cockpit;
using Content.Client.Shuttles.UI;
using Content.Server._Mono.NPC.HTN;
using Content.Server.NPC.HTN;
using Content.Server.Physics.Controllers;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._NF.Shuttles.Events;
using Content.Shared._WF.ShipShields;
using Content.Shared._WF.Shuttles;
using Content.Shared.NPC;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Cockpit;

/// <summary>Checks cockpit lamps distinguish actual operations from requested or stale settings.</summary>
[TestFixture]
public sealed class WFCockpitStatusTest
{
    [Test]
    public void StatusMappingDoesNotInventReadinessOrDeployment()
    {
        Assert.That(WFCockpitStatusReading.Ftl(FTLState.Invalid, WFFtlDriveStatus.Ready).State, Is.EqualTo(WFCockpitLampState.Unavailable));
        Assert.That(WFCockpitStatusReading.Ftl(FTLState.Available, WFFtlDriveStatus.Ready).State, Is.EqualTo(WFCockpitLampState.Good));
        Assert.That(WFCockpitStatusReading.Ftl(FTLState.Travelling, WFFtlDriveStatus.Ready).State, Is.EqualTo(WFCockpitLampState.Active));
        Assert.That(WFCockpitStatusReading.Ftl(FTLState.Cooldown, WFFtlDriveStatus.Ready).State, Is.EqualTo(WFCockpitLampState.Caution));
        foreach (var drive in new[] { WFFtlDriveStatus.Missing, WFFtlDriveStatus.Unpowered })
        {
            Assert.That(WFCockpitStatusReading.Ftl(FTLState.Available, drive).State, Is.EqualTo(WFCockpitLampState.Fault),
                "An idle ship without a powered drive must never read ready.");
            Assert.That(WFCockpitStatusReading.Ftl(FTLState.Travelling, drive).State, Is.EqualTo(WFCockpitLampState.Active),
                "A jump already under way is still reported as it is.");
            Assert.That(WFCockpitStatusReading.Ftl(FTLState.Invalid, drive).State, Is.EqualTo(WFCockpitLampState.Unavailable));
        }
        Assert.That(WFCockpitStatusReading.Ftl(FTLState.Available, WFFtlDriveStatus.Missing).Detail,
            Is.Not.EqualTo(WFCockpitStatusReading.Ftl(FTLState.Available, WFFtlDriveStatus.Unpowered).Detail));
        Assert.That(WFCockpitStatusReading.Autopilot(null).State, Is.EqualTo(WFCockpitLampState.Unavailable));
        Assert.That(WFCockpitStatusReading.Dampening(InertiaDampeningMode.Station, false).State, Is.EqualTo(WFCockpitLampState.Unavailable));
        Assert.That(WFCockpitStatusReading.Dampening(InertiaDampeningMode.Dampen, false).State, Is.EqualTo(WFCockpitLampState.Good));
        Assert.That(WFCockpitStatusReading.Dampening(InertiaDampeningMode.Dampen, true).State, Is.EqualTo(WFCockpitLampState.Off));
        Assert.That(WFCockpitStatusReading.Dampening(InertiaDampeningMode.Anchor, true).State, Is.EqualTo(WFCockpitLampState.Good));
        Assert.That(WFCockpitStatusReading.Docked(null).State, Is.EqualTo(WFCockpitLampState.Unavailable));
        Assert.That(WFCockpitStatusReading.Docked(false).State, Is.EqualTo(WFCockpitLampState.Off));
        Assert.That(WFCockpitStatusReading.Docked(true).State, Is.EqualTo(WFCockpitLampState.Good));
        var shield = new WFShipShieldShuntState(true, false, 0.6f, 0, 0, MathF.PI);
        Assert.That(WFCockpitStatusReading.Shield(shield).State, Is.EqualTo(WFCockpitLampState.Caution),
            "Enabling the emitter is not proof that its field has deployed.");
        shield.Enabled = false;
        Assert.That(WFCockpitStatusReading.Shield(shield).State, Is.EqualTo(WFCockpitLampState.Off));
        shield.Active = true;
        Assert.That(WFCockpitStatusReading.Shield(shield).State, Is.EqualTo(WFCockpitLampState.Good),
            "Until it disappears, an existing field remains the authoritative deployed state.");
        shield.Available = false;
        Assert.That(WFCockpitStatusReading.Shield(shield).State, Is.EqualTo(WFCockpitLampState.Unavailable));
    }

    [Test]
    public async Task AutopilotReportsOnlyRealSteeringAndClosedHelmsReleaseTracking()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var console = entities.SpawnEntity("ComputerShuttle", map.GridCoords);
            var actor = entities.SpawnEntity("MobHuman", map.GridCoords);
            var helms = entities.System<ShuttleConsoleSystem>();
            var htn = entities.GetComponent<HTNComponent>(console);
            var helm = entities.GetComponent<ShuttleConsoleComponent>(console);
            var receiver = entities.GetComponent<ApcPowerReceiverComponent>(console);
            var transform = entities.GetComponent<TransformComponent>(console);
            receiver.Powered = true;
            htn.Enabled = true;
            entities.EnsureComponent<ActiveNPCComponent>(console);
            entities.EnsureComponent<ShuttleComponent>(map.Grid.Owner);
            var inputs = entities.EnsureComponent<PilotedShuttleComponent>(map.Grid.Owner).InputSources;
            Assert.That(transform.Anchored, Is.True);
            Assert.That(helms.GetWfCockpitAutopilotStatus(console), Is.False);
            htn.Blackboard.SetValue(helm.AutopilotTargetKey, new EntityCoordinates(map.Grid.Owner, new Vector2(50, 0)));
            Assert.That(helms.GetWfCockpitAutopilotStatus(console), Is.False, "A selected or stale target must not light the autopilot lamp.");
            var steering = entities.EnsureComponent<ShipSteererComponent>(console);
            steering.Status = ShipSteeringStatus.Moving;
            Assert.That(helms.GetWfCockpitAutopilotStatus(console), Is.False, "Steering must be registered as an actual ship input source.");
            inputs.Add(console);
            Assert.That(helms.GetWfCockpitAutopilotStatus(console), Is.True);
            var ui = entities.System<SharedUserInterfaceSystem>();
            var tracking = new[] { "_wfCockpitAutopilotStates", "_wfCockpitStatusViewers", "_wfShieldHelmStates" }
                .Select(name => (System.Collections.IDictionary) typeof(ShuttleConsoleSystem)
                    .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(helms)!).ToArray();
            ui.OpenUi(console, ShuttleConsoleUiKey.Key, actor);
            Assert.That(helms.GetWfCockpitAutopilotStatus(console), Is.True, "Opening the helm must not reset autopilot steering.");
            helms.Update(0.3f);
            Assert.That(tracking[0].Contains(console), Is.True, "An open helm is tracked for status updates.");
            ui.CloseUi(console, ShuttleConsoleUiKey.Key, actor);
            helms.Update(0.3f);
            Assert.That(tracking[0].Contains(console), Is.False, "Closed helms must release status tracking.");
            Assert.That(tracking[1].Contains(console), Is.False, "Closed helms must release their queued viewers.");
            Assert.That(tracking[2].Contains(console), Is.False, "Closed helms must also release shield status tracking.");
            steering.Status = ShipSteeringStatus.InRange;
            Assert.That(helms.GetWfCockpitAutopilotStatus(console), Is.False, "Arrival must turn the lamp off.");
            steering.Status = ShipSteeringStatus.Moving;
            htn.Enabled = false;
            Assert.That(helms.GetWfCockpitAutopilotStatus(console), Is.False, "Disabled planning must not advertise a stale steering component.");
            htn.Enabled = true;
            receiver.Powered = false;
            Assert.That(helms.GetWfCockpitAutopilotStatus(console), Is.False);
            receiver.Powered = true;
            inputs.Remove(console);
            Assert.That(helms.GetWfCockpitAutopilotStatus(console), Is.False);
            entities.RemoveComponent<ShipSteererComponent>(console);
            Assert.That(helms.GetWfCockpitAutopilotStatus(console), Is.False);
            Assert.That(helms.GetWfCockpitAutopilotStatus(actor), Is.Null, "A console without autopilot support must remain unavailable.");
            entities.DeleteEntity(console);
            entities.DeleteEntity(actor);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AutopilotLampReachesAClientWindowOpenedInTheStatusTick()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        EntityUid console = default, actor = default, observer = default;

        void OpenInStatusTick()
        {
            entities.System<SharedUserInterfaceSystem>().OpenUi(console, ShuttleConsoleUiKey.Key, actor);
            entities.System<ShuttleConsoleSystem>().Update(0.3f);
        }

        async Task AssertAutopilotLamp(WFCockpitLampState expected, string reason)
        {
            await pair.Client.WaitAssertion(() =>
            {
                static IEnumerable<Control> Descendants(Control root)
                {
                    yield return root;
                    foreach (var child in root.Children)
                    foreach (var nested in Descendants(child))
                        yield return nested;
                }

                var window = Descendants(pair.Client.ResolveDependency<IUserInterfaceManager>().WindowRoot)
                    .OfType<ShuttleConsoleWindow>().Last();
                var bank = window.WfCockpitStatus();
                var lamp = bank.Children.OfType<WFCockpitStatusLamp>().Single(control => control.Name == "wf-cockpit-status-autopilot");
                Assert.That(lamp.Reading.State, Is.EqualTo(expected), reason);
                Assert.That(window.FindControl<NavScreen>("NavContainer").Visible, Is.True,
                    "Autopilot telemetry must not open the strategic MFD.");
                bank.Dispose();
            });
        }

        await pair.Server.WaitAssertion(() =>
        {
            console = entities.SpawnEntity("ComputerShuttle", map.GridCoords);
            actor = entities.SpawnEntity("MobHuman", map.GridCoords);
            observer = entities.SpawnEntity("MobHuman", map.GridCoords);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
            var ui = entities.System<SharedUserInterfaceSystem>();
            // The power network must not rebuild the cached helm snapshot while real ticks run.
            entities.RemoveComponent<ApcPowerReceiverComponent>(console);
            Assert.That(ui.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var cached), Is.True);
            Assert.That(cached!.CockpitAutopilotActive, Is.False, "The cached snapshot predates the live status.");
            // Without planning the live status is unavailable, a value only the status messages can deliver.
            entities.RemoveComponent<HTNComponent>(console);
            Assert.That(entities.System<ShuttleConsoleSystem>().GetWfCockpitAutopilotStatus(console), Is.Null);
            // An earlier viewer makes the status known, so the player's open is an unchanged-status refresh.
            ui.OpenUi(console, ShuttleConsoleUiKey.Key, observer);
            entities.System<ShuttleConsoleSystem>().Update(0.3f);
        });
        await pair.RunTicksSync(2);
        await pair.Server.WaitAssertion(OpenInStatusTick);
        await pair.RunTicksSync(40);
        await AssertAutopilotLamp(WFCockpitLampState.Unavailable,
            "A window created in the status tick must still receive the live status on a later tick.");

        await pair.Server.WaitAssertion(() =>
            entities.System<SharedUserInterfaceSystem>().CloseUi(console, ShuttleConsoleUiKey.Key, actor));
        await pair.RunTicksSync(5);
        await pair.Server.WaitAssertion(OpenInStatusTick);
        await pair.RunTicksSync(40);
        await AssertAutopilotLamp(WFCockpitLampState.Unavailable,
            "A reopened window must receive the live status instead of the stale cached snapshot.");

        await pair.Server.WaitAssertion(() =>
        {
            var ui = entities.System<SharedUserInterfaceSystem>();
            ui.CloseUi(console, ShuttleConsoleUiKey.Key, actor);
            ui.CloseUi(console, ShuttleConsoleUiKey.Key, observer);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            foreach (var entity in new[] { console, actor, observer })
                entities.DeleteEntity(entity);
        });
        await pair.CleanReturnAsync();
    }
}
