using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.AsteroidMining;
using Content.Client._WF.Cockpit;
using Content.Client.Shuttles.UI;
using Content.Server._WF.AsteroidMining;
using Content.Server._WF.Cockpit;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.AsteroidMining;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Cockpit;
using Content.Shared.Access.Components;
using Content.Shared.Buckle;
using Content.Shared.Shuttles.Components;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.AsteroidMining;

/// <summary>Tests the cockpit's mining page, which works the lasers through the helm's gunnery link, and its rock scanner.</summary>
[TestFixture]
public sealed class CockpitMiningTest
{
    private const string Laser = "WFWeaponTurretMiningBeam";

    [Test]
    public async Task LinkedPilotWorksTheLasersThroughTheHelm()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Client.WaitPost(() => pair.Client.CfgMan.SetCVar(WolfgateCVars.CockpitAutoEnter, false));
        var em = pair.Server.EntMan;
        EntityUid actor = default, helm = default, gun = default, laser = default;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x < 3; x++)
            for (var y = 0; y < 3; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            var seatPosition = new EntityCoordinates(map.Grid.Owner, new Vector2(1.5f, 0.5f));
            actor = em.SpawnEntity("MobHuman", seatPosition);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
            var seat = em.SpawnEntity("ChairPilotSeat", seatPosition);
            helm = em.SpawnEntity("ComputerShuttle", new EntityCoordinates(map.Grid.Owner, new Vector2(1.5f, 1.5f)));
            gun = em.SpawnEntity("ComputerGunneryConsole", new EntityCoordinates(map.Grid.Owner, new Vector2(2.5f, 1.5f)));
            var server = em.SpawnEntity("GunneryServerLow", new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 2.5f)));
            laser = em.SpawnEntity(Laser, new EntityCoordinates(map.Grid.Owner, new Vector2(2.5f, 2.5f)));
            foreach (var powered in new[] { helm, gun, server })
                em.GetComponent<ApcPowerReceiverComponent>(powered).Powered = true;
            em.RemoveComponent<AccessReaderComponent>(helm);
            em.RemoveComponent<AccessReaderComponent>(gun);
            em.EnsureComponent<PilotComponent>(actor);
            em.System<ShuttleConsoleSystem>().AddPilot(helm, actor, em.GetComponent<ShuttleConsoleComponent>(helm));
            em.System<SharedUserInterfaceSystem>().OpenUi(helm, ShuttleConsoleUiKey.Key, actor);
            Assert.That(em.System<SharedBuckleSystem>().TryBuckle(actor, actor, seat), Is.True);
        });
        await pair.RunTicksSync(10);

        ShuttleConsoleWindow window = default!;
        await pair.Client.WaitPost(() =>
        {
            window = Descendants(pair.Client.ResolveDependency<IUserInterfaceManager>().WindowRoot)
                .OfType<ShuttleConsoleWindow>().Single();
            // Telemetry alone: the pilot isn't holding the guns.
            window.WfSendCockpitGunnery(new WFCockpitGunnerySessionMessage(true));
        });
        await pair.RunTicksSync(30);

        await pair.Server.WaitAssertion(() =>
        {
            var cockpit = em.System<WFCockpitGunnerySystem>();
            Assert.That(cockpit.GetConsole(actor), Is.EqualTo(gun), "The seated pilot links to the nearby gunnery console.");
            Assert.That(cockpit.Links(), Does.Contain((actor, helm, gun)));
        });
        await pair.Client.WaitAssertion(() =>
        {
            var state = window.WfCockpitMiningState;
            Assert.That(state.Lasers, Is.EqualTo(1), "The helm passes on the linked ship's mining lasers.");
            Assert.That(state.Power, Is.EqualTo(0.5f).Within(0.001f));
            window.WfSendCockpitGunnery(new WFMiningBeamPowerMessage(0.3f));
        });
        await pair.RunTicksSync(30);

        await pair.Server.WaitAssertion(() =>
            Assert.That(em.GetComponent<WFMiningBeamEmitterComponent>(laser).Power, Is.EqualTo(0.3f).Within(0.001f),
                "A power set in the cockpit reaches the ship's lasers."));
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(window.WfCockpitMiningState.Power, Is.EqualTo(0.3f).Within(0.001f), "The new power comes back to the page.");
            window.WfSendCockpitGunnery(new WFCockpitGunnerySessionMessage(false));
        });
        await pair.RunTicksSync(30);

        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(window.WfCockpitMiningState.Lasers, Is.Zero, "Dropping the link clears the page.");
            window.WfSendCockpitGunnery(new WFMiningBeamPowerMessage(0.9f));
        });
        await pair.RunTicksSync(10);

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFMiningBeamEmitterComponent>(laser).Power, Is.EqualTo(0.3f).Within(0.001f),
                "A helm without a gunnery link can't set the lasers.");
            var ui = em.System<SharedUserInterfaceSystem>();
            ui.CloseUi(helm, ShuttleConsoleUiKey.Key, actor);
            foreach (var entity in new[] { helm, gun })
                em.RemoveComponent<ApcPowerReceiverComponent>(entity);
        });
        await pair.RunTicksSync(5);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ScannerReadsWhatARockInViewHolds()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        NetEntity rockNet = default;
        float mass = 0f;
        var ores = new List<float>();
        await pair.Server.WaitPost(() =>
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, em.SpawnEntity("MobHuman", map.GridCoords)));
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            // A loose boulder: the scanner reads it the same way as a rock on its own asteroid grid.
            var mapUid = em.System<SharedMapSystem>().GetMap(map.MapId);
            var rock = em.SpawnEntity("WFAsteroidBoulder", new EntityCoordinates(mapUid, new Vector2(8f, 0f)));
            em.System<WFAsteroidSystem>().Roll(rock, "WFAsteroidRock");
            var comp = em.GetComponent<WFAsteroidComponent>(rock);
            rockNet = em.GetNetEntity(rock);
            mass = comp.Mass;
            ores.AddRange(comp.Ores.Select(ore => ore.Fraction));
        });
        await pair.RunTicksSync(30);

        await pair.Client.WaitAssertion(() =>
        {
            var client = pair.Client.EntMan;
            var rock = client.GetEntity(rockNet);
            var xform = client.GetComponent<TransformComponent>(rock);
            var position = client.System<SharedTransformSystem>().GetWorldPosition(xform);
            var reader = new WFAsteroidScanReader(client, pair.Client.ResolveDependency<IPrototypeManager>());
            var reading = new WFAsteroidScanReading();
            Assert.That(reader.TryFind(xform.MapID, position + new Vector2(0.6f, 0f), 0.5f, out var key), Is.True,
                "Pointing at the rock finds it.");
            Assert.That(key, Is.EqualTo(rock), "A loose rock is keyed by itself.");
            Assert.That(reader.TryRead(key, position - new Vector2(10f, 0f), reading), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reading.Resolved, Is.True, "A rock in view range gives a full scan.");
                Assert.That(reading.Mass, Is.EqualTo(mass).Within(0.001f), "The rock's mass reaches the client.");
                Assert.That(reading.Ores.Select(ore => ore.Fraction), Is.EqualTo(ores).Within(0.0001f), "Its ores reach the client.");
                Assert.That(reading.Ores.All(ore => ore.Name.Length > 0), Is.True);
                Assert.That(reading.WindowHigh, Is.GreaterThan(reading.WindowLow));
                Assert.That(reading.Range, Is.EqualTo(10f - reading.Radius).Within(0.01f), "Range runs to the rock's surface.");
                Assert.That(reading.Stability, Is.AnyOf("steady", "drifting", "erratic"));
                Assert.That(reading.Blast, Is.AnyOf("low", "high", "severe"));
            });
            Assert.That(reader.TryFind(xform.MapID, position + new Vector2(30f, 0f), 0.5f, out _), Is.False,
                "Empty space has nothing to scan.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MiningPageShowsOnlyWithLasersAndFitsTheMfd()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var page = new WFCockpitMiningPage();
            Assert.That(page.Available, Is.False, "A ship with no mining laser has no mining page.");

            var rock = new WFMiningBeamTargetEntry("medium asteroid", 34f,
                new[] { new WFMiningBeamOreEntry("iron ore", 0.4f, Color.Brown), new WFMiningBeamOreEntry("gold ore", 0.1f, Color.Gold) },
                0.2f, 0.4f, 0.6f, 0.5f, 0.3f, 0f, 0.9f);
            page.UpdateState(new WFMiningBeamGunneryState(2, 0.7f, 0f, rock));
            Assert.That(page.Available, Is.True);
            var lever = WFCockpitLease.Descendants(page).OfType<WFMiningPowerLever>().Single();
            Assert.That(lever.Power, Is.EqualTo(0.7f).Within(0.001f), "The lever shows the lasers' power.");

            // The narrowest MFD the cockpit lays out.
            var size = new Vector2(260, 520);
            page.Measure(size);
            page.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
            foreach (var child in WFCockpitLease.Descendants(page).Where(control => control != page && control.VisibleInTree))
            {
                Assert.That(child.GlobalPosition.X + child.Width, Is.LessThanOrEqualTo(page.GlobalPosition.X + page.Width + 1),
                    $"{child.GetType().Name} sticks out of the mining page.");
            }

            page.UpdateState(new WFMiningBeamGunneryState(0, 0f, 0f, null));
            Assert.That(page.Available, Is.False, "Losing the lasers takes the page away again.");
        });
        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var nested in Descendants(child))
            yield return nested;
    }
}
