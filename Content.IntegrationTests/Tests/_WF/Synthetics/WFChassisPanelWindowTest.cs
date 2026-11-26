#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._WF.Synthetics;
using Content.Client._WF.Wolfmed.Overlays;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests._WF.CombatConsole;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Synthetics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WF.Synthetics;

/// <summary>
/// The chassis panel window on the client, fed hand-built states: table rows and columns, localized text, which
/// buttons are live, the confirm-by-second-click controls, the body doll and row selection, and where the bay overlay
/// draws.
/// </summary>
[TestFixture]
public sealed class WFChassisPanelWindowTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly string[] UnresolvedPrefixes = { "wf-chassis-", "wolfmed-synthetic-" };

    private static NetEntity Net(int id) => new(id);

    private static WFChassisPartEntry Part(TargetBodyPart slot, string name, int id, Action<WFChassisPartEntry>? edit = null)
    {
        var entry = new WFChassisPartEntry
        {
            Part = Net(id),
            Slot = slot,
            Name = name,
            Attached = true,
            Working = true,
            Integrity = 0.8f,
            Pain = 0.1f,
            CanDetach = slot != TargetBodyPart.Head && slot != TargetBodyPart.Torso,
        };
        edit?.Invoke(entry);
        return entry;
    }

    /// <summary>Ten parts: two arms, two hands, two legs, a head and torso, a detached leg and an isolated foot.</summary>
    internal static WFChassisPanelState State(bool bayOpen = false, bool overclocked = false, float power = 0.7f)
    {
        var state = new WFChassisPanelState
        {
            BayOpen = bayOpen,
            Overclocked = overclocked,
            Power = power,
            Fluid = 0.9f,
            Sensors = 0.6f,
            CoreTemperature = 330f,
            Status = "wolfmed-synthetic-line-servo",
        };

        state.Parts.Add(Part(TargetBodyPart.Head, "wolfmed-synthetic-part-head", 1));
        state.Parts.Add(Part(TargetBodyPart.Torso, "wolfmed-synthetic-part-torso", 2));
        state.Parts.Add(Part(TargetBodyPart.LeftArm, "wolfmed-synthetic-part-left-arm", 3,
            e => e.Faults.Add("wolfmed-synthetic-line-servo")));
        state.Parts.Add(Part(TargetBodyPart.RightArm, "wolfmed-synthetic-part-right-arm", 4));
        state.Parts.Add(Part(TargetBodyPart.LeftHand, "wolfmed-synthetic-part-left-hand", 5));
        state.Parts.Add(Part(TargetBodyPart.RightHand, "wolfmed-synthetic-part-right-hand", 6,
            e => e.Working = false));
        state.Parts.Add(Part(TargetBodyPart.LeftLeg, "wolfmed-synthetic-part-left-leg", 7));
        state.Parts.Add(new WFChassisPartEntry
        {
            Slot = TargetBodyPart.RightLeg,
            Name = "wolfmed-synthetic-part-right-leg",
        });
        state.Parts.Add(Part(TargetBodyPart.LeftFoot, "wolfmed-synthetic-part-left-foot", 9,
            e => { e.Isolated = true; e.Working = false; }));
        state.Parts.Add(Part(TargetBodyPart.RightFoot, "wolfmed-synthetic-part-right-foot", 10,
            e => { e.Loose = true; e.Faults.Add("wolfmed-synthetic-line-breach"); e.Faults.Add("wolfmed-synthetic-line-dent"); }));

        state.Organs.Add(new WFChassisOrganEntry
        {
            Organ = Net(20), Slot = "eyes", Holder = TargetBodyPart.Head, Name = "optic sensor",
            Health = 0.8f, Band = "wf-chassis-band-normal", CanDisconnect = true,
        });
        state.Organs.Add(new WFChassisOrganEntry
        {
            Organ = Net(21), Slot = "posbrain", Holder = TargetBodyPart.Head, Name = "positronic brain",
            Health = 0.4f, Band = "wf-chassis-band-impaired", Vital = true, CanDisconnect = true,
        });
        state.Organs.Add(new WFChassisOrganEntry { Slot = "pump", Holder = TargetBodyPart.Torso });
        state.Organs.Add(new WFChassisOrganEntry
        {
            Organ = Net(23), Slot = "battery", Holder = TargetBodyPart.Torso, Name = "power cell",
            Health = 0.6f, Band = "wf-chassis-band-charge",
        });
        return state;
    }

    internal static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var nested in Descendants(child))
            yield return nested;
    }

    private static IEnumerable<string> Texts(Control root)
    {
        foreach (var control in Descendants(root))
        {
            switch (control)
            {
                case Label { Text: { } label }:
                    yield return label;
                    break;
                case Button { Text: { } button }:
                    yield return button;
                    break;
            }
        }
    }

    private static void AssertResolved(WFChassisPanelWindow window)
    {
        foreach (var text in Texts(window))
        {
            foreach (var prefix in UnresolvedPrefixes)
                Assert.That(text, Does.Not.Contain(prefix), $"'{text}' is a loc key that did not resolve.");
        }
    }

    /// <summary>Row counts, text and which buttons are live.</summary>
    [Test]
    public async Task RowsTextAndButtonsTest()
    {
        WFChassisPanelWindow? window = null;
        await Client.WaitPost(() =>
        {
            window = new WFChassisPanelWindow();
            window.OpenCentered();
            window.Update(State());
        });
        await RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var panel = window!;
            Assert.That(panel.Parts, Has.Count.EqualTo(10));
            Assert.That(panel.Organs, Has.Count.EqualTo(4));
            AssertResolved(panel);

            Assert.Multiple(() =>
            {
                string[] states =
                {
                    "OK", "OK", "OK", "OK", "OK", "OFFLINE", "OK", "DETACHED", "ISOLATED", "LOOSE",
                };
                for (var i = 0; i < 10; i++)
                    Assert.That(panel.Parts[i].State.Text, Is.EqualTo(states[i]), $"part {i} state tag");

                Assert.That(panel.Parts[0].Name.Text, Is.EqualTo("HEAD"));
                Assert.That(panel.Parts[2].Faults.Text, Is.EqualTo("SERVO DAMAGE"));
                Assert.That(panel.Parts[9].Faults.Text, Is.EqualTo("FLUID LEAK, PANEL DEFORMED"));
                Assert.That(panel.Parts[2].Integrity.Text, Does.Contain("80%"));
                Assert.That(panel.Parts[2].Load.Text, Is.EqualTo("10%"));
                Assert.That(panel.Parts[7].Integrity.Text, Is.EqualTo("--"), "a detached part has no integrity.");

                // ISOLATE / RESTORE only on arm, hand, leg and foot kinds, and only while attached.
                bool[] isolate = { false, false, true, true, true, true, true, false, true, true };
                // DETACH follows CanDetach.
                bool[] detach = { false, false, true, true, true, true, true, false, true, true };
                for (var i = 0; i < 10; i++)
                {
                    Assert.That(!panel.Parts[i].Isolate.Disabled, Is.EqualTo(isolate[i]), $"part {i} isolate enabled");
                    Assert.That(!panel.Parts[i].Detach.Disabled, Is.EqualTo(detach[i]), $"part {i} detach enabled");
                }

                Assert.That(panel.Parts[2].Isolate.Text, Is.EqualTo("ISOLATE"));
                Assert.That(panel.Parts[8].Isolate.Text, Is.EqualTo("RESTORE"));

                Assert.That(panel.Organs[0].Slot.Text, Is.EqualTo("OPTICS"));
                Assert.That(panel.Organs[0].Name.Text, Is.EqualTo("OPTIC SENSOR"));
                Assert.That(panel.Organs[0].Band.Text, Is.EqualTo("NOMINAL"));
                Assert.That(panel.Organs[1].Slot.Text, Is.EqualTo("POSITRONIC CORE"));
                Assert.That(panel.Organs[1].Vital.Text, Is.EqualTo("VITAL"));
                Assert.That(panel.Organs[1].Band.Text, Is.EqualTo("IMPAIRED"));
                Assert.That(panel.Organs[2].Slot.Text, Is.EqualTo("COOLANT PUMP"));
                Assert.That(panel.Organs[2].Name.Text, Is.EqualTo("EMPTY"));
                Assert.That(panel.Organs[2].Disconnect.Disabled, Is.True, "nothing to disconnect in an empty slot.");
                Assert.That(panel.Organs[3].Slot.Text, Is.EqualTo("POWER CELL"));
                Assert.That(panel.Organs[3].Band.Text, Is.EqualTo("CHARGE"));
                Assert.That(panel.Organs[3].Health.Text, Does.Contain("60%"));
                Assert.That(panel.Organs[0].Disconnect.Disabled, Is.False);
                Assert.That(panel.Organs[3].Disconnect.Disabled, Is.True, "CanDisconnect is false while the bay is shut.");

                var gauges = Texts(panel).ToList();
                Assert.That(gauges, Has.Some.StartWith("POWER "));
                Assert.That(gauges, Has.Some.StartWith("FLUID "));
                Assert.That(gauges, Has.Some.StartWith("SENSOR "));
                Assert.That(gauges, Has.Some.EqualTo("CORE 330 K"));
                Assert.That(gauges, Has.Some.EqualTo("SERVO DAMAGE"), "the status line.");
                Assert.That(panel.BayButton is Button { Text: "OPEN BAY" });
            });

            // No cell, no core temperature: those two header gauges go away.
            var noCell = State(power: -1f);
            noCell.CoreTemperature = -1f;
            panel.Update(noCell);
            var shown = Descendants(panel).OfType<Label>().Where(l => l.Visible).Select(l => l.Text ?? string.Empty).ToList();
            Assert.That(shown.Any(text => text.StartsWith("POWER [")), Is.False, "no cell, no POWER gauge.");
            Assert.That(shown.Any(text => text.StartsWith("CORE")), Is.False, "no core temperature, no CORE gauge.");
            Assert.That(shown.Any(text => text.StartsWith("FLUID [")), Is.True);
            panel.Update(State(bayOpen: true));
            Assert.That(panel.Parts, Has.Count.EqualTo(10), "the same count updates in place.");
            Assert.That(((Button) panel.BayButton).Text, Is.EqualTo("CLOSE BAY"));
        });

        await Client.WaitPost(() => window!.Dispose());
    }

    /// <summary>
    /// A changed number of entries rebuilds the rows, and a slot with no loc key shows its raw id. In-place updates
    /// keep the same controls so they do not flicker.
    /// </summary>
    [Test]
    public async Task RowsUpdateInPlaceTest()
    {
        await Client.WaitAssertion(() =>
        {
            using var panel = new WFChassisPanelWindow();
            panel.Update(State());
            var rows = panel.Parts.ToArray();
            var buttons = panel.Parts.Select(r => r.Isolate).ToArray();

            var next = State();
            next.Parts[2].Isolated = true;
            panel.Update(next);
            Assert.That(panel.Parts, Is.EqualTo(rows), "the same count must reuse the rows.");
            Assert.That(panel.Parts.Select(r => r.Isolate), Is.EqualTo(buttons));
            Assert.That(panel.Parts[2].Isolate.Text, Is.EqualTo("RESTORE"));

            next.Organs.Add(new WFChassisOrganEntry { Organ = Net(30), Slot = "custom_slot", Name = "odd part" });
            next.Parts.RemoveAt(9);
            panel.Update(next);
            Assert.That(panel.Parts, Has.Count.EqualTo(9));
            Assert.That(panel.Organs, Has.Count.EqualTo(5));
            Assert.That(panel.Organs[4].Slot.Text, Is.EqualTo("custom_slot"), "an unknown slot falls back to its id.");
            Assert.That(panel.Organs[4].Health.Text, Is.EqualTo("--"), "no health, no gauge.");
            AssertResolved(panel);
        });
    }

    /// <summary>
    /// Every state tag, the load cell, the faults cell, the bands and the controls carry a tip; a fault's tip holds the
    /// readout prototype's advice and, for a wound fault, the analyzer's treatment summary.
    /// </summary>
    [Test]
    public async Task TooltipsTest()
    {
        await Client.WaitAssertion(() =>
        {
            using var panel = new WFChassisPanelWindow();
            panel.Update(State());

            // Wrapped tips are compared on their words.
            string Flat(string? tip) => (tip ?? string.Empty).Replace("\n", " ");

            Assert.Multiple(() =>
            {
                // OK, OFFLINE, ISOLATED, LOOSE and DETACHED are parts 0, 5, 8, 9 and 7.
                var expected = new Dictionary<int, string>
                {
                    [0] = "Running within rating.",
                    [5] = "Too damaged to run.",
                    [8] = "Servos and sensors cut from the panel.",
                    [9] = "Seated by hand.",
                    [7] = "The limb is off the chassis.",
                };
                foreach (var (index, text) in expected)
                {
                    Assert.That(panel.Parts[index].State.ToolTip, Is.Not.Null.And.Not.Empty, $"part {index} state tip");
                    Assert.That(Flat(panel.Parts[index].State.ToolTip), Does.StartWith(text), $"part {index} state tip");
                    Assert.That(panel.Parts[index].State.MouseFilter, Is.Not.EqualTo(Control.MouseFilterMode.Ignore),
                        $"part {index} state cell must take the mouse for its tip");
                }

                foreach (var row in panel.Parts)
                {
                    Assert.That(Flat(row.Load.ToolTip), Does.StartWith("Pain this part's damage sensors"));
                    Assert.That(row.Faults.ToolTip, Is.Not.Null.And.Not.Empty);
                    Assert.That(row.Isolate.ToolTip, Is.Not.Null.And.Not.Empty);
                    Assert.That(row.Detach.ToolTip, Is.Not.Null.And.Not.Empty);
                    foreach (var tip in new[] { row.State.ToolTip, row.Load.ToolTip, row.Faults.ToolTip, row.Isolate.ToolTip, row.Detach.ToolTip })
                        Assert.That(tip, Does.Not.Contain("wf-chassis-"), "a tip that is still a loc key");
                }

                Assert.That(panel.Parts[0].Faults.ToolTip, Is.EqualTo("No faults logged."));
                Assert.That(Flat(panel.Parts[2].Isolate.ToolTip), Does.StartWith("Cuts the limb's servos"));
                Assert.That(Flat(panel.Parts[8].Isolate.ToolTip), Does.StartWith("Powers the limb's servos"));

                // The breach fault: readout line, HUD advice, then the analyzer's treatment summary.
                var breach = Flat(panel.Parts[9].Faults.ToolTip);
                Assert.That(breach, Does.Contain("FLUID LEAK - WELD BREACH"));
                Assert.That(breach, Does.Contain("A hole in the chassis leaking coolant and hydraulic fluid. It never clots."));
                Assert.That(breach, Does.Contain("PANEL DEFORMED"), "the second fault gets its own entry.");
                Assert.That(panel.Parts[9].Faults.ToolTip, Does.Contain("\n"), "one fault per line at least.");

                Assert.That(Flat(panel.Parts[2].Faults.ToolTip), Does.StartWith("SERVO DAMAGE"));

                // A line no readout prototype owns shows the line alone.
                Assert.That(panel.FaultTip("wf-chassis-band-normal"), Is.EqualTo("NOMINAL"));

                Assert.That(Flat(panel.Organs[1].Vital.ToolTip), Is.EqualTo("Pulling this stops the chassis (pump) or ends the mind (core)."));
                Assert.That(panel.Organs[0].Vital.ToolTip, Is.Null);
                foreach (var index in new[] { 0, 1, 3 })
                    Assert.That(panel.Organs[index].Band.ToolTip, Is.Not.Null.And.Not.Empty, $"organ {index} band tip");
                Assert.That(panel.Organs[2].Band.ToolTip, Is.Null, "an empty slot has no band.");
                Assert.That(Flat(panel.Organs[3].Band.ToolTip), Does.StartWith("Charge left in the power cell."));

                Assert.That(Flat(panel.BayButton.ToolTip), Does.StartWith("Opens the torso plating. Takes 3 seconds, 6 on yourself."));
                Assert.That(Flat(panel.RebootControl.ToolTip), Does.Contain("down for 4 seconds"));
                Assert.That(Flat(panel.OverclockControl.ToolTip), Does.Contain("25% faster for 3 times the drain"));
                Assert.That(Flat(panel.Organs[0].Disconnect.ToolTip), Does.StartWith("Pulls the component out"));
            });

            panel.Update(State(bayOpen: true, overclocked: true));
            Assert.That(Flat(panel.BayButton.ToolTip), Does.StartWith("Shuts the torso plating."));
            Assert.That(Flat(panel.OverclockControl.ToolTip), Does.StartWith("Overclock is running"));
        });
    }

    /// <summary>Every control sends its message; the dangerous ones need a second click on the same control.</summary>
    [Test]
    public async Task ControlsSendMessagesTest()
    {
        WFChassisPanelWindow? window = null;
        var isolates = new List<(NetEntity, bool)>();
        var detaches = new List<NetEntity>();
        var disconnects = new List<NetEntity>();
        var bay = 0;
        var reboots = 0;
        var overclocks = new List<bool>();

        await Client.WaitPost(() =>
        {
            window = new WFChassisPanelWindow();
            window.OnIsolate += (part, isolate) => isolates.Add((part, isolate));
            window.OnDetach += detaches.Add;
            window.OnDisconnect += disconnects.Add;
            window.OnToggleBay += () => bay++;
            window.OnReboot += () => reboots++;
            window.OnOverclock += overclocks.Add;
            window.OpenCentered();
            window.Update(State(bayOpen: true));
        });
        await RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var panel = window!;

            WFButtonTestInput.Click(panel.Parts[2].Isolate);
            WFButtonTestInput.Click(panel.Parts[8].Isolate);
            WFButtonTestInput.Click(panel.Parts[0].Isolate);
            Assert.That(isolates, Is.EqualTo(new[] { (Net(3), true), (Net(9), false) }),
                "isolate and restore are sent; a disabled head button sends nothing.");

            WFButtonTestInput.Click(panel.Parts[3].Detach);
            WFButtonTestInput.Click(panel.Parts[1].Detach);
            Assert.That(detaches, Is.EqualTo(new[] { Net(4) }));
        });

        // The component buttons are on the second tab and are only laid out once it is shown.
        await Client.WaitPost(() => window!.CurrentTab = 1);
        await RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var panel = window!;

            // A plain component goes at once; the core asks twice.
            WFButtonTestInput.Click(panel.Organs[0].Disconnect);
            Assert.That(disconnects, Is.EqualTo(new[] { Net(20) }));

            var core = panel.Organs[1].Disconnect;
            WFButtonTestInput.Click(core);
            Assert.That(disconnects, Has.Count.EqualTo(1), "the first click on a vital component only arms it.");
            Assert.That(core.Text, Is.EqualTo("CONFIRM"));

            // Another confirm control takes the arming away.
            WFButtonTestInput.Click(panel.RebootControl);
            Assert.That(reboots, Is.EqualTo(0));
            Assert.That(core.Text, Is.EqualTo("DISCONNECT"), "arming reboot disarms the component.");
            Assert.That(((Button) panel.RebootControl).Text, Is.EqualTo("CONFIRM"));
            WFButtonTestInput.Click(core);
            Assert.That(disconnects, Has.Count.EqualTo(1), "a second click after the arming moved only re-arms it.");

            WFButtonTestInput.Click(core);
            Assert.That(disconnects, Is.EqualTo(new[] { Net(20), Net(21) }));
            Assert.That(core.Text, Is.EqualTo("DISCONNECT"), "confirming disarms.");

            WFButtonTestInput.Click(panel.RebootControl);
            WFButtonTestInput.Click(panel.RebootControl);
            Assert.That(reboots, Is.EqualTo(1));

            WFButtonTestInput.Click(panel.BayButton);
            Assert.That(bay, Is.EqualTo(1));

            WFButtonTestInput.Click(panel.OverclockControl);
            Assert.That(overclocks, Is.EqualTo(new[] { true }));
            Assert.That(panel.OverclockControl.Pressed, Is.True);
            panel.Update(State(bayOpen: true, overclocked: false));
            Assert.That(panel.OverclockControl.Pressed, Is.False, "the toggle follows the server's state.");

            // Arm the core again and let the window lapse.
            WFButtonTestInput.Click(core);
            Assert.That(core.Text, Is.EqualTo("CONFIRM"));
        });

        await Task.Delay(TimeSpan.FromSeconds(WFChassisPanelWindow.ConfirmSeconds + 0.3));
        await RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            var core = window!.Organs[1].Disconnect;
            Assert.That(core.Text, Is.EqualTo("DISCONNECT"), "the arming lapses after the confirm window.");
            WFButtonTestInput.Click(core);
            Assert.That(disconnects, Has.Count.EqualTo(2), "a late second click only re-arms.");
        });

        await Client.WaitPost(() => window!.Dispose());
    }

    /// <summary>A left click on a table cell; the cells take the mouse themselves.</summary>
    internal static void ClickCell(Control cell)
    {
        var local = cell.Size / 2;
        var pointer = new ScreenCoordinates(cell.GlobalPixelPosition + local * cell.UIScale, cell.Window!.Id);
        var args = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, pointer, true,
            local, local * cell.UIScale);
        cell.GetType().GetMethod("KeyBindDown", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .Invoke(cell, new object[] { args });
    }

    internal static Color Background(Control cell) => ((StyleBoxFlat) ((PanelContainer) cell).PanelOverride!).BackgroundColor;

    internal static readonly Color SelectedBackground = Color.FromHex("#4a3508");

    /// <summary>Parts and components are tables: a header row, then one cell per column, lined up under the header.</summary>
    [Test]
    public async Task TablesLineUpTest()
    {
        WFChassisPanelWindow? window = null;
        await Client.WaitPost(() =>
        {
            window = new WFChassisPanelWindow();
            window.OpenCentered();
            window.Update(State());
        });
        await RunTicksSync(3);

        List<GridContainer> grids = null!;
        await Client.WaitAssertion(() =>
        {
            var panel = window!;
            grids = Descendants(panel).OfType<GridContainer>().ToList();
            Assert.That(grids, Has.Count.EqualTo(2), "one grid for parts, one for components.");
            Assert.That(grids[0].Columns, Is.EqualTo(7));
            Assert.That(grids[1].Columns, Is.EqualTo(6));
            Assert.That(grids[0].ChildCount, Is.EqualTo(7 * 11), "a header row and ten part rows.");
            Assert.That(grids[1].ChildCount, Is.EqualTo(6 * 5), "a header row and four component rows.");

            var texts = Texts(panel).ToList();
            foreach (var title in new[] { "PART", "STATE", "INTEGRITY", "LOAD", "FAULTS", "ACTIONS", "SLOT", "COMPONENT", "HEALTH", "STATUS" })
                Assert.That(texts, Has.Some.EqualTo(title), $"column title {title}");

            foreach (var row in panel.Parts)
                Assert.That(row.Cells, Has.Count.EqualTo(7));
            foreach (var row in panel.Organs)
                Assert.That(row.Cells, Has.Count.EqualTo(6));
            Assert.That(Background(panel.Parts[0].Cells[0]), Is.Not.EqualTo(Background(panel.Parts[1].Cells[0])),
                "rows alternate their background.");

            AssertAligned(grids[0]);
        });

        // A hidden tab is not laid out, so the components table is checked once its tab is shown.
        await Client.WaitPost(() => window!.CurrentTab = 1);
        await RunTicksSync(3);
        await Client.WaitAssertion(() => AssertAligned(grids[1]));

        await Client.WaitPost(() => window!.Dispose());
    }

    private static void AssertAligned(GridContainer grid)
    {
        Assert.Multiple(() =>
        {
            var columns = grid.Columns;
            var cells = grid.Children.ToList();
            for (var i = columns; i < cells.Count; i++)
            {
                var header = cells[i % columns];
                Assert.That(cells[i].GlobalPosition.X, Is.EqualTo(header.GlobalPosition.X).Within(0.5f),
                    $"cell {i} must sit under its header");
                Assert.That(cells[i].Width, Is.EqualTo(header.Width).Within(0.5f), $"cell {i} width");
                Assert.That(cells[i].Width, Is.GreaterThan(0f), $"cell {i} must be laid out");
            }
        });
    }

    /// <summary>
    /// The window has two tabs, PARTS and COMPONENTS; the parts table and the doll live in the first, the components
    /// table in the second, and a new state never moves the player off the tab they chose.
    /// </summary>
    [Test]
    public async Task TabsTest()
    {
        WFChassisPanelWindow? window = null;
        var changes = new List<int>();
        await Client.WaitPost(() =>
        {
            window = new WFChassisPanelWindow();
            window.OnTabChanged += changes.Add;
            window.OpenCentered();
            window.Update(State());
        });
        await RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var panel = window!;
            var tabs = panel.TabHost;
            Assert.That(tabs.ChildCount, Is.EqualTo(2));
            Assert.That(tabs.GetActualTabTitle(0), Is.EqualTo("PARTS"));
            Assert.That(tabs.GetActualTabTitle(1), Is.EqualTo("COMPONENTS"));
            Assert.That(panel.CurrentTab, Is.EqualTo(0), "the window opens on the parts.");

            var parts = tabs.GetChild(0);
            var components = tabs.GetChild(1);
            var grids = Descendants(panel).OfType<GridContainer>().ToList();
            Assert.That(grids, Has.Count.EqualTo(2));
            Assert.That(Descendants(parts).OfType<GridContainer>().ToList(), Is.EqualTo(new[] { grids[0] }),
                "the parts table is in tab 0.");
            Assert.That(Descendants(components).OfType<GridContainer>().ToList(), Is.EqualTo(new[] { grids[1] }),
                "the components table is in tab 1.");
            Assert.That(grids[0].Columns, Is.EqualTo(7));
            Assert.That(grids[1].Columns, Is.EqualTo(6));
            Assert.That(parts.Visible, Is.True);
            Assert.That(components.Visible, Is.False);

            // The header and the controls stay out of the tabs, so they show on both.
            foreach (var control in new Control[] { panel.BayButton, panel.RebootControl, panel.OverclockControl })
                Assert.That(Descendants(tabs).Contains(control), Is.False, $"{control.GetType().Name} sits below the tabs.");

            panel.Update(State(bayOpen: true));
            Assert.That(panel.CurrentTab, Is.EqualTo(0), "an update keeps the tab.");
            panel.CurrentTab = 1;
            Assert.That(changes, Is.EqualTo(new[] { 1 }));
            panel.Update(State());
            panel.Update(new WFChassisPanelState());
            Assert.That(panel.CurrentTab, Is.EqualTo(1), "updates, even an empty one, do not reset the tab.");
            Assert.That(components.Visible, Is.True);
            Assert.That(parts.Visible, Is.False);

            panel.CurrentTab = 99;
            Assert.That(panel.CurrentTab, Is.EqualTo(1), "an index past the last tab clamps to it.");
        });

        // The controls are live on the components tab as well.
        await RunTicksSync(3);
        await Client.WaitAssertion(() =>
        {
            var bay = 0;
            window!.OnToggleBay += () => bay++;
            window.Update(State());
            WFButtonTestInput.Click(window.BayButton);
            Assert.That(bay, Is.EqualTo(1));
        });

        await Client.WaitPost(() => window!.Dispose());
    }

    /// <summary>Each part's colouring on the doll: off, disabled or an integrity band.</summary>
    [Test]
    public void DollMappingTest()
    {
        var state = State();
        var body = WFChassisPanelWindow.BuildDoll(state.Parts);

        Assert.Multiple(() =>
        {
            Assert.That(body[TargetBodyPart.Head], Is.EqualTo(TargetIntegrity.LightlyWounded), "0.8 is lightly wounded.");
            Assert.That(body[TargetBodyPart.RightLeg], Is.EqualTo(TargetIntegrity.Severed), "not attached.");
            Assert.That(body[TargetBodyPart.LeftFoot], Is.EqualTo(TargetIntegrity.Disabled), "isolated.");
            Assert.That(body[TargetBodyPart.RightHand], Is.EqualTo(TargetIntegrity.Disabled), "not working.");
            Assert.That(body[TargetBodyPart.Groin], Is.EqualTo(body[TargetBodyPart.Torso]), "the groin follows the torso.");
            Assert.That(body, Has.Count.EqualTo(11));

            var bands = new (float Integrity, TargetIntegrity Expected)[]
            {
                (1f, TargetIntegrity.Healthy), (0.9f, TargetIntegrity.Healthy), (0.89f, TargetIntegrity.LightlyWounded),
                (0.7f, TargetIntegrity.LightlyWounded), (0.69f, TargetIntegrity.SomewhatWounded),
                (0.5f, TargetIntegrity.SomewhatWounded), (0.49f, TargetIntegrity.ModeratelyWounded),
                (0.3f, TargetIntegrity.ModeratelyWounded), (0.29f, TargetIntegrity.HeavilyWounded),
                (0.1f, TargetIntegrity.HeavilyWounded), (0.09f, TargetIntegrity.CriticallyWounded),
                (0f, TargetIntegrity.CriticallyWounded),
            };
            foreach (var (integrity, expected) in bands)
            {
                var entry = Part(TargetBodyPart.LeftArm, "x", 1, e => e.Integrity = integrity);
                Assert.That(WFChassisPanelWindow.DollIntegrity(entry), Is.EqualTo(expected), $"integrity {integrity}");
            }

            // Detached outranks isolated; isolated outranks a healthy reading.
            var both = Part(TargetBodyPart.LeftArm, "x", 1, e => { e.Attached = false; e.Isolated = true; });
            Assert.That(WFChassisPanelWindow.DollIntegrity(both), Is.EqualTo(TargetIntegrity.Severed));
            var isolated = Part(TargetBodyPart.LeftArm, "x", 1, e => { e.Isolated = true; e.Integrity = 1f; });
            Assert.That(WFChassisPanelWindow.DollIntegrity(isolated), Is.EqualTo(TargetIntegrity.Disabled));
        });
    }

    /// <summary>A pick from the doll selects its row and a click on a row selects it; picking the same one again clears it.</summary>
    [Test]
    public async Task DollSelectsRowsTest()
    {
        WFChassisPanelWindow? window = null;
        await Client.WaitPost(() =>
        {
            window = new WFChassisPanelWindow();
            window.OpenCentered();
            window.Update(State());
        });
        await RunTicksSync(3);

        await Client.WaitAssertion(() =>
        {
            var panel = window!;
            Assert.That(panel.Selected, Is.Null);
            Assert.That(panel.Doll, Is.Null, "a disconnected client has no entity systems to draw the doll with.");

            panel.PickFromDoll(TargetBodyPart.LeftArm);
            Assert.That(panel.Selected, Is.EqualTo(TargetBodyPart.LeftArm));
            Assert.That(Background(panel.Parts[2].Cells[0]), Is.EqualTo(SelectedBackground), "the left arm row is lit.");
            Assert.That(panel.Parts.Count(r => Background(r.Cells[0]) == SelectedBackground), Is.EqualTo(1));

            // The groin has no row of its own and picks the torso.
            panel.PickFromDoll(TargetBodyPart.Groin);
            Assert.That(panel.Selected, Is.EqualTo(TargetBodyPart.Torso));
            Assert.That(Background(panel.Parts[1].Cells[0]), Is.EqualTo(SelectedBackground));
            Assert.That(Background(panel.Parts[2].Cells[0]), Is.Not.EqualTo(SelectedBackground));

            // A click on a row's cell selects it on the doll.
            ClickCell(panel.Parts[3].Cells[0]);
            Assert.That(panel.Selected, Is.EqualTo(TargetBodyPart.RightArm));
            Assert.That(Background(panel.Parts[3].Cells[0]), Is.EqualTo(SelectedBackground));

            // A new state keeps the pick and its highlight.
            panel.Update(State(bayOpen: true));
            Assert.That(panel.Selected, Is.EqualTo(TargetBodyPart.RightArm));
            Assert.That(Background(panel.Parts[3].Cells[0]), Is.EqualTo(SelectedBackground));

            ClickCell(panel.Parts[3].Cells[0]);
            Assert.That(panel.Selected, Is.Null, "the same row again clears the pick.");
            Assert.That(panel.Parts.Any(r => Background(r.Cells[0]) == SelectedBackground), Is.False);

            // The buttons in a row still do their own job and do not select.
            WFButtonTestInput.Click(panel.Parts[2].Isolate);
            Assert.That(panel.Selected, Is.Null);
        });

        await Client.WaitPost(() => window!.Dispose());
    }

    /// <summary>The bay strip sits in the gap above the hotbar and clear of every box the readout draws.</summary>
    [Test]
    public void BayStripClearsTheReadoutTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var (width, height) in new[] { (1920, 1080), (1280, 720), (1600, 900) })
            {
                var screen = new UIBox2(0f, 0f, width, height);
                var strip = WFChassisBayLayout.Strip(screen);
                Assert.That(WolfmedSyntheticHudLayout.Contains(screen, strip), Is.True, $"strip inside {width}x{height}");

                foreach (var (name, box) in WolfmedSyntheticHudLayout.Reserved(screen))
                    Assert.That(WolfmedSyntheticHudLayout.Overlaps(strip, box), Is.False, $"strip overlaps {name} at {width}x{height}");

                foreach (var ui in new[] { 1f, 1.5f, 2f })
                {
                    var scale = WolfmedSyntheticHudLayout.FitScale(screen, WolfmedSyntheticHudLayout.Scale(1f, ui));
                    foreach (var (name, box) in WolfmedSyntheticHudLayout.DrawnBoxes(screen, scale,
                                 WolfmedSyntheticHudLayout.MaxSystemGauges, WolfmedSyntheticHudLayout.MaxAdviceRows, 8))
                        Assert.That(WolfmedSyntheticHudLayout.Overlaps(strip, box), Is.False,
                            $"strip overlaps the readout's {name} at {width}x{height}, ui scale {ui}");

                    var tag = WFChassisBayLayout.Tag(screen, WolfmedSyntheticHudLayout.Scale(1f, ui));
                    Assert.That(WolfmedSyntheticHudLayout.Contains(screen, tag), Is.True, $"tag inside {width}x{height}, ui scale {ui}");
                    foreach (var (name, box) in WolfmedSyntheticHudLayout.Reserved(screen))
                        Assert.That(WolfmedSyntheticHudLayout.Overlaps(tag, box), Is.False, $"tag overlaps {name} at {width}x{height}, ui scale {ui}");
                }
            }
        });
    }
}
