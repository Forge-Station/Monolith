#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.NpcVehicleCrew;
using Content.Shared._WF.NpcVehicleCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.NpcVehicleCrew;

/// <summary>The admin's vehicle crew panel: spawn crews beside you, order one, delete one, clear the rest.</summary>
[TestFixture]
[TestOf(typeof(WFVehicleCrewSystem))]
public sealed class WFVehicleCrewPanelTest
{
    /// <summary>A crew defined by <see cref="WFVehicleCrewTest"/>'s test prototypes.</summary>
    private const string Crew = "WFVehicleCrewTestCrew";

    [Test]
    public async Task ThePanelSpawnsOrdersAndClearsCrews()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var crews = server.System<WFVehicleCrewSystem>();
        var map = await pair.CreateTestMap();
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -4; x <= 30; x++)
            for (var y = -4; y <= 30; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            var admin = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, admin);
        });
        await pair.RunTicksSync(5);

        var states = new List<WFVehicleCrewPanelState>();
        var panel = client.System<Content.Client._WF.NpcVehicleCrew.WFVehicleCrewPanelSystem>();
        void Record(WFVehicleCrewPanelState state) => states.Add(state);
        panel.Received += Record;

        async Task Send(WFVehicleCrewPanelRequest request)
        {
            await client.WaitPost(() => panel.Send(request));
            await pair.RunTicksSync(10);
        }

        try
        {
            await Send(new WFVehicleCrewPanelRequest
            {
                Action = WFVehicleCrewPanelAction.Spawn, Crew = Crew, Behaviour = WFVehicleBehaviour.Hold, Count = 2, Distance = 8,
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(crews.GetCrews(), Has.Count.EqualTo(2), "The panel should spawn two crews.");
                Assert.That(crews.GetCrews().All(crew => crew.Comp.Behaviour == WFVehicleBehaviour.Hold), Is.True);
            });
            Assert.That(states.Last().Error, Is.False, states.Last().Message);
            Assert.That(states.Last().Crews, Has.Count.EqualTo(2), "The client should be told about both crews.");

            // The window itself: its roster lists every crew, and a refresh brings in the two out there.
            Content.Client._WF.NpcVehicleCrew.WFVehicleCrewPanel? window = null;
            await client.WaitPost(() =>
            {
                window = new Content.Client._WF.NpcVehicleCrew.WFVehicleCrewPanel();
                window.OpenCentered();
            });
            await pair.RunTicksSync(10);
            await client.WaitAssertion(() =>
            {
                var text = string.Join("\n", Descendants(window!).OfType<Robust.Client.UserInterface.Controls.Label>().Select(label => label.Text));
                Assert.That(text, Does.Contain(Crew), "The roster doesn't list the test crew.");
                Assert.That(Descendants(window!).OfType<Robust.Client.UserInterface.Controls.Button>()
                    .Count(button => button.Text == Robust.Shared.Localization.Loc.GetString("wf-vehiclecrew-panel-goto")), Is.EqualTo(2),
                    "The window should list both live crews.");
                window!.Close();
            });

            var first = states.Last().Crews[0].Vehicle;
            await Send(new WFVehicleCrewPanelRequest { Action = WFVehicleCrewPanelAction.Order, Vehicle = first, Behaviour = WFVehicleBehaviour.Patrol });
            await server.WaitAssertion(() =>
            {
                var crew = entMan.GetComponent<WFVehicleCrewComponent>(entMan.GetEntity(first));
                Assert.That(crew.Behaviour, Is.EqualTo(WFVehicleBehaviour.Patrol), "The order didn't take.");
            });

            await Send(new WFVehicleCrewPanelRequest { Action = WFVehicleCrewPanelAction.Delete, Vehicle = first });
            await server.WaitAssertion(() => Assert.That(crews.GetCrews(), Has.Count.EqualTo(1), "Delete should take one crew."));

            await Send(new WFVehicleCrewPanelRequest { Action = WFVehicleCrewPanelAction.ClearAll });
            await server.WaitAssertion(() => Assert.That(crews.GetCrews(), Is.Empty, "Clear all should take the rest."));
            Assert.That(states.Last().Crews, Is.Empty);
        }
        finally
        {
            panel.Received -= Record;
        }

        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Robust.Client.UserInterface.Control> Descendants(Robust.Client.UserInterface.Control root)
    {
        foreach (var child in root.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
