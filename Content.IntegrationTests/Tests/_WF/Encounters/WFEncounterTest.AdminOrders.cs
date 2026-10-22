#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._WF.Encounters;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    /// <summary>
    /// From the window an admin has a simulated extorter go after a chosen player ship, a ship come to him and hold, a
    /// simulated ship spawn at once, and a kept encounter go away; the player ship shows among the targets.
    /// </summary>
    [Test]
    public async Task AdminOrdersSendAnExtorterAfterAChosenShip()
    {
        await WithSimulation(async () =>
        {
            var config = Server.ResolveDependency<IConfigurationManager>();
            EntityUid extorter = default, hauler = default, grid = default;
            EntityUid? board = null;
            await Server.WaitAssertion(() =>
            {
                // Nothing spawns by itself: every hull here is the admin's doing.
                config.SetCVar(EncountersCVars.MaterializeRange, 50f);
                board = EnsureBoard();
                grid = PutPlayerOnGrid(new Vector2(-20000, -20000));
                extorter = SpawnBountyEncounter("WFTestSimExtorter", new Vector2(-12000, -20000));
                hauler = SpawnBountyEncounter("WFTestSimHauler", new Vector2(-14000, -20000));
                var admin = Server.System<WFEncounterAdminSystem>();
                var state = admin.BuildState(ServerSession);
                Assert.That(state.Targets.Select(target => target.Grid), Does.Contain(SEntMan.GetNetEntity(grid)), "The player's ship is a target.");
                var ship = state.Encounters.Single(entry => entry.Uid == SEntMan.GetNetEntity(extorter)).Ships.Single();
                Assert.That(ship.Extorter && ship.Virtual, Is.True);

                Assert.That(admin.Handle(new WFEncounterAdminRequest
                {
                    Action = WFEncounterAdminAction.Order, Target = SEntMan.GetNetEntity(hauler), Key = "ship",
                    Order = WFEncounterAdminOrder.Extort, OrderTarget = SEntMan.GetNetEntity(grid),
                }, ServerSession), Is.EqualTo(Server.ResolveDependency<Robust.Shared.Localization.ILocalizationManager>().GetString("wf-encounter-admin-order-not-extorter")), "A hauler can't extort.");
                Assert.That(admin.Handle(new WFEncounterAdminRequest
                {
                    Action = WFEncounterAdminAction.Order, Target = SEntMan.GetNetEntity(extorter), Key = "ship",
                    Order = WFEncounterAdminOrder.Extort,
                }, ServerSession), Is.EqualTo(Server.ResolveDependency<Robust.Shared.Localization.ILocalizationManager>().GetString("wf-encounter-admin-order-no-target")), "Extorting needs a target.");
                admin.Handle(new WFEncounterAdminRequest
                {
                    Action = WFEncounterAdminAction.Order, Target = SEntMan.GetNetEntity(extorter), Key = "ship",
                    Order = WFEncounterAdminOrder.Extort, OrderTarget = SEntMan.GetNetEntity(grid),
                }, ServerSession);
                var extort = Ship(extorter).Extort!;
                Assert.That(extort.Phase, Is.EqualTo(WFExtortPhase.Close), "Ordered, it goes after its mark.");
                Assert.That(extort.Mark, Is.EqualTo(grid));
                Assert.That(Server.System<WFCrewObjectiveSystem>().QueueStatus(Ship(extorter).Grid, Ship(extorter).Group), Is.Not.Null);
            });

            // Simulated, it closes on its mark.
            float before = default;
            await Server.WaitPost(() => before = Distance(Ship(extorter).Grid, grid));
            await RunTicks(60 * 10);
            await Server.WaitAssertion(() =>
            {
                Assert.That(Distance(Ship(extorter).Grid, grid), Is.LessThan(before - 50f), "It closes on the ship it was set on.");
                Assert.That(Ship(extorter).Extort!.Mark, Is.EqualTo(grid), "And keeps it as its mark.");

                var admin = Server.System<WFEncounterAdminSystem>();
                Assert.That(admin.Handle(new WFEncounterAdminRequest
                {
                    Action = WFEncounterAdminAction.Order, Target = SEntMan.GetNetEntity(hauler), Key = "ship", Order = WFEncounterAdminOrder.SpawnNow,
                }, ServerSession), Does.Not.Contain("could not"));
            });
            await Until(() => !Ship(hauler).Virtual && !Ship(hauler).Building, 20, "Spawn now builds the simulated hauler's hull.");
            await Server.WaitAssertion(() =>
            {
                var admin = Server.System<WFEncounterAdminSystem>();
                admin.Handle(new WFEncounterAdminRequest
                {
                    Action = WFEncounterAdminAction.Order, Target = SEntMan.GetNetEntity(hauler), Key = "ship", Order = WFEncounterAdminOrder.Hold,
                }, ServerSession);
                var row = Server.System<WFCrewObjectiveSystem>().Snapshot()
                    .First(crew => crew.Grid == SEntMan.GetNetEntity(Ship(hauler).Grid) && crew.Group == Ship(hauler).Group);
                Assert.That(row.Objectives.Single().Kind, Is.EqualTo(Content.Shared._WF.NpcCrew.WFCrewObjectiveKind.Hold), "Hold leaves it one order: hold.");

                SEntMan.GetComponent<WFEncounterComponent>(hauler).Pinned = true;
                admin.Handle(new WFEncounterAdminRequest
                {
                    Action = WFEncounterAdminAction.Order, Target = SEntMan.GetNetEntity(hauler), Key = "ship", Order = WFEncounterAdminOrder.SendAway,
                }, ServerSession);
                var comp = SEntMan.GetComponent<WFEncounterComponent>(hauler);
                Assert.That(comp.Pinned, Is.False, "Sent away, a kept encounter is no longer kept.");
                Assert.That(comp.Resolution, Is.EqualTo(WFEncounterResolution.Completed));
                Assert.That(comp.JumpAt, Is.Not.Null);

                Server.System<WFEncounterSystem>().End(extorter);
                SendPlayerHome(grid);
                if (board is { } spawned)
                    SEntMan.DeleteEntity(spawned);
                config.SetCVar(EncountersCVars.MaterializeRange, EncountersCVars.MaterializeRange.DefaultValue);
            });
            await Until(() => !SEntMan.EntityExists(hauler), 30, "Nobody aboard, it jumps out and is gone.");
        });
    }

    /// <summary>The window shows a state's encounters, opens the one picked with its ships and order panel, and offers extort for an extorter.</summary>
    [Test]
    public async Task EncounterWindowShowsThePickedEncounterAndItsOrders()
    {
        var encounter = new NetEntity(424242);
        var state = new WFEncounterAdminState
        {
            Encounters =
            {
                new WFEncounterAdminEntry
                {
                    Uid = encounter, Prototype = "WFTestSimExtorter", Name = "Raiders AB-123", State = "Active", Threat = 2, Age = 90f,
                    Ships =
                    {
                        new WFEncounterAdminShip { Key = "ship", Name = "Dredger AB-123", Side = "pirates", Exists = true, Virtual = true, Extorter = true },
                    },
                },
            },
            Targets = { new WFEncounterAdminTarget { Grid = new NetEntity(434343), Name = "Player Ship", Players = "Someone", Distance = 2500f } },
        };

        await Client.WaitAssertion(() =>
        {
            var window = new WFEncounterWindow();
            window.OpenCentered();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(WFEncounterWindow).GetMethod("Receive", flags)!.Invoke(window, new object[] { state });
            typeof(WFEncounterWindow).GetField("_selected", flags)!.SetValue(window, (NetEntity?) encounter);
            typeof(WFEncounterWindow).GetMethod("Redraw", flags)!.Invoke(window, null);

            var orders = (List<WFEncounterAdminOrder>) typeof(WFEncounterWindow).GetField("_orders", flags)!.GetValue(window)!;
            Assert.That(orders, Does.Contain(WFEncounterAdminOrder.Extort), "An extorter can be told to extort.");
            Assert.That(orders, Does.Contain(WFEncounterAdminOrder.SpawnNow), "A simulated ship can be spawned at once.");
            Assert.That(orders, Does.Not.Contain(WFEncounterAdminOrder.Board), "It is no boarder.");
            var targets = (List<NetEntity>) typeof(WFEncounterWindow).GetField("_targetGrids", flags)!.GetValue(window)!;
            Assert.That(targets, Has.Count.EqualTo(1));
            var detail = (Robust.Client.UserInterface.Controls.BoxContainer) typeof(WFEncounterWindow).GetField("_detail", flags)!.GetValue(window)!;
            Assert.That(detail.ChildCount, Is.GreaterThan(3), "The picked encounter is shown.");
            window.Close();
        });
    }

    private float Distance(EntityUid a, EntityUid b)
    {
        var transform = Server.System<SharedTransformSystem>();
        return (transform.GetWorldPosition(a) - transform.GetWorldPosition(b)).Length();
    }
}
