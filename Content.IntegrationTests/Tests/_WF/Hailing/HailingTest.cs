#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.Cockpit;
using Content.Client._WF.Hailing;
using Content.Client.Shuttles.UI;
using Content.Client.UserInterface.Screens;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WF.Hailing;
using Content.Server._WF.ShipPa;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Hailing;
using Content.Shared._WF.ShipPa;
using Content.Shared.Buckle;
using Content.Shared.CCVar;
using Content.Shared.IdentityManagement;
using Content.Shared.Shuttles.Components;
using Content.Shared.Verbs;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Hailing;

/// <summary>
/// Ship-to-ship hailing: one console rings another ship's, an answer opens a channel with the other
/// pilot sent to each client, and every way out of a hail leaves both consoles idle.
/// </summary>
public sealed class HailingTest : InteractionTest
{
    // The cockpit needs a pilot who can buckle in and has the usual HUD parts.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: InteractionTestMob
  id: HailingTestMob
  components:
  - type: ContentEye
  - type: Sprite
    sprite: Mobs/Species/Human/parts.rsi
    layers:
    - state: torso_m
  - type: Buckle
  - type: Alerts
  - type: Inventory
  - type: InventorySlots
  - type: ContainerContainer
";

    protected override string PlayerPrototype => "HailingTestMob";

    /// <summary>Far outside the player's view, well inside hailing range.</summary>
    private static readonly Vector2 OtherShipPosition = new(300f, 0f);

    private EntityUid _console;
    private EntityUid _otherGrid;
    private EntityUid _otherConsole;
    private EntityUid _otherPilot;

    [Test]
    public async Task AnsweredHailOpensAChannel()
    {
        // Test pairs run without PVS, which would send the other pilot regardless; TestPair reverts this.
        await Server.WaitPost(() => Server.CfgMan.SetCVar(CVars.NetPVS, true));
        await SetupShips();
        var hailing = SEntMan.System<HailingSystem>();
        var otherPilot = SEntMan.GetNetEntity(_otherPilot);
        var otherShip = string.Empty;
        await Server.WaitPost(() => otherShip = SEntMan.GetComponent<MetaDataComponent>(_otherGrid).EntityName);

        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.TryGetEntity(otherPilot, out _), Is.False,
                "The other pilot should start outside the player's view, or the test proves nothing.");
        });

        UIBox2 contents = default;

        await Client.WaitAssertion(() =>
        {
            var helm = Helm();
            Assert.That(helm.FindControl<HailKey>("HailButton").Text, Is.EqualTo(Loc.GetString("hail-console-button")));
            contents = Box(helm.FindControl<Control>("Contents"));
        });

        await Server.WaitAssertion(() =>
        {
            var targets = hailing.GetTargets(_console);
            Assert.That(targets.Select(t => t.Ship), Is.EquivalentTo(new[] { SEntMan.GetNetEntity(_otherGrid) }));
            Assert.That(targets[0].Busy, Is.False);
            Assert.That(targets[0].Distance, Is.EqualTo(OtherShipPosition.X).Within(5f));

            // Through the console's own message, so the subscription is covered too.
            Raise(_console, new HailRequestMessage(SEntMan.GetNetEntity(_otherGrid)), SPlayer);

            Assert.That(State(_console), Is.EqualTo(HailState.Calling));
            Assert.That(State(_otherConsole), Is.EqualTo(HailState.Ringing));
            var back = hailing.GetTargets(_otherConsole);
            Assert.That(back, Has.Count.EqualTo(1));
            Assert.That(back[0].Busy, Is.True, "A ship whose only console is on a hail should show as busy.");
        });

        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            var console = CEntMan.GetEntity(SEntMan.GetNetEntity(_console));
            Assert.That(CEntMan.TryGetComponent(console, out HailConsoleComponent? hail), "The hail should reach the client.");
            Assert.That(hail!.State, Is.EqualTo(HailState.Calling));

            // The tab key is the windowed console's hail lamp, and it lights without moving anything.
            var helm = Helm();
            Assert.That(helm.FindControl<HailKey>("HailButton").Text, Is.EqualTo(Loc.GetString("hail-key-calling")));
            Assert.That(Box(helm.FindControl<Control>("Contents")), Is.EqualTo(contents), "A hail must not move the console's pages.");
        });

        await Server.WaitAssertion(() =>
        {
            Raise(_otherConsole, new HailAnswerMessage(true), _otherPilot);

            var own = SEntMan.GetComponent<HailConsoleComponent>(_console);
            var other = SEntMan.GetComponent<HailConsoleComponent>(_otherConsole);
            Assert.That(own.State, Is.EqualTo(HailState.Connected));
            Assert.That(other.State, Is.EqualTo(HailState.Connected));
            Assert.That(own.Peer, Is.EqualTo(_otherConsole));
            Assert.That(other.Peer, Is.EqualTo(_console));
            Assert.That(own.PeerOperator, Is.EqualTo(otherPilot));
            Assert.That(other.PeerOperator, Is.EqualTo(SEntMan.GetNetEntity(SPlayer)));
            Assert.That(own.PeerName, Is.Not.Empty);

            // Someone else looking at the console can't hang up a call that isn't theirs.
            Raise(_console, new HailEndMessage(), _otherPilot);
            Assert.That(own.State, Is.EqualTo(HailState.Connected), "Only the person on the call may close it.");

            Raise(_console, new HailSayMessage("  Heave to and\nprepare to be boarded.  "), SPlayer);
            Assert.That(hailing.Say(_otherConsole, _otherPilot, "No."), Is.True);
            Assert.That(hailing.Say(_otherConsole, SPlayer, "Someone else's console."), Is.False,
                "Only the person on the call can transmit from a console.");

            Assert.That(own.Log.Select(l => (l.Text, l.Own)), Is.EqualTo(new[]
            {
                ("Heave to and prepare to be boarded.", true),
                ("No.", false),
            }));

            Assert.That(other.Log.Select(l => (l.Text, l.Own)), Is.EqualTo(new[]
            {
                ("Heave to and prepare to be boarded.", false),
                ("No.", true),
            }));
        });

        await RunTicks(15);

        await Client.WaitAssertion(() =>
        {
            var console = CEntMan.GetEntity(SEntMan.GetNetEntity(_console));
            var hail = CEntMan.GetComponent<HailConsoleComponent>(console);
            Assert.That(hail.State, Is.EqualTo(HailState.Connected));
            Assert.That(hail.Log, Has.Count.EqualTo(2));
            Assert.That(hail.PeerOperator, Is.EqualTo(otherPilot));

            Assert.That(CEntMan.TryGetEntity(otherPilot, out var pilot), "The other pilot should be sent for the screen.");
            Assert.That(CEntMan.HasComponent<SpriteComponent>(pilot), "The screen draws the pilot's sprite.");
            Assert.That(CEntMan.GetComponent<MetaDataComponent>(pilot!.Value).Flags & MetaDataFlags.Detached,
                Is.EqualTo(MetaDataFlags.None), "The other pilot should stay in view for the whole call.");

            var window = FindHailWindow();
            Assert.That(window, Is.Not.Null, "An answered hail should bring the hail window up.");
            Assert.That(window!.FindControl<Label>("StatusLabel").Text,
                Is.EqualTo(Loc.GetString("hail-banner-open", ("ship", otherShip))));
            Assert.That(window.FindControl<LineEdit>("ChatEntry").Editable, "The player took the call, so they can transmit.");
            Assert.That(window.FindControl<Control>("ChatRow").Visible);
        });

        await Server.WaitAssertion(() =>
        {
            Raise(_otherConsole, new HailEndMessage(), _otherPilot);
            Assert.That(State(_console), Is.Null);
            Assert.That(State(_otherConsole), Is.Null);
        });

        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            var console = CEntMan.GetEntity(SEntMan.GetNetEntity(_console));
            Assert.That(CEntMan.HasComponent<HailConsoleComponent>(console), Is.False, "The client should see the channel close.");

            var window = FindHailWindow();
            Assert.That(window, Is.Not.Null, "The window should stay up with the finished call's log.");
            Assert.That(window!.FindControl<Label>("StatusLabel").Text,
                Is.EqualTo(Loc.GetString("hail-ended-peer-closed", ("ship", otherShip))));
            Assert.That(window.FindControl<Control>("ChatRow").Visible, Is.False);

            var helm = Helm();
            Assert.That(helm.FindControl<HailKey>("HailButton").Text, Is.EqualTo(Loc.GetString("hail-key-closed")),
                "The key should say for a moment that the call is over.");
        });
    }

    [Test]
    public async Task UnansweredHailsEnd()
    {
        await SetupShips();
        var hailing = SEntMan.System<HailingSystem>();
        var cfg = Server.ResolveDependency<IConfigurationManager>();
        var timeout = cfg.GetCVar(HailingCVars.RingTimeout);
        var retry = cfg.GetCVar(HailingCVars.RetryDelay);

        try
        {
            await Server.WaitAssertion(() =>
            {
                Assert.That(hailing.TryHail(_console, SPlayer, _otherGrid), Is.True);
                hailing.Answer(_otherConsole, _otherPilot, false);
                Assert.That(State(_console), Is.Null, "A declined hail should stop.");
                Assert.That(State(_otherConsole), Is.Null);

                Assert.That(hailing.TryHail(_console, SPlayer, _otherGrid), Is.False,
                    "A declined caller should have to wait before ringing again.");
                Assert.That(State(_otherConsole), Is.Null);

                cfg.SetCVar(HailingCVars.RingTimeout, 1f);
            });

            await RunSeconds(retry + 1f);

            await Server.WaitAssertion(() =>
            {
                Assert.That(hailing.TryHail(_console, SPlayer, _otherGrid), Is.True);
                Assert.That(State(_otherConsole), Is.EqualTo(HailState.Ringing));
            });

            await RunSeconds(2.5f);

            await Server.WaitAssertion(() =>
            {
                Assert.That(State(_console), Is.Null, "An unanswered hail should give up.");
                Assert.That(State(_otherConsole), Is.Null);
                cfg.SetCVar(HailingCVars.RingTimeout, timeout);
            });

            await RunSeconds(retry + 1f);

            // The caller walking away from the helm cancels a hail that is still ringing.
            await Server.WaitAssertion(() =>
            {
                Assert.That(hailing.TryHail(_console, SPlayer, _otherGrid), Is.True);
                SEntMan.System<SharedUserInterfaceSystem>().CloseUi(_console, ShuttleConsoleUiKey.Key, SPlayer);
            });

            await RunSeconds(1.5f);

            await Server.WaitAssertion(() =>
            {
                Assert.That(State(_console), Is.Null);
                Assert.That(State(_otherConsole), Is.Null);
            });
        }
        finally
        {
            await Server.WaitPost(() => cfg.SetCVar(HailingCVars.RingTimeout, timeout));
        }
    }

    [Test]
    public async Task CallsDropWhenAnEndGoesAway()
    {
        await SetupShips();
        var hailing = SEntMan.System<HailingSystem>();
        var ui = SEntMan.System<SharedUserInterfaceSystem>();

        // The other pilot leaves the helm.
        await Connect(hailing);
        await Server.WaitPost(() => ui.CloseUi(_otherConsole, ShuttleConsoleUiKey.Key, _otherPilot));
        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(State(_console), Is.Null, "A call should end when the other pilot leaves the console.");
            Assert.That(State(_otherConsole), Is.Null);
            ui.OpenUi(_otherConsole, ShuttleConsoleUiKey.Key, _otherPilot);
        });

        // The other console loses power.
        await RunTicks(5);
        await Connect(hailing);
        // Computers ride out a plain power cut on their backup battery, so switch this one off outright.
        await Server.WaitPost(() => SEntMan.GetComponent<ApcPowerReceiverComponent>(_otherConsole).PowerDisabled = true);
        await RunSeconds(2f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(State(_console), Is.Null, "A call should end when a console loses power.");
            Assert.That(State(_otherConsole), Is.Null);
            Assert.That(hailing.GetTargets(_console), Is.Empty, "A ship with no working console can't be hailed.");
            var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(_otherConsole);
            power.PowerDisabled = false;
            power.Powered = true;
            ui.OpenUi(_otherConsole, ShuttleConsoleUiKey.Key, _otherPilot);
            ui.OpenUi(_console, ShuttleConsoleUiKey.Key, SPlayer);
        });

        // The other ship is destroyed.
        await RunTicks(5);
        await Connect(hailing);
        await Server.WaitPost(() => SEntMan.DeleteEntity(_otherGrid));
        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(State(_console), Is.Null, "A call should end when the other ship is gone.");
        });
    }

    [Test]
    public async Task HiddenShipsCannotBeHailed()
    {
        await SetupShips();
        var hailing = SEntMan.System<HailingSystem>();
        var shuttles = SEntMan.System<ShuttleSystem>();

        await Server.WaitAssertion(() =>
        {
            shuttles.AddIFFFlag(_otherGrid, IFFFlags.Hide);
            Assert.That(hailing.GetTargets(_console), Is.Empty, "A cloaked ship should not be listed.");
            Assert.That(hailing.TryHail(_console, SPlayer, _otherGrid), Is.False);
            Assert.That(State(_otherConsole), Is.Null);

            shuttles.RemoveIFFFlag(_otherGrid, IFFFlags.Hide);
            shuttles.AddIFFFlag(_otherGrid, IFFFlags.HideLabelAlways);
            var targets = hailing.GetTargets(_console);
            Assert.That(targets, Has.Count.EqualTo(1));
            Assert.That(targets[0].Name, Is.EqualTo(Loc.GetString("hail-unknown-vessel")),
                "A ship hiding its label should stay unnamed on the list.");

            // And it is told who is calling only as far as its own radar knows.
            shuttles.AddIFFFlag(MapData.Grid, IFFFlags.HideLabelAlways);
            Assert.That(hailing.TryHail(_console, SPlayer, _otherGrid), Is.True);
            Assert.That(SEntMan.GetComponent<HailConsoleComponent>(_otherConsole).PeerShip,
                Is.EqualTo(Loc.GetString("hail-unknown-vessel")));
        });
    }

    [Test]
    public async Task HailIsAnnouncedOverThePa()
    {
        await SetupShips();
        var hailing = SEntMan.System<HailingSystem>();
        var pa = SEntMan.System<ShipPaSystem>();

        await Server.WaitPost(() =>
        {
            var speaker = SEntMan.SpawnEntity("WFWallmountShipPaSpeaker", new EntityCoordinates(_otherGrid, new Vector2(0.5f, 1.5f)));
            var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(speaker);
            power.NeedsPower = false;
            power.Powered = true;
        });

        await RunTicks(20);

        await Server.WaitAssertion(() =>
        {
            Assert.That(pa.CountSpeakers(_otherGrid).Online, Is.EqualTo(1), "The hailed ship needs a working speaker.");
            Assert.That(hailing.TryHail(_console, SPlayer, _otherGrid), Is.True);

            var caller = SEntMan.GetComponent<MetaDataComponent>(MapData.Grid).EntityName;
            var caption = Loc.GetString("hail-pa-incoming", ("ship", caller));
            Assert.That(SEntMan.GetComponent<ShipPaBroadcastComponent>(_otherGrid).Broadcasts.Exists(b => b.Caption == caption),
                "The hailed ship's PA should announce the hail.");
            Assert.That(!SEntMan.TryGetComponent(MapData.Grid, out ShipPaBroadcastComponent? own) || own.Broadcasts.Count == 0,
                "The caller's own PA has nothing to say.");
        });

        // Hanging up and ringing straight back must not read it out again.
        await Server.WaitPost(() => hailing.End(_console));
        await RunSeconds(3f);

        await Server.WaitAssertion(() =>
        {
            var before = SEntMan.GetComponent<ShipPaBroadcastComponent>(_otherGrid).Broadcasts.Select(b => b.Id).ToList();
            Assert.That(hailing.TryHail(_console, SPlayer, _otherGrid), Is.True);
            Assert.That(SEntMan.GetComponent<ShipPaBroadcastComponent>(_otherGrid).Broadcasts.Select(b => b.Id), Is.EquivalentTo(before),
                "A ship's PA should not announce every hail from a caller that keeps redialling.");
        });
    }

    [Test]
    public async Task CockpitShowsHails()
    {
        var settings = Client.ResolveDependency<IConfigurationManager>();
        var layout = settings.GetCVar(CCVars.UILayout);
        await Client.WaitPost(() => settings.SetCVar(CCVars.UILayout, "Default"));
        await RunTicks(3);

        await SetupShips();
        var hailing = SEntMan.System<HailingSystem>();
        var ui = Client.ResolveDependency<IUserInterfaceManager>();
        var controller = ui.GetUIController<WFCockpitUIController>();
        var ownShip = string.Empty;
        await Server.WaitPost(() => ownShip = SEntMan.GetComponent<MetaDataComponent>(_otherGrid).EntityName);

        await Server.WaitAssertion(() =>
        {
            var chair = SEntMan.SpawnEntity("Chair", new EntityCoordinates(MapData.Grid, new Vector2(0.5f, 0.5f)));
            SEntMan.EnsureComponent<PilotComponent>(SPlayer);
            SEntMan.System<ShuttleConsoleSystem>().AddPilot(_console, SPlayer, SEntMan.GetComponent<ShuttleConsoleComponent>(_console));
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, chair), Is.True);
        });

        await RunTicks(10);

        try
        {
            await Client.WaitAssertion(() =>
            {
                var window = Helm();
                Assert.That(controller.Enter(window), Is.True, "The seated pilot should get into the cockpit.");
            });

            // The strip is on the communications caption row from the start, so a hail has nothing to push aside.
            UIBox2 comms = default, chat = default;

            await RunTicks(5);

            await Client.WaitAssertion(() =>
            {
                var hud = ((InGameScreen) ui.ActiveScreen!).Children.OfType<WFCockpitView>().Single();
                var strip = WFCockpitLease.Descendants(hud).OfType<HailStrip>().SingleOrDefault();
                Assert.That(strip, Is.Not.Null, "The cockpit should carry the hail strip.");
                Assert.That(Ancestors(strip!).Any(control => control.Name == "CockpitComms"),
                    "It belongs in the communications housing.");
                Assert.That(strip!.VisibleInTree);
                Assert.That(Named<Label>(strip, "HailStripStatus").Text, Is.Empty, "An idle strip is just its key.");
                AssertKey(strip, "HailStripPrimary", "hail-strip-hail", ui);
                Assert.That(Named<Button>(strip, "HailStripSecondary").Visible, Is.False);

                // Chat is what the housing is for: the strip may not cost it a pixel either way.
                var row = strip.Parent!;
                var caption = row.GetChild(0);
                var box = ((InGameScreen) ui.ActiveScreen!).ChatBox;
                Assert.That(row.Height, Is.EqualTo(caption.DesiredSize.Y).Within(0.5f), "The caption row must stay as tall as its caption.");
                Assert.That(box.Width, Is.EqualTo(row.Width).Within(0.5f), "The chat keeps the housing's full width.");
                Assert.That(box.GlobalPosition.Y - (row.GlobalPosition.Y + row.Height), Is.LessThanOrEqualTo(7f),
                    "Nothing sits between the caption row and the chat.");

                comms = Box(Ancestors(strip).First(control => control.Name == "CockpitComms"));
                chat = Box(box);
            });

            await Server.WaitAssertion(() => Assert.That(hailing.TryHail(_otherConsole, _otherPilot, MapData.Grid), Is.True));
            await RunTicks(20);

            await Client.WaitAssertion(() =>
            {
                var hud = ((InGameScreen) ui.ActiveScreen!).Children.OfType<WFCockpitView>().Single();
                var strip = WFCockpitLease.Descendants(hud).OfType<HailStrip>().Single();
                Assert.That(Named<Label>(strip, "HailStripStatus").Text,
                    Is.EqualTo(Loc.GetString("hail-strip-incoming", ("ship", ownShip))), "An incoming hail should show on the strip.");
                AssertKey(strip, "HailStripPrimary", "hail-strip-answer", ui);
                AssertKey(strip, "HailStripSecondary", "hail-strip-decline", ui);

                Assert.That(Box(Ancestors(strip).First(control => control.Name == "CockpitComms")), Is.EqualTo(comms),
                    "A hail must not move the communications housing.");
                Assert.That(Box(((InGameScreen) ui.ActiveScreen!).ChatBox), Is.EqualTo(chat), "Nor the chat.");
            });

            await Server.WaitPost(() => hailing.Answer(_console, SPlayer, true));
            await RunTicks(15);

            await Client.WaitAssertion(() =>
            {
                Assert.That(FindHailWindow()?.IsOpen, Is.True, "Answering from the cockpit should put the hail on screen.");

                var hud = ((InGameScreen) ui.ActiveScreen!).Children.OfType<WFCockpitView>().Single();
                var strip = WFCockpitLease.Descendants(hud).OfType<HailStrip>().Single();
                Assert.That(Named<Label>(strip, "HailStripStatus").Text, Is.EqualTo(Loc.GetString("hail-strip-open", ("ship", ownShip))));
                AssertKey(strip, "HailStripPrimary", "hail-strip-view", ui);
                AssertKey(strip, "HailStripSecondary", "hail-strip-end", ui);
                Assert.That(Box(((InGameScreen) ui.ActiveScreen!).ChatBox), Is.EqualTo(chat));

                controller.Exit();
                Assert.That(strip.Parent, Is.Null, "Leaving the cockpit should take the strip back out.");
                Assert.That(strip.Disposed, Is.False, "It is kept for the next time.");
                Assert.That(controller.Enter(Helm()), Is.True);
                Assert.That(WFCockpitLease.Descendants(((InGameScreen) ui.ActiveScreen!).Children.OfType<WFCockpitView>().Single())
                    .OfType<HailStrip>().Single(), Is.SameAs(strip), "Going back in should bring the same strip.");
            });
        }
        finally
        {
            await Client.WaitPost(() =>
            {
                controller.Exit();
                settings.SetCVar(CCVars.UILayout, layout);
            });
        }
    }

    [Test]
    public async Task TestShipHailsAnswersAndEchoes()
    {
        await SetupShips();
        var hailing = SEntMan.System<HailingSystem>();
        var console = Server.ResolveDependency<IConsoleHost>();
        EntityUid tester = default;

        await Server.WaitPost(() => console.ExecuteCommand(ServerSession, "hailtest"));
        await RunSeconds(3.5f);

        await Server.WaitAssertion(() =>
        {
            var ships = SEntMan.AllEntityQueryEnumerator<HailTestShipComponent>();
            Assert.That(ships.MoveNext(out tester, out var ship), "The command should bring in a test ship.");
            Assert.That(State(_console), Is.EqualTo(HailState.Ringing), "Its pilot should be hailing the player's ship.");
            Assert.That(SEntMan.GetComponent<HailConsoleComponent>(_console).PeerShip, Is.EqualTo(Loc.GetString("hail-test-ship-name")));
            Assert.That(State(_otherConsole), Is.Null, "Only the ship the admin stands on is hailed.");

            hailing.Answer(_console, SPlayer, true);
            Assert.That(hailing.Say(_console, SPlayer, "ping"), Is.True);
        });

        await RunSeconds(3f);

        await Server.WaitAssertion(() =>
        {
            var heard = SEntMan.GetComponent<HailConsoleComponent>(_console).Log.Where(l => !l.Own).Select(l => l.Text).ToList();
            Assert.That(heard, Does.Contain(Loc.GetString("hail-test-greeting")));
            Assert.That(heard, Does.Contain(Loc.GetString("hail-test-reply", ("text", "ping"))), "The test pilot should send lines back.");

            // And the other way round: it picks up when hailed.
            hailing.End(_console);
            Assert.That(hailing.TryHail(_console, SPlayer, tester), Is.True, "The test ship should be on the hail list.");
        });

        await RunSeconds(3.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(State(_console), Is.EqualTo(HailState.Connected), "The test pilot should answer a hail.");
            console.ExecuteCommand(ServerSession, "hailtest clear");
        });

        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(tester), Is.False, "Clearing should remove the test ship.");
            Assert.That(State(_console), Is.Null, "And the call with it.");
        });
    }

    [Test]
    public async Task AdminHailsAsGod()
    {
        await SetupShips();
        var hailing = SEntMan.System<HailingSystem>();
        var admin = SEntMan.System<AdminHailSystem>();
        var god = string.Empty;
        var otherShip = string.Empty;
        var pilotName = string.Empty;
        EntityUid endpoint = default;

        await Server.WaitAssertion(() =>
        {
            god = Loc.GetString("hail-god-name");
            otherShip = SEntMan.GetComponent<MetaDataComponent>(_otherGrid).EntityName;
            pilotName = Identity.Name(_otherPilot, SEntMan);

            var verbs = SEntMan.System<SharedVerbSystem>().GetLocalVerbs(_otherConsole, SPlayer, typeof(Verb));
            Assert.That(verbs.Any(verb => verb.Text == Loc.GetString("hail-admin-verb")),
                "An admin should get the verb on any shuttle console.");

            Assert.That(admin.Start(ServerSession, _otherConsole), Is.Not.Null);

            var ring = SEntMan.GetComponent<HailConsoleComponent>(_otherConsole);
            Assert.That(ring.State, Is.EqualTo(HailState.Ringing));
            Assert.That(ring.Divine, Is.True);
            Assert.That(ring.PeerShip, Is.EqualTo(god), "The ship is told God is calling, not which admin or ship.");
            Assert.That(State(_console), Is.Null, "The admin's own ship has nothing to do with it.");
            endpoint = ring.Peer!.Value;

            hailing.Answer(_otherConsole, _otherPilot, true);
            Assert.That(ring.State, Is.EqualTo(HailState.Connected));
            Assert.That(ring.PeerName, Is.EqualTo(god));
            Assert.That(ring.PeerOperator, Is.Null, "The pilot is never shown or sent the admin.");
            Assert.That(ring.Viewed, Is.Null);

            var own = SEntMan.GetComponent<HailConsoleComponent>(endpoint);
            Assert.That(own.PeerOperator, Is.EqualTo(SEntMan.GetNetEntity(_otherPilot)), "The admin sees who answered.");

            admin.Say(endpoint, "Be not afraid.");
            Assert.That(hailing.Say(_otherConsole, _otherPilot, "Who is this?"), Is.True);
            Assert.That(ring.Log.Select(l => (l.Sender, l.Text, l.Own)), Is.EqualTo(new[]
            {
                (god, "Be not afraid.", false),
                (pilotName, "Who is this?", true),
            }));
        });

        // God has no ship to drift out of range or helm to walk away from.
        await RunSeconds(2f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(State(_otherConsole), Is.EqualTo(HailState.Connected));
            Assert.That(State(endpoint), Is.EqualTo(HailState.Connected));
        });

        HailWindow window = default!;

        await Client.WaitAssertion(() =>
        {
            window = FindHailWindow()!;
            Assert.That(window, Is.Not.Null, "The admin should get a hail window of their own.");
            Assert.That(window.FindControl<Label>("StatusLabel").Text, Is.EqualTo(Loc.GetString("hail-banner-open", ("ship", otherShip))));
            Assert.That(window.FindControl<LineEdit>("ChatEntry").Editable, "The admin can transmit.");
            Assert.That(window.FindControl<Control>("ContactsBox").Visible, Is.False, "There is no contact list to hail from.");
        });

        // The pilot hangs up: the admin is told, and keeps the window and its log.
        await Server.WaitPost(() => hailing.End(_otherConsole));
        await RunTicks(10);

        await Client.WaitAssertion(() =>
        {
            Assert.That(window.IsOpen);
            Assert.That(window.FindControl<Label>("StatusLabel").Text, Is.EqualTo(Loc.GetString("hail-ended-peer-closed", ("ship", otherShip))));
            Assert.That(window.FindControl<Control>("BackButton").Visible, Is.False);
            window.Close();
        });

        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(endpoint), Is.False, "Closing the window should remove the admin's stand-in.");
            Assert.That(State(_otherConsole), Is.Null);
        });
    }

    private async Task Connect(HailingSystem hailing)
    {
        await Server.WaitAssertion(() =>
        {
            Assert.That(hailing.TryHail(_console, SPlayer, _otherGrid), Is.True);
            hailing.Answer(_otherConsole, _otherPilot, true);
            Assert.That(State(_console), Is.EqualTo(HailState.Connected));
            Assert.That(State(_otherConsole), Is.EqualTo(HailState.Connected));
        });
    }

    private static IEnumerable<Control> Ancestors(Control control)
    {
        for (var parent = control.Parent; parent != null; parent = parent.Parent)
        {
            yield return parent;
        }
    }

    /// <summary>Finds a control built in code by its name; FindControl only knows names from XAML.</summary>
    private static T Named<T>(Control root, string name) where T : Control
    {
        return WFCockpitLease.Descendants(root).OfType<T>().Single(control => control.Name == name);
    }

    private static UIBox2 Box(Control control)
    {
        return UIBox2.FromDimensions(control.GlobalPosition, control.Size);
    }

    /// <summary>A key is showing, says what it should and has room for all of it.</summary>
    private static void AssertKey(Control root, string name, string caption, IUserInterfaceManager ui)
    {
        var key = Named<Button>(root, name);
        Assert.That(key.VisibleInTree, $"{name} should be showing.");
        Assert.That(key.Text, Is.EqualTo(Loc.GetString(caption)));

        var label = key.Label;
        var font = label.FontOverride ?? (label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styled)
            ? styled
            : ui.ThemeDefaults.LabelFont);
        var width = 0f;

        foreach (var rune in label.Text!.EnumerateRunes())
        {
            width += font.GetCharMetrics(rune, label.UIScale)?.Advance ?? 0;
        }

        Assert.That(label.PixelSize.X + 1, Is.GreaterThanOrEqualTo(width), $"{key.Text} must not be cut off.");
    }

    /// <summary>The player's own helm window. A pooled client can hold another test's leftovers.</summary>
    private ShuttleConsoleWindow Helm()
    {
        var console = CEntMan.GetEntity(SEntMan.GetNetEntity(_console));
        return Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<ShuttleConsoleWindow>()
            .Single(window => window.WfCockpitConsole == console);
    }

    private HailWindow? FindHailWindow()
    {
        return Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<HailWindow>().SingleOrDefault();
    }

    private HailState? State(EntityUid console)
    {
        return SEntMan.TryGetComponent(console, out HailConsoleComponent? hail) ? hail.State : null;
    }

    private void Raise(EntityUid console, BoundUserInterfaceMessage message, EntityUid actor)
    {
        message.Actor = actor;
        message.UiKey = ShuttleConsoleUiKey.Key;
        message.Entity = SEntMan.GetNetEntity(console);
        SEntMan.EventBus.RaiseLocalEvent(console, (object) message);
    }

    /// <summary>
    /// Puts a console and the player on the test grid, and a second ship with a console and a pilot far away.
    /// </summary>
    private async Task SetupShips()
    {
        var maps = Server.ResolveDependency<IMapManager>();
        var ui = SEntMan.System<SharedUserInterfaceSystem>();

        await Server.WaitPost(() =>
        {
            Floor(MapData.Grid);
            _console = SpawnConsole(MapData.Grid);

            var other = maps.CreateGridEntity(MapData.MapId);
            _otherGrid = other.Owner;
            Floor(_otherGrid);
            Transform.SetWorldPosition(_otherGrid, OtherShipPosition);
            _otherConsole = SpawnConsole(_otherGrid);
            _otherPilot = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(_otherGrid, new Vector2(0.5f, 0.5f)));
        });

        // A console already open when it first reaches the client is a different engine path from real play.
        await RunTicks(5);

        await Server.WaitPost(() =>
        {
            ui.OpenUi(_console, ShuttleConsoleUiKey.Key, SPlayer);
            ui.OpenUi(_otherConsole, ShuttleConsoleUiKey.Key, _otherPilot);
        });

        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(ui.IsUiOpen(_console, ShuttleConsoleUiKey.Key, SPlayer), "The player needs the console open.");
            Assert.That(ui.IsUiOpen(_otherConsole, ShuttleConsoleUiKey.Key, _otherPilot), "The other pilot needs theirs open.");
        });
    }

    private void Floor(EntityUid grid)
    {
        var tiles = new List<(Vector2i GridIndices, Tile Tile)>();

        for (var x = 0; x < 3; x++)
        {
            for (var y = 0; y < 3; y++)
            {
                tiles.Add((new Vector2i(x, y), new Tile(1)));
            }
        }

        MapSystem.SetTiles(grid, SEntMan.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(grid), tiles);
    }

    private EntityUid SpawnConsole(EntityUid grid)
    {
        var console = SEntMan.SpawnEntity("ComputerShuttle", new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
        // The power update is throttled, so don't wait on it to notice.
        var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(console);
        power.NeedsPower = false;
        power.Powered = true;
        return console;
    }
}
