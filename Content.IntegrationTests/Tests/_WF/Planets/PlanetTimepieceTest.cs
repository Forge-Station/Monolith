#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Content.Client._WF.Planets;
using Content.IntegrationTests.Pair;
using Content.Shared.Clothing.Components;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared._WF.Planets;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Utility;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class PlanetTimepieceTest
{
    private const string TimepieceProto = "WFPlanetTimepiece";
    private static readonly Regex PlanetReport = new("^Asclepiu [0-2][0-9]:[0-5][0-9] \u2014 Clear$");

    [Test]
    public async Task UseAndExamineReportPlanetWhileOffplanetHasNoSignal()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await EnableFeature(pair);

        var layers = await BuildStandalone(pair);
        var outside = await pair.CreateTestMap();
        var user = await AttachViewer(pair, layers[0], Vector2.Zero);
        var server = pair.Server;
        var client = pair.Client;
        var timepiece = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            timepiece = entMan.SpawnEntity(TimepieceProto, entMan.GetComponent<TransformComponent>(user).Coordinates);
            Assert.That(entMan.GetComponent<ClothingComponent>(timepiece).QuickEquip, Is.False,
                "Quick-equip would handle use-in-hand before the timepiece can show its report.");
            Assert.That(entMan.System<SharedHandsSystem>().TryPickupAnyHand(user, timepiece), Is.True,
                "Precondition: the user picked up the timepiece.");
        });

        await pair.RunTicksSync(pair.SecondsToTicks(2f));
        var popups = client.System<WFPlanetTimepiecePopupProbeSystem>();
        var hud = client.System<WFPlanetTimepieceHudSystem>();
        await client.WaitAssertion(() =>
        {
            var panel = new WFPlanetTimepieceHud(client.ResolveDependency<IResourceCache>());
            panel.SetReadout("Asclepiu", "08:00", "Clear");
            panel.Measure(new Vector2(164, 90));
            panel.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(164, 90)));
            var labels = panel.Children.Single().Children.OfType<Label>().ToArray();
            Assert.That(labels.Length, Is.EqualTo(3));
            Assert.That(labels.All(label => label.Width > 100 && label.Height > 0), Is.True,
                "Clipped labels must receive real layout width, not just contain text.");
        });
        await client.WaitAssertion(() =>
            Assert.That(hud.HudVisible, Is.False, "Holding the timepiece must not show its HUD."));
        await client.WaitPost(popups.Received.Clear);
        await server.WaitAssertion(() =>
            Assert.That(server.System<SharedInteractionSystem>().UseInHandInteraction(user, timepiece), Is.True));
        await pair.RunTicksSync(pair.SecondsToTicks(1f));

        await client.WaitAssertion(() =>
            Assert.That(popups.Received.Any(message => PlanetReport.IsMatch(message)), Is.True,
                "Using the timepiece did not show the planet, local HH:mm, and actual clear weather. Received: " + string.Join(" | ", popups.Received)));

        await server.WaitAssertion(() =>
            Assert.That(Examine(server.EntMan, timepiece, user).Split('\n').Any(line => PlanetReport.IsMatch(line.Trim())), Is.True,
                "Examining the timepiece did not show the same planetary report."));

        await server.WaitPost(() => server.System<SharedTransformSystem>()
            .SetCoordinates(user, new EntityCoordinates(outside.MapUid, Vector2.Zero)));
        await pair.RunTicksSync(pair.SecondsToTicks(1f));
        await client.WaitPost(popups.Received.Clear);

        await server.WaitAssertion(() =>
            Assert.That(server.System<SharedInteractionSystem>()
                .UseInHandInteraction(user, timepiece, checkUseDelay: false), Is.True));
        await pair.RunTicksSync(pair.SecondsToTicks(1f));

        await client.WaitAssertion(() =>
            Assert.That(popups.Received, Does.Contain("No planetary signal."),
                "Using the timepiece off-planet did not explicitly reject the signal."));
        await server.WaitAssertion(() =>
            Assert.That(Examine(server.EntMan, timepiece, user), Does.Contain("No planetary signal.")));

        await server.WaitAssertion(() =>
            Assert.That(server.System<InventorySystem>().TryEquip(user, timepiece, "leftarmband", silent: true, force: true),
                Is.True, "The timepiece could not be worn in its declared arm slot."));
        await pair.RunTicksSync(pair.SecondsToTicks(1f));

        await client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(hud.HudVisible, Is.True, "Wearing the timepiece must show its HUD.");
                Assert.That(hud.DisplayedPlanet, Is.EqualTo("No planetary signal."));
                Assert.That(hud.DisplayedTime, Is.EqualTo("--:--"));
                Assert.That(hud.DisplayedWeather, Is.Empty);
                Assert.That(hud.HudWidth, Is.LessThanOrEqualTo(180f), "The worn HUD should remain unobtrusive.");
            });
        });

        await server.WaitPost(() => server.System<SharedTransformSystem>()
            .SetCoordinates(user, new EntityCoordinates(layers[0], Vector2.Zero)));
        await pair.RunTicksSync(pair.SecondsToTicks(2f));

        await client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(hud.HudVisible, Is.True);
                Assert.That(hud.DisplayedPlanet, Is.EqualTo("Asclepiu"));
                Assert.That(hud.DisplayedTime, Does.Match("^[0-2][0-9]:[0-5][0-9]$"));
                Assert.That(hud.DisplayedWeather, Is.EqualTo("Clear"));
            });
        });

        await server.WaitAssertion(() =>
        {
            var inventory = server.System<InventorySystem>();
            Assert.That(inventory.TryUnequip(user, "leftarmband", out var removed, silent: true, force: true), Is.True);
            Assert.That(removed, Is.EqualTo(timepiece));
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(user, timepiece), Is.True);
        });
        await pair.RunTicksSync(pair.SecondsToTicks(1f));
        await client.WaitAssertion(() =>
            Assert.That(hud.HudVisible, Is.False, "Removing the timepiece must hide its HUD."));

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    private static string Examine(IEntityManager entMan, EntityUid timepiece, EntityUid examiner)
    {
        var ev = new ExaminedEvent(new FormattedMessage(), timepiece, examiner, true, false);
        entMan.EventBus.RaiseLocalEvent(timepiece, ev);
        return ev.GetTotalMessage().ToString();
    }
}

/// <summary>Records server-authored timepiece popups received by the connected test client.</summary>
public sealed partial class WFPlanetTimepiecePopupProbeSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;

    public readonly List<string> Received = new();

    public override void Initialize()
    {
        base.Initialize();

        if (!_net.IsClient)
            return;

        SubscribeNetworkEvent<PopupEntityEvent>(ev => Received.Add(ev.Message));
    }
}
