using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Content.Client._WF.ShipShields;
using Content.Client._WF.Stylesheets;
using Content.Client.Shuttles.UI;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.ShipShields;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.ShipShields;

/// <summary>Checks shared generator and helm controls fit and preserve intentional commands.</summary>
[TestFixture]
public sealed class WFShipShieldControlsTest
{
    [Test]
    public async Task GeneratorLayoutHealthWarningsAndHelmAvailability()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            using var generator = new WFShipShieldGeneratorWindow();
            var panel = generator.ShieldPanel;
            panel.UpdateState(State(), 0f);
            foreach (var size in new[] { new Vector2(760f, 540f), new Vector2(900f, 700f) })
            {
                generator.SetSize = size;
                generator.Measure(size);
                generator.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                var body = Tree(panel).Single(control => control.GetType().Name == "ShieldColumns");
                Assert.That(body.Size.Y, Is.GreaterThan(100f), "Preview and settings must receive remaining height instead of collapsing inside a scroll container.");
                Assert.That(body.Size.X, Is.GreaterThan(500f));
                var health = Field<ProgressBar>(panel, "_health");
                Assert.That(health.Size.Y, Is.GreaterThanOrEqualTo(22f), "Integrity must have a readable thick bar.");
                foreach (var action in new Control[] { Field<Button>(panel, "_reset"), Field<Button>(panel, "_enabled"), health })
                {
                    Assert.That(action.Size.Y, Is.GreaterThan(0f));
                    Assert.That(action.GlobalPosition.Y, Is.GreaterThanOrEqualTo(generator.GlobalPosition.Y));
                    Assert.That(action.GlobalPosition.Y + action.Size.Y,
                        Is.LessThanOrEqualTo(generator.GlobalPosition.Y + size.Y + 1f), "Main commands and integrity must remain visible at compact size.");
                }
            }
            var recovery = Field<RichTextLabel>(panel, "_recovery");
            Assert.That(recovery.Visible, Is.False, "Online shields do not need a recovery countdown.");
            var recovering = new WFShipShieldShuntState(true, false, 0.6f, 0.8f, 0.2f, MathF.PI / 2f)
            {
                RecoveryStatus = WFShipShieldRecoveryStatus.Overloaded,
                RecoverySeconds = 80,
            };
            panel.UpdateState(recovering, 0f);
            Assert.That(recovery.Visible, Is.True);
            Assert.That(recovery.Text, Does.Contain("1:20").And.Contain("Overload cooldown"));
            panel.SetDraft(43f, 0.75f, 120f);
            panel.UpdateState(new WFShipShieldShuntState(true, false, 0.6f, 0.8f, 0.2f, MathF.PI / 2f)
            {
                RecoveryStatus = WFShipShieldRecoveryStatus.RechargingAndOverloaded,
                RecoverySeconds = 65,
            }, 0f);
            Assert.That(recovery.Text, Does.Contain("1:05").And.Contain("Recharging and overload cooldown"));
            Assert.That(Field<Slider>(panel, "_concentration").Value, Is.EqualTo(75f), "Countdown updates must preserve an allocation draft.");
            generator.SetSize = new Vector2(760f, 540f);
            generator.Measure(new Vector2(760f, 540f));
            generator.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(760f, 540f)));
            Assert.That(Tree(panel).Single(control => control.GetType().Name == "ShieldColumns").Size.Y, Is.GreaterThan(100f));
            Assert.That(recovery.GlobalPosition.Y + recovery.Size.Y, Is.LessThan(generator.GlobalPosition.Y + 540f));
            recovering.RecoveryStatus = WFShipShieldRecoveryStatus.NoPower;
            recovering.RecoverySeconds = -1;
            panel.UpdateState(recovering, 0f);
            Assert.That(recovery.Text, Does.Contain("Waiting for power").And.Not.Contain("1:20"));
            recovering.RecoveryStatus = WFShipShieldRecoveryStatus.Lowered;
            panel.UpdateState(recovering, 0f);
            Assert.That(recovery.Text, Does.Contain("Manually lowered"));
            panel.UpdateState(State(), 0f);
            Assert.That(recovery.Visible, Is.False);
            var bar = Field<ProgressBar>(panel, "_health");
            var dial = Field<Control>(panel, "_dial");
            Color Tint() => ((StyleBoxFlat)bar.ForegroundStyleBoxOverride!).BackgroundColor;
            Assert.That(Tint(), Is.EqualTo(WFShipShieldEffects.HealthColor(0.6f)));
            Assert.That(Field<Color>(dial, "HealthTint"), Is.EqualTo(Tint()), "The ring and bar must show the same condition.");
            panel.UpdateState(State(health: 0.09f), 0f);
            var bright = Tint();
            typeof(WFShipShieldShuntScreen).GetMethod("FrameUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(panel, new object[] { new FrameEventArgs(0.5f) });
            Assert.That(Tint().R, Is.LessThan(bright.R - 0.2f), "Integrity below ten percent must visibly pulse red.");
            Assert.That(Field<Color>(dial, "HealthTint"), Is.EqualTo(Tint()));
            panel.UpdateState(State(health: 0.1f), 0f);
            Assert.That(Tint(), Is.EqualTo(WFShipShieldEffects.HealthColor(0.1f)), "The warning threshold is strictly below ten percent.");

            var enabledRequests = new List<bool>();
            panel.OnSetEnabled += enabledRequests.Add;
            Press(Field<Button>(panel, "_enabled"));
            Assert.That(enabledRequests, Is.EqualTo(new[] { false }));
            panel.UpdateState(State(enabled: false), 0f);
            Press(Field<Button>(panel, "_enabled"));
            Assert.That(enabledRequests, Is.EqualTo(new[] { false, true }));
            panel.UpdateState(State(available: false), 0f);
            Assert.That(Field<Button>(panel, "_enabled").Disabled, Is.True);
            Press(Field<Button>(panel, "_enabled"));
            Assert.That(enabledRequests, Has.Count.EqualTo(2));

            using var helm = new ShuttleConsoleWindow();
            var tab = Field<Button>(helm, "_shieldModeButton");
            var screen = Field<WFShipShieldShuntScreen>(helm, "_shieldScreen");
            Assert.That(tab.Visible, Is.False, "Ships without an installed generator must not show the Shields tab.");
            var rotation = Angle.FromDegrees(17f);
            void Update(WFShipShieldShuntState state) => typeof(ShuttleConsoleWindow)
                .GetMethod("WfShieldUpdateState", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(helm, new object[] { state, rotation });
            Update(State());
            Assert.That(tab.Visible, Is.True);
            helm.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Shields);
            Assert.That(screen.Visible, Is.True);
            foreach (var size in new[] { helm.MinSize, new Vector2(1128, 776) })
            {
                helm.SetSize = size;
                helm.Measure(size);
                helm.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                var body = Tree(screen).Single(control => control.GetType().Name == "ShieldColumns");
                Assert.That(body.Height, Is.GreaterThan(100), "The instrument layout must reserve space for the coverage dial.");
                foreach (var action in new Control[] { Field<Button>(screen, "_enabled"), Field<Button>(screen, "_reset"), Field<ProgressBar>(screen, "_health") })
                {
                    Assert.That(action.Height, Is.GreaterThan(0));
                    Assert.That(action.GlobalPosition.Y, Is.GreaterThanOrEqualTo(screen.GlobalPosition.Y));
                    Assert.That(action.GlobalPosition.Y + action.Height, Is.LessThanOrEqualTo(helm.GlobalPosition.Y + size.Y),
                        "Deployment, reset and integrity must remain visible at the helm's minimum size.");
                }
            }
            var allocations = new List<(float Direction, float Amount, float Arc)>();
            helm.ShieldShuntRequested += (direction, amount, arc) => allocations.Add((direction, amount, arc));
            screen.SetDraft(43f, 0.75f, 120f);
            Update(State(health: 0.4f));
            helm.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Nav);
            helm.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Shields);
            Assert.That(allocations, Is.Empty);
            Tick(screen, 0.11f);
            Assert.That(allocations, Has.Count.EqualTo(1));
            Assert.That(allocations[0].Amount, Is.EqualTo(0.75f).Within(0.0001f), "Health polling and tab switching must preserve the draft.");
            Assert.That(allocations[0].Direction, Is.EqualTo(WFShipShieldHelmAngles.GridDirection(43f, (float)rotation.Theta)).Within(0.0001f));
            var moving = State(health: 0.4f);
            moving.Concentration = 0.4f;
            moving.TargetDirectionRadians = allocations[0].Direction;
            moving.TargetConcentration = 0.75f;
            moving.TargetArcRadians = 2f * MathF.PI / 3f;
            var nav = Tree(helm).OfType<ShuttleNavControl>().First();
            var navRotation = Angle.FromDegrees(61f);
            typeof(ShuttleNavControl).GetField("_rotation", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(nav, navRotation);
            helm.UpdateShieldShuntSnapshot(new WFShipShieldHelmUpdateMessage(moving).ShieldShunt);
            Assert.That(Field<float>(screen, "_helmRotation"), Is.EqualTo((float)rotation.Theta).Within(0.0001f),
                "Shield-only snapshots must preserve the current helm bearing.");
            Assert.That(typeof(ShuttleNavControl).GetField("_rotation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(nav),
                Is.EqualTo(navRotation), "A shield snapshot must not reset navigation state.");
            Assert.That(Field<WFShipShieldShuntState>(screen, "_state"), Is.SameAs(moving));
            Tick(screen, 0.05f);
            var liveDial = Field<Control>(screen, "_dial");
            Assert.That(Field<float>(liveDial, "Concentration"), Is.InRange(0.201f, 0.399f),
                "The ring should animate toward actual coverage without jumping to the requested 75 percent.");
            Assert.That(allocations, Has.Count.EqualTo(1), "Incoming health/allocation snapshots must not send edits back.");
            screen.SetDraft(80f, 0.6f, 90f);
            screen.SetDraft(90f, 0.5f, 100f);
            Tick(screen, 0.01f);
            Assert.That(allocations, Has.Count.EqualTo(1), "Rapid input must respect the send interval.");
            Tick(screen, 0.1f);
            Assert.That(allocations, Has.Count.EqualTo(2));
            Assert.That(allocations[1].Amount, Is.EqualTo(0.5f));
            Assert.That(allocations[1].Direction, Is.EqualTo(WFShipShieldHelmAngles.GridDirection(90f, (float) rotation.Theta)).Within(0.0001f));
            helm.UpdateShieldShuntSnapshot(State(available: false));
            Assert.That(tab.Visible, Is.False);
            Assert.That(screen.Visible, Is.False, "Removing the generator must leave Shields mode.");
            Assert.That(Field<ShuttleConsoleWindow.ShuttleConsoleMode>(helm, "_mode"), Is.EqualTo(ShuttleConsoleWindow.ShuttleConsoleMode.Nav));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CockpitShuntStripSpansTheDialBank()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalTheme = settings.GetCVar(WolfgateCVars.UiStyle);
            using var screen = new WFShipShieldShuntScreen();
            screen.WfRefitInstruments();
            screen.UpdateState(State(), 0f);
            var original = Tree(screen).OfType<WFGlassGauge>()
                .ToDictionary(gauge => gauge, gauge => (gauge.Parent, gauge.SetSize));
            var lease = new WFCockpitLease();
            using var page = screen.WfCockpitShieldDetails(lease);
            try
            {
                foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
                foreach (var size in new[] { new Vector2(300, 500), new Vector2(560, 800) })
                {
                    settings.SetCVar(WolfgateCVars.UiStyle, theme);
                    foreach (var control in Tree(page))
                        control.InvalidateMeasure();
                    page.Measure(size);
                    page.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    var strip = Tree(page).OfType<WFGlassGauge>().Single(gauge => gauge.Strip);
                    var rounds = Tree(page).OfType<WFGlassGauge>().Where(gauge => !gauge.Strip).ToArray();
                    var bank = (GridContainer) rounds[0].Parent!;
                    Assert.That(strip.Height, Is.EqualTo(64).Within(1), "Target shunt must retain a thin linear scale in both themes.");
                    Assert.That(strip.Width, Is.EqualTo(bank.Width).Within(1), "The target scale spans both dial columns.");
                    Assert.That(strip.GlobalPosition.X, Is.EqualTo(bank.GlobalPosition.X).Within(1));
                    Assert.That(strip.GlobalPosition.Y, Is.EqualTo(page.GlobalPosition.Y).Within(1), "Target shunt belongs at the top of the shield page.");
                    Assert.That(strip.GlobalPosition.Y + strip.Height, Is.LessThanOrEqualTo(bank.GlobalPosition.Y));
                    Assert.That(rounds, Has.Length.EqualTo(4));
                    Assert.That(bank.Columns, Is.EqualTo(2));
                    Assert.That(rounds.Select(gauge => gauge.GlobalPosition.X).Distinct().Count(), Is.EqualTo(2));
                    Assert.That(rounds.Select(gauge => gauge.GlobalPosition.Y).Distinct().Count(), Is.EqualTo(2));
                    foreach (var gauge in rounds)
                        Assert.That(gauge.Height, Is.EqualTo(160).Within(1), "Round dials keep one height in both themes.");
                }
            }
            finally
            {
                lease.Restore();
                settings.SetCVar(WolfgateCVars.UiStyle, originalTheme);
            }
            foreach (var (gauge, layout) in original)
            {
                Assert.That(gauge.Parent, Is.SameAs(layout.Parent));
                Assert.That(gauge.SetSize, Is.EqualTo(layout.SetSize), "Exiting cockpit must restore the original shield-console sizing.");
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Wide screens give the coverage panel a second column; narrow ones keep a single stack.</summary>
    [Test]
    public async Task SettingsSplitIntoTwoColumnsOnlyWhenWide()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            using var screen = new WFShipShieldShuntScreen();
            screen.UpdateState(State(), 0f);
            var settings = Tree(screen).Single(control => control.GetType().Name == "ShieldSettings");
            foreach (var (size, wide) in new[] { (new Vector2(760f, 900f), false), (new Vector2(1400f, 900f), true) })
            {
                foreach (var control in Tree(screen))
                    control.InvalidateMeasure();
                screen.Measure(size);
                screen.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                var panels = settings.Children.Where(child => child.Visible).ToArray();
                Assert.That(panels, Has.Length.EqualTo(3));
                Assert.That(panels[1].GlobalPosition.X, Is.EqualTo(panels[0].GlobalPosition.X).Within(1f));
                if (wide)
                {
                    Assert.That(panels[2].GlobalPosition.X, Is.GreaterThanOrEqualTo(panels[0].GlobalPosition.X + panels[0].Width),
                        "The coverage panel takes its own column at wide sizes.");
                    Assert.That(panels[2].GlobalPosition.Y, Is.EqualTo(panels[0].GlobalPosition.Y).Within(1f));
                }
                else
                {
                    Assert.That(panels[2].GlobalPosition.X, Is.EqualTo(panels[0].GlobalPosition.X).Within(1f),
                        "Narrow screens keep one column.");
                    Assert.That(panels[2].GlobalPosition.Y, Is.GreaterThan(panels[1].GlobalPosition.Y));
                }
            }
        });
        await pair.CleanReturnAsync();
    }

    private static void Tick(WFShipShieldShuntScreen screen, float seconds) =>
        typeof(WFShipShieldShuntScreen).GetMethod("FrameUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(screen, new object[] { new FrameEventArgs(seconds) });

    private static WFShipShieldShuntState State(float health = 0.6f, bool available = true, bool enabled = true) =>
        new(available, enabled, health, 0.8f, 0.2f, MathF.PI / 2f, enabled);

    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void Press(BaseButton button)
    {
        var handler = (Action<BaseButton.ButtonEventArgs>)typeof(BaseButton)
            .GetField("OnPressed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(button);
        handler?.Invoke(new BaseButton.ButtonEventArgs(button, null!));
    }

    private static IEnumerable<Control> Tree(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var descendant in Tree(child))
            yield return descendant;
    }
}