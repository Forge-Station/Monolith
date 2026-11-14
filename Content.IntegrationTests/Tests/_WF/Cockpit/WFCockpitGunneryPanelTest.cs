#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Content.IntegrationTests.Tests._WF.CombatConsole;
using Content.Client._WF.Stylesheets;
using Content.Shared._Mono.FireControl;
using Content.Shared._Mono.ShipGuns;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.CombatConsole;
using Content.Shared.Shuttles.BUIStates;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Cockpit;

/// <summary>Exercises the embedded battery's existing command handlers, supply telemetry and its single scrolling list.</summary>
[TestFixture]
public sealed class WFCockpitGunneryPanelTest
{
    [Test]
    public async Task EmbeddedBatteryKeepsGroupsFlaresAndSupply()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalTheme = settings.GetCVar(WolfgateCVars.UiStyle);
            var windows = Tree(ui.RootControl).OfType<BaseWindow>().ToArray();
            using var panel = new WFCockpitGunneryPanel();
            var commands = new List<BoundUserInterfaceMessage>();
            panel.Command += commands.Add;
            ui.RootControl.AddChild(panel);
            var entries = Enumerable.Range(1, 25).Select(index => new FireControllableEntry(new NetEntity(910 + index),
                default, $"Battery {index}", 6, true)).ToArray();
            var state = new FireControlConsoleBoundInterfaceState(true, entries,
                new NavInterfaceState(250, null, null, new(), default));
            var combat = state.Combat;
            combat.FlareLaunchers.Add(entries[24].NetEntity);
            combat.Ammunition = 8;
            combat.Groups[0].AddRange(new[] { entries[0].NetEntity, entries[23].NetEntity });
            combat.WeaponSupplies[entries[0].NetEntity] = new WFWeaponSupply(WFWeaponSupplyKind.Infinite, null, null);
            combat.WeaponSupplies[entries[1].NetEntity] = new WFWeaponSupply(WFWeaponSupplyKind.Energy, 3, 10);
            combat.WeaponSupplies[entries[2].NetEntity] = new WFWeaponSupply(WFWeaponSupplyKind.Recharging, 0, 10);
            combat.WeaponTypes[entries[0].NetEntity] = ShipGunType.Ballistic;
            panel.UpdateState(state);
            var (scroll, grid) = WFWeaponListChecks.OneList(panel, "embedded");
            var buttons = grid.Children.OfType<Button>().ToArray();
            var flares = new HashSet<NetEntity> { entries[24].NetEntity };
            Button Named(string name) => Tree(panel).OfType<Button>().Single(button => button.Name == name);
            Label NamedLabel(string name) => Tree(panel).OfType<Label>().Single(label => label.Name == name);
            Assert.That(Tree(ui.RootControl).OfType<BaseWindow>(), Is.EqualTo(windows), "The cockpit battery must not open a gunnery window.");
            void Layout(Vector2 size)
            {
                foreach (var child in Tree(panel))
                    child.InvalidateMeasure();
                panel.Measure(size);
                panel.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
            }
            WFWeaponRow Row(Button button) => button.Children.OfType<WFWeaponRow>().Single();
            List<Button> Listed(IEnumerable<FireControllableEntry> present)
            {
                var weapons = present.ToArray();
                var byName = grid.Children.OfType<Button>().ToDictionary(button => button.Text!);
                return WFWeaponListChecks.Expected(weapons, weapons.ToDictionary(weapon => weapon.NetEntity, weapon => byName[weapon.Name]), flares);
            }
            void CheckList(Vector2 size, string context)
            {
                var listed = Listed(entries);
                WFWeaponListChecks.NoPaging(panel, context);
                WFWeaponListChecks.Layout(grid, listed, context);
                WFWeaponListChecks.Compact(grid, listed, context);
                foreach (var button in listed)
                    WFWeaponListChecks.Readable(button, ui, context);
                WFWeaponListChecks.Reachable(scroll, listed, () => Layout(size), context);
            }
            Layout(new Vector2(296, 450));
            Assert.That(WFWeaponListChecks.Overflows(scroll, grid), Is.True, "A large battery scrolls rather than paging or shrinking every row.");
            Assert.That(buttons.Count(button => button.Visible), Is.EqualTo(24), "Every offensive weapon is in the one list.");
            Assert.That(buttons[24].Visible, Is.False, "Flares are excluded from offensive selection.");
            Assert.That(Row(buttons[0]).Reading.Text, Does.Contain(Loc.GetString("wf-gauge-unlimited")));
            Assert.That(buttons[0].ModulateSelfOverride, Is.Null);
            Assert.That(Row(buttons[1]).Reading.Value, Is.EqualTo(30));
            Assert.That(Row(buttons[2]).Reading.Text, Does.Contain(Loc.GetString("wf-gauge-recharging")));
            Assert.That(Row(buttons[3]).Reading.Value, Is.EqualTo(6), "Finite supply must remain an exact count.");
            Press(Named("CockpitGroupRecall0"));
            Assert.That(panel.SelectedWeapons, Is.EquivalentTo(combat.Groups[0]), "A group includes weapons that are scrolled out of view.");
            Assert.That(panel.HasSelectedWeapons, Is.True, "The armed check must see a recalled group.");
            var farWeapon = Listed(entries)[^1];
            Assert.That(farWeapon.Pressed, Is.False);
            scroll.VScroll = farWeapon.Position.Y;
            Layout(new Vector2(296, 450));
            Assert.That(scroll.VScroll, Is.GreaterThan(0), "The end of the list is below the first screenful.");
            Assert.That(farWeapon.GlobalPosition.Y + farWeapon.Height, Is.LessThanOrEqualTo(scroll.GlobalPosition.Y + scroll.Height + 1));
            Assert.That(panel.SelectedWeapons, Is.EquivalentTo(combat.Groups[0]), "Scrolling must not drop off-screen firing selections.");
            Toggle(farWeapon, true);
            var expectedSelection = combat.Groups[0].Append(entries[Array.IndexOf(buttons, farWeapon)].NetEntity).ToArray();
            Assert.That(panel.SelectedWeapons, Is.EquivalentTo(expectedSelection));
            var offset = scroll.VScroll;
            panel.UpdateStatus(state);
            Layout(new Vector2(296, 450));
            Assert.That(scroll.VScroll, Is.EqualTo(offset), "Authoritative supply snapshots must not send the operator back to the top of the list.");
            Assert.That(panel.SelectedWeapons, Is.EquivalentTo(expectedSelection));
            Assert.That(commands, Is.Empty, "Scrolling and received state must not send control commands.");
            var store = Named("CockpitGroupStore");
            Toggle(store, true);
            Layout(new Vector2(296, 450));
            Assert.That(Named("CockpitGroupSave2").Visible, Is.True);
            Press(Named("CockpitGroupSave2"));
            var saved = commands.OfType<WFSaveWeaponGroupMessage>().Single();
            Assert.That(saved.Slot, Is.EqualTo(2));
            Assert.That(saved.Weapons, Is.EquivalentTo(expectedSelection), "Saving a group includes selected weapons anywhere in the list.");
            Assert.That(store.Pressed, Is.False);
            Assert.That(Named("CockpitGroupSave2").Visible, Is.False);
            Toggle(Named("CockpitAutomaticFlares"), true);
            Assert.That(commands.OfType<WFAutomaticFlaresMessage>().Single().Enabled, Is.True);
            Assert.That(Named("CockpitDispenseFlares").Disabled, Is.False);
            Press(Named("CockpitDispenseFlares"));
            Assert.That(commands.OfType<WFDispenseFlaresMessage>().Count(), Is.EqualTo(1));
            Press(Named("CockpitGunneryRefresh"));
            Assert.That(commands.OfType<FireControlConsoleRefreshServerMessage>().Count(), Is.EqualTo(1));
            var supply = NamedLabel("CockpitFlareSupply");
            var flarePanel = Tree(panel).Single(control => control.Name == "WfCountermeasurePanel");
            Assert.That(supply.Text, Is.EqualTo(Loc.GetString("wf-gunnery-flare-ready", ("ammo", "8"))));
            combat.FlareLaunchers.Clear();
            panel.UpdateStatus(state);
            Assert.That(flarePanel.Visible, Is.False);
            Assert.That(NamedLabel("WfMissileAlert").VisibleInTree, Is.True, "Incoming missile status remains available without flare launchers.");
            Assert.That(Named("CockpitAutomaticFlares").Visible, Is.False);
            Assert.That(Named("CockpitDispenseFlares").Visible, Is.False);
            Assert.That(Named("CockpitGunneryRefresh").Visible, Is.True, "Link refresh remains available without flares.");
            combat.FlareLaunchers.Add(entries[24].NetEntity);
            combat.Ammunition = 0;
            panel.UpdateStatus(state);
            Assert.That(flarePanel.Visible, Is.True, "Installed but empty launchers must remain visible.");
            Assert.That(Named("CockpitAutomaticFlares").Visible, Is.True);
            Assert.That(Named("CockpitDispenseFlares").Visible, Is.True);
            Assert.That(Named("CockpitDispenseFlares").Disabled, Is.True);
            Layout(new Vector2(296, 450));
            var beforeEmptyPress = commands.Count;
            Press(Named("CockpitDispenseFlares"));
            Assert.That(commands, Has.Count.EqualTo(beforeEmptyPress), "An empty dispenser must reject the real button input.");
            combat.UnlimitedSupply = true;
            panel.UpdateStatus(state);
            Assert.That(Named("CockpitDispenseFlares").Disabled, Is.False, "An automatic feed remains usable at zero stored rounds.");
            Assert.That(supply.Text, Does.Contain(Loc.GetString("wf-gunnery-flare-unlimited")));
            combat.UnlimitedSupply = false;
            combat.Ammunition = 8;
            combat.Cooldown = 3;
            panel.UpdateStatus(state);
            Assert.That(Named("CockpitDispenseFlares").Disabled, Is.True, "The embedded button retains the existing lockout.");
            var beforeCooldownPress = commands.Count;
            Press(Named("CockpitDispenseFlares"));
            Assert.That(commands, Has.Count.EqualTo(beforeCooldownPress), "A rearming dispenser must reject the real button input.");
            var commandsBeforeTheme = commands.Count;
            try
            {
                foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
                foreach (var size in new[] { new Vector2(296, 450), new Vector2(400, 700) })
                {
                    settings.SetCVar(WolfgateCVars.UiStyle, theme);
                    panel.UpdateStatus(state);
                    Layout(size);
                    Toggle(Named("WfWeaponTypeSelect"), true);
                    Layout(size);
                    CheckList(size, $"{theme}/{size}/types");
                    Toggle(Named("WfWeaponTypeSelect"), false);
                    Layout(size);
                    CheckList(size, $"{theme}/{size}");
                    Assert.That(panel.SelectedWeapons, Is.EquivalentTo(expectedSelection), "Theme changes retain off-screen selections.");
                    Assert.That(commands.Count, Is.EqualTo(commandsBeforeTheme), "Restyling must not send commands.");
                    foreach (var button in Tree(panel).OfType<Button>().Where(button => button.VisibleInTree && button.Parent != grid))
                    {
                        Assert.That(button.Height, Is.GreaterThanOrEqualTo(28), $"{theme}: {button.Name}");
                        Assert.That(button.GlobalPosition.X, Is.GreaterThanOrEqualTo(panel.GlobalPosition.X));
                        Assert.That(button.GlobalPosition.Y, Is.GreaterThanOrEqualTo(panel.GlobalPosition.Y));
                        Assert.That(button.GlobalPosition.X + button.Width, Is.LessThanOrEqualTo(panel.GlobalPosition.X + panel.Width + 1));
                        Assert.That(button.GlobalPosition.Y + button.Height, Is.LessThanOrEqualTo(panel.GlobalPosition.Y + panel.Height + 1));
                    }
                    Assert.That(scroll.GlobalPosition.X, Is.GreaterThanOrEqualTo(panel.GlobalPosition.X), theme);
                    Assert.That(scroll.GlobalPosition.Y, Is.GreaterThanOrEqualTo(panel.GlobalPosition.Y), theme);
                    Assert.That(scroll.GlobalPosition.X + scroll.Width, Is.LessThanOrEqualTo(panel.GlobalPosition.X + panel.Width + 1), theme);
                    Assert.That(scroll.GlobalPosition.Y + scroll.Height, Is.LessThanOrEqualTo(panel.GlobalPosition.Y + panel.Height + 1), theme);
                }
                var compactSize = new Vector2(296, 450);
                panel.UpdateStatus(state);
                Layout(compactSize);
                var fullHeight = grid.ContentHeight;
                scroll.VScroll = fullHeight;
                Layout(compactSize);
                Assert.That(scroll.VScroll, Is.GreaterThan(0));
                panel.UpdateStatus(new FireControlConsoleBoundInterfaceState(true, entries.Take(4).ToArray(), state.NavState));
                Layout(compactSize);
                Assert.That(grid.ContentHeight, Is.LessThan(fullHeight), "Removing weapons shortens the list.");
                Assert.That(scroll.Height, Is.EqualTo(grid.ContentHeight).Within(1), "A short list takes only the height it needs.");
                Assert.That(scroll.VScroll, Is.Zero, "A list that fits again starts from its top.");
                Assert.That(grid.Children.OfType<Button>().Count(), Is.EqualTo(4));
                WFWeaponListChecks.Layout(grid, Listed(entries.Take(4)), "four weapons");
                panel.UpdateStatus(state);
                Layout(compactSize);
                Assert.That(grid.ContentHeight, Is.EqualTo(fullHeight).Within(0.5f), "Adding weapons back restores the whole list without resizing the panel.");
                var restored = Listed(entries);
                Assert.That(restored.Any(button => button.Text == entries[23].Name), Is.True);
                WFWeaponListChecks.Reachable(scroll, restored, () => Layout(compactSize),
                    "The newly added final offensive weapon must be reachable after an in-place refresh.");
                panel.ClearSelection();
                Assert.That(panel.SelectedWeapons, Is.Empty);
                Assert.That(panel.HasSelectedWeapons, Is.False);
                panel.UpdateState(null);
                Layout(new Vector2(296, 450));
                Assert.That(grid.ContentHeight, Is.Zero);
                Assert.That(grid.Children.OfType<Button>(), Is.Empty);
                Assert.That(flarePanel.Visible, Is.False);
                Assert.That(Named("CockpitAutomaticFlares").Disabled, Is.True);
                Assert.That(Named("CockpitDispenseFlares").Disabled, Is.True);
            }
            finally
            {
                panel.Parent?.RemoveChild(panel);
                settings.SetCVar(WolfgateCVars.UiStyle, originalTheme);
            }
        });
        await pair.CleanReturnAsync();
    }

    private static void Press(BaseButton button) => WFButtonTestInput.Click(button);

    private static void Toggle(BaseButton button, bool pressed) => WFButtonTestInput.Toggle(button, pressed);

    private static IEnumerable<Control> Tree(Control root) => WFCockpitLease.Descendants(root);
}
