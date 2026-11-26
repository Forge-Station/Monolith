#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client._Mono.FireControl.UI;
using Content.Client._WF.CombatConsole;
using Content.Client._WF.Stylesheets;
using Content.Server._WF.CombatConsole;
using Content.Server._Mono.FireControl;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Server.Power.Components;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Content.Shared._Mono.FireControl;
using Content.Shared._Mono.ShipGuns;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.CombatConsole;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.CombatConsole;

/// <summary>Checks actual ammunition providers and the single scrolling gunnery battery at compact sizes.</summary>
[TestFixture]
public sealed class WFGunnerySupplyTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFGunneryFiniteTest
  components:
  - type: BasicEntityAmmoProvider
    proto: BulletHawk
    capacity: 10
    count: 0
- type: entity
  id: WFGunneryInfiniteTest
  components:
  - type: BasicEntityAmmoProvider
    proto: BulletHawk
- type: entity
  parent: WFGunneryFiniteTest
  id: WFGunneryRechargeTest
  components:
  - type: RechargeBasicEntityAmmo
    rechargeCooldown: 3600
";

    [Test]
    public async Task ProvidersAndAdaptiveBattery()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var system = em.System<WFCombatConsoleSystem>();
            foreach (var (prototype, kind) in new[]
            {
                ("WFGunneryFiniteTest", WFWeaponSupplyKind.Finite),
                ("WFGunneryInfiniteTest", WFWeaponSupplyKind.Infinite),
                ("WFGunneryRechargeTest", WFWeaponSupplyKind.Recharging),
                ("SunnyMagazine", WFWeaponSupplyKind.Infinite),
                ("WeaponTurretFlare", WFWeaponSupplyKind.Infinite),
            })
            {
                var uid = em.SpawnEntity(prototype, map.GridCoords);
                var supply = system.GetWeaponSupply(uid);
                Assert.That(supply.Kind, Is.EqualTo(kind), prototype);
                if (kind == WFWeaponSupplyKind.Infinite)
                    Assert.That(supply.Count, Is.Null, "Unlimited providers must not report zero stored rounds as an empty magazine.");
                else
                {
                    Assert.That(supply.Count, Is.Zero);
                    Assert.That(supply.Capacity, Is.EqualTo(10));
                }
                em.DeleteEntity(uid);
            }
            var energy = em.SpawnEntity(null, map.GridCoords);
            var provider = em.AddComponent<ProjectileBatteryAmmoProviderComponent>(energy);
            provider.Shots = 3;
            provider.Capacity = 10;
            Assert.That(system.GetWeaponSupply(energy), Is.EqualTo(new WFWeaponSupply(WFWeaponSupplyKind.Energy, 3, 10)));
            em.DeleteEntity(energy);
        });
        await pair.Client.WaitAssertion(() =>
        {
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalTheme = settings.GetCVar(WolfgateCVars.UiStyle);
            using var window = new FireControlWindow();
            window.OpenCentered();
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var entries = Enumerable.Range(1, 40).Select(index => new FireControllableEntry(new NetEntity(810 + index),
                default, $"Battery {index}", 0, true)).ToArray();
            var state = new FireControlConsoleBoundInterfaceState(true, entries,
                new NavInterfaceState(250, null, null, new(), default));
            state.Combat.WeaponSupplies[entries[0].NetEntity] = new WFWeaponSupply(WFWeaponSupplyKind.Infinite, null, null);
            state.Combat.WeaponSupplies[entries[1].NetEntity] = new WFWeaponSupply(WFWeaponSupplyKind.Energy, 3, 10);
            state.Combat.WeaponSupplies[entries[2].NetEntity] = new WFWeaponSupply(WFWeaponSupplyKind.Recharging, 0, 10);
            state.Combat.WeaponSupplies[entries[3].NetEntity] = new WFWeaponSupply(WFWeaponSupplyKind.Finite, 0, 10);
            window.UpdateStatus(state);
            WFWeaponRow Row(int index) => window.WeaponsList[entries[index].NetEntity].Children.OfType<WFWeaponRow>().Single();
            Assert.That(Row(0).Reading.Text, Is.EqualTo(Loc.GetString("wf-gauge-unlimited")));
            Assert.That(Row(0).Reading.Value, Is.GreaterThan(0));
            Assert.That(window.WeaponsList[entries[0].NetEntity].ModulateSelfOverride, Is.Null);
            Assert.That(Row(1).Reading.Value, Is.EqualTo(30));
            Assert.That(Row(1).Reading.Maximum, Is.EqualTo(100));
            Assert.That(Row(2).Reading.Text, Is.EqualTo(Loc.GetString("wf-gauge-recharging")));
            Assert.That(Row(3).Reading.Value, Is.Zero);
            var commands = new List<BoundUserInterfaceMessage>();
            window.CombatMessage += commands.Add;
            window.WeaponsList[entries[0].NetEntity].Pressed = true;
            window.WeaponsList[entries[^1].NetEntity].Pressed = true;
            window.OnWeaponSelectionChanged?.Invoke();
            var selection = new[] { entries[0].NetEntity, entries[^1].NetEntity };
            Button Named(string name) => Descendants(window).OfType<Button>().Single(button => button.Name == name);
            void Layout(Vector2 size)
            {
                window.SetSize = size;
                foreach (var control in Descendants(window))
                    control.InvalidateMeasure();
                window.Measure(size);
                window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
            }
            try
            {
                foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
                foreach (var size in new[] { new Vector2(960, 600), new Vector2(1180, 780) })
                {
                    settings.SetCVar(WolfgateCVars.UiStyle, theme);
                    window.UpdateStatus(state);
                    Layout(size);
                    var context = $"{theme}/{size}";
                    var (scroll, battery) = WFWeaponListChecks.OneList(window, context);
                    WFWeaponListChecks.NoPaging(window, context);
                    var listed = WFWeaponListChecks.Expected(entries, window.WeaponsList);
                    Assert.That(WFWeaponListChecks.Overflows(scroll, battery), Is.True, $"{context}: a dense battery scrolls instead of paging.");
                    WFWeaponListChecks.Layout(battery, listed, context);
                    WFWeaponListChecks.Compact(battery, listed, context);
                    foreach (var button in listed)
                    {
                        WFWeaponListChecks.Readable(button, ui, context);
                        Assert.That(button.Children.OfType<WFWeaponRow>().Single().NameLabel.Align, Is.EqualTo(Label.AlignMode.Left),
                            "Long names must preserve their identifying prefix.");
                    }
                    WFWeaponListChecks.Reachable(scroll, listed, () => Layout(size), context);
                    Assert.That(window.WeaponsList.Where(weapon => weapon.Value.Pressed).Select(weapon => weapon.Key), Is.EquivalentTo(selection),
                        "Scrolling and theme changes must retain every selected weapon.");
                    scroll.VScroll = battery.ContentHeight;
                    Layout(size);
                    var offset = scroll.VScroll;
                    Assert.That(offset, Is.GreaterThan(0), context);
                    window.UpdateStatus(state);
                    Layout(size);
                    Assert.That(scroll.VScroll, Is.EqualTo(offset), "Supply updates must leave the scroll position in place.");
                    scroll.VScroll = 0;
                    Layout(size);
                    Assert.That(commands, Is.Empty, "Scrolling, snapshots and restyling must not send gunnery commands.");
                    var batteryPanel = Descendants(window).Single(control => control.Name == "WfWeaponBattery");
                    Assert.That(batteryPanel.GlobalPosition.X - window.GlobalPosition.X, Is.LessThan(40), "The battery must not sit in an unused centered allocation.");
                    Assert.That(batteryPanel.Width, Is.LessThan(size.X * 0.4f), "Weapon controls must remain a sidebar.");
                    Assert.That(window.Radar.Width, Is.GreaterThan(size.X * 0.5f), "The fire director must remain the primary control across most of the console.");
                    Assert.That(window.Radar.Height, Is.GreaterThan(batteryPanel.Height * 0.65f), "Status and support controls must not crowd out the fire director vertically.");
                    var flares = Descendants(window).Single(control => control.Name == "WfCountermeasurePanel");
                    Assert.That(flares.Visible, Is.False, "A ship without launchers must not reserve space for flare controls.");
                }
                WFButtonTestInput.Toggle(Named("CockpitGroupStore"), true);
                Layout(new Vector2(1180, 780));
                WFButtonTestInput.Click(Named("CockpitGroupSave2"));
                var saved = commands.OfType<WFSaveWeaponGroupMessage>().Single();
                Assert.That(saved.Weapons, Is.EquivalentTo(selection), "A standalone group save includes selections anywhere in the list.");
                state.Connected = false;
                window.UpdateStatus(state);
                Assert.That(Row(0).Reading.Value, Is.Null, "Disconnected infinite weapons must not retain live supply telemetry.");
            }
            finally
            {
                settings.SetCVar(WolfgateCVars.UiStyle, originalTheme);
                window.Close();
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WeaponBankIsOneSortedTwoColumnScrollingList()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        await pair.Client.WaitAssertion(() =>
        {
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalTheme = settings.GetCVar(WolfgateCVars.UiStyle);
            using var window = new FireControlWindow();
            window.OpenCentered();
            // Mixed case, a numbered run and four weapons whose names differ only in case, handed over in no particular order.
            var names = new List<(int Id, string Name)>
            {
                (1041, "Hawk"), (1005, "Hawk"), (1020, "hawk"), (1002, "HAWK"),
                (1033, "alpha"), (1011, "Zulu"), (1050, "bravo"), (1008, "Charlie"), (1060, "echo"),
            };
            names.AddRange(Enumerable.Range(1, 30).Select(index => (1100 + index * 7 % 31, $"Battery {index}")));
            names.Add((1200, "Flare"));
            var entries = names.OrderByDescending(item => item.Id % 13).ThenBy(item => item.Id)
                .Select(item => new FireControllableEntry(new NetEntity(item.Id), default, item.Name, 5, true)).ToArray();
            Assert.That(entries, Has.Length.EqualTo(40));
            var flare = new NetEntity(1200);
            var excluded = new HashSet<NetEntity> { flare };
            var navigation = new NavInterfaceState(250, null, null, new(), default);
            var state = new FireControlConsoleBoundInterfaceState(true, entries, navigation);
            state.Combat.FlareLaunchers.Add(flare);
            window.UpdateStatus(state);
            var commands = new List<BoundUserInterfaceMessage>();
            window.CombatMessage += commands.Add;
            Button Named(string name) => Descendants(window).OfType<Button>().Single(button => button.Name == name);
            NetEntity IdOf(Button button) => window.WeaponsList.Single(weapon => weapon.Value == button).Key;
            void Layout(Vector2 size)
            {
                window.SetSize = size;
                foreach (var control in Descendants(window))
                    control.InvalidateMeasure();
                window.Measure(size);
                window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
            }
            try
            {
                foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
                foreach (var size in new[] { new Vector2(960, 600), new Vector2(1180, 780) })
                {
                    var context = $"{theme}/{size}";
                    settings.SetCVar(WolfgateCVars.UiStyle, theme);
                    window.UpdateStatus(state);
                    Layout(size);
                    WFWeaponListChecks.NoPaging(window, context);
                    var (scroll, grid) = WFWeaponListChecks.OneList(window, context);
                    var listed = WFWeaponListChecks.Expected(entries, window.WeaponsList, excluded);
                    Assert.That(listed, Has.Count.EqualTo(entries.Length - 1), "Flare launchers stay out of the list.");
                    Assert.That(window.WeaponsList[flare].Visible, Is.False, context);
                    Assert.That(listed.Select(button => button.Text).Take(4), Is.EqualTo(new[] { "alpha", "Battery 1", "Battery 10", "Battery 11" }),
                        $"{context}: case does not split the alphabet.");
                    var hawks = listed.Where(button => string.Equals(button.Text, "hawk", StringComparison.OrdinalIgnoreCase)).ToArray();
                    Assert.That(hawks.Select(button => IdOf(button).Id), Is.EqualTo(new[] { 1002, 1005, 1020, 1041 }),
                        $"{context}: identical names are ordered by entity id.");
                    WFWeaponListChecks.Layout(grid, listed, context);
                    Assert.That(WFWeaponListChecks.Overflows(scroll, grid), Is.True, $"{context}: a long battery scrolls.");
                    Assert.That(scroll.Children.OfType<VScrollBar>().Single().Visible, Is.True, context);
                    WFWeaponListChecks.Reachable(scroll, listed, () => Layout(size), context);

                    var before = listed.Select(button => (button.Position, button.Size)).ToArray();
                    scroll.VScroll = grid.ContentHeight;
                    Layout(size);
                    var offset = scroll.VScroll;
                    Assert.That(offset, Is.GreaterThan(0), context);
                    var reversed = new FireControlConsoleBoundInterfaceState(true, Enumerable.Reverse(entries).ToArray(), navigation) { Combat = state.Combat };
                    window.UpdateStatus(reversed);
                    Layout(size);
                    Assert.That(grid.Entries(), Is.EqualTo(listed), $"{context}: a snapshot in another order must not reorder the list.");
                    Assert.That(listed.Select(button => (button.Position, button.Size)), Is.EqualTo(before), context);
                    Assert.That(scroll.VScroll, Is.EqualTo(offset), $"{context}: a snapshot must not move the list.");
                    scroll.VScroll = 0;
                    Layout(size);
                }

                settings.SetCVar(WolfgateCVars.UiStyle, WolfgateSkins.Retro.Id);
                var wide = new Vector2(1180, 780);
                Layout(wide);
                var (listScroll, listGrid) = WFWeaponListChecks.OneList(window, "groups");
                var ordered = WFWeaponListChecks.Expected(entries, window.WeaponsList, excluded);
                var first = ordered[0];
                var middle = ordered[ordered.Count / 2];
                var last = ordered[^1];
                var group = new[] { IdOf(first), IdOf(middle), IdOf(last) };
                state.Combat.Groups[1].AddRange(group);
                window.UpdateStatus(state);
                Layout(wide);
                WFButtonTestInput.Click(Named("CockpitGroupRecall1"));
                Assert.That(window.WeaponsList.Where(weapon => weapon.Value.Pressed).Select(weapon => weapon.Key), Is.EquivalentTo(group),
                    "Recalling a group selects its weapons anywhere in the list.");
                Assert.That(Named("CockpitGroupRecall1").Pressed, Is.True);
                listScroll.VScroll = listGrid.ContentHeight;
                Layout(wide);
                Assert.That(last.GlobalPosition.Y + last.Height, Is.LessThanOrEqualTo(listScroll.GlobalPosition.Y + listScroll.Height + 1));
                WFButtonTestInput.Click(last);
                Assert.That(last.Pressed, Is.False, "A weapon at the end of the list deselects like any other.");
                Assert.That(Named("CockpitGroupRecall1").Pressed, Is.False, "The group no longer matches the selection.");
                WFButtonTestInput.Click(last);
                Assert.That(Named("CockpitGroupRecall1").Pressed, Is.True);
                WFButtonTestInput.Toggle(Named("CockpitGroupStore"), true);
                Layout(wide);
                WFButtonTestInput.Click(Named("CockpitGroupSave0"));
                Assert.That(commands.OfType<WFSaveWeaponGroupMessage>().Single().Weapons, Is.EquivalentTo(group), "Saving covers the whole list.");
                window.UpdateStatus(state);
                Assert.That(window.WeaponsList.Where(weapon => weapon.Value.Pressed).Select(weapon => weapon.Key), Is.EquivalentTo(group),
                    "A snapshot keeps the selection.");

                var few = entries.Where(entry => entry.NetEntity != flare).Take(3).ToArray();
                window.UpdateStatus(new FireControlConsoleBoundInterfaceState(true, few, navigation) { Combat = state.Combat });
                Layout(wide);
                Assert.That(listGrid.ContentHeight, Is.GreaterThan(0));
                Assert.That(listScroll.Height, Is.EqualTo(listGrid.ContentHeight).Within(1), "A short list takes only the height it needs.");
                Assert.That(listScroll.Children.OfType<VScrollBar>().Single().Visible, Is.False);
                var alert = Descendants(window).Single(control => control.Name == "WfMissileAlert");
                Assert.That(alert.GlobalPosition.Y - (listScroll.GlobalPosition.Y + listScroll.Height), Is.LessThan(16),
                    "Whatever follows the list moves up under it.");
                WFWeaponListChecks.Layout(listGrid, WFWeaponListChecks.Expected(few, window.WeaponsList), "few");
                window.UpdateStatus(new FireControlConsoleBoundInterfaceState(true, Array.Empty<FireControllableEntry>(), navigation));
                Layout(wide);
                Assert.That(listGrid.ContentHeight, Is.Zero);
                Assert.That(window.WeaponsList, Is.Empty);
            }
            finally
            {
                settings.SetCVar(WolfgateCVars.UiStyle, originalTheme);
                window.Close();
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RealWeaponNamesStayDistinguishableInBothSkins()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        await pair.Client.WaitAssertion(() =>
        {
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var prototypes = pair.Client.ResolveDependency<IPrototypeManager>();
            var factory = pair.Client.ResolveDependency<IComponentFactory>();
            var gun = factory.GetComponentName<ShipGunTypeComponent>();
            var names = prototypes.EnumeratePrototypes<EntityPrototype>()
                .Where(proto => !proto.Abstract && proto.Components.ContainsKey(gun))
                .Select(proto => proto.Name).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToArray();
            Assert.That(names, Has.Length.GreaterThan(30), "The check needs the real ship weapons.");
            var originalTheme = settings.GetCVar(WolfgateCVars.UiStyle);
            using var window = new FireControlWindow();
            window.OpenCentered();
            var entries = names.Select((name, index) => new FireControllableEntry(new NetEntity(2000 + index), default, name, 0, true)).ToArray();
            var state = new FireControlConsoleBoundInterfaceState(true, entries,
                new NavInterfaceState(250, null, null, new(), default));
            try
            {
                foreach (var theme in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
                foreach (var size in new[] { new Vector2(960, 600), new Vector2(1180, 780) })
                {
                    settings.SetCVar(WolfgateCVars.UiStyle, theme);
                    window.UpdateStatus(state);
                    window.SetSize = size;
                    foreach (var control in Descendants(window))
                        control.InvalidateMeasure();
                    window.Measure(size);
                    window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    var shown = entries.Select(entry => (entry.Name, Shown: WFWeaponListChecks.VisiblePrefix(window.WeaponsList[entry.NetEntity], ui))).ToArray();
                    var clashes = shown.GroupBy(item => item.Shown).Where(group => group.Select(item => item.Name).Distinct().Count() > 1)
                        .Select(group => $"'{group.Key}' for {string.Join(" | ", group.Select(item => item.Name))}").ToArray();
                    Assert.That(clashes, Is.Empty, $"{theme}/{size}: weapons with different names must not be clipped to the same text (shortest shown: {shown.Min(item => item.Shown.Length)} characters).");
                }
            }
            finally
            {
                settings.SetCVar(WolfgateCVars.UiStyle, originalTheme);
                window.Close();
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FlaresUseClearLanesDistractEligibleLocksAndRefreshClosedAlerts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        var entities = new List<EntityUid>();
        var seekers = new List<EntityUid>();
        EntityUid consoleUid = default;
        EntityUid launcher = default;
        EntityUid decoy = default;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x < 6; x++)
            for (var y = 0; y < 3; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            EntityUid Spawn(string? prototype, EntityCoordinates coordinates)
            {
                var entity = em.SpawnEntity(prototype, coordinates);
                entities.Add(entity);
                return entity;
            }
            consoleUid = Spawn("ComputerGunneryConsole", map.GridCoords);
            em.GetComponent<ApcPowerReceiverComponent>(consoleUid).Powered = true;
            var actor = Spawn("MobHuman", map.GridCoords);
            var serverUid = Spawn(null, map.GridCoords);
            var server = em.AddComponent<FireControlServerComponent>(serverUid);
            server.ConnectedGrid = map.Grid.Owner;
            server.ProcessingPower = 100;
            server.Consoles.Add(consoleUid);
            em.EnsureComponent<FireControlGridComponent>(map.Grid.Owner).ControllingServer = serverUid;
            var console = em.GetComponent<FireControlConsoleComponent>(consoleUid);
            console.ConnectedServer = serverUid;
            launcher = Spawn("WeaponTurretFlare", new EntityCoordinates(map.Grid.Owner, new Vector2(4.5f, 1.5f)));
            em.GetComponent<FireControllableComponent>(launcher).ControllingServer = serverUid;
            server.Controlled.Add(launcher);
            Spawn("WallSolid", new EntityCoordinates(map.Grid.Owner, new Vector2(4.5f, 0.5f)));
            var fire = em.System<FireControlSystem>();
            Assert.That(fire.WfFlareTarget(launcher, out var lane), Is.True,
                "An inward obstruction must not stop a launcher with a clear outward lane.");
            var transforms = em.System<SharedTransformSystem>();
            var lanePoint = transforms.ToMapCoordinates(em.GetCoordinates(lane)).Position;
            Assert.That(lanePoint.X, Is.GreaterThan(transforms.GetWorldPosition(launcher).X));
            var ui = em.System<SharedUserInterfaceSystem>();
            ui.OpenUi(consoleUid, FireControlConsoleUiKey.Key, actor);
            ui.RaiseUiMessage(consoleUid, FireControlConsoleUiKey.Key, new WFDispenseFlaresMessage { Actor = actor });
            em.System<GunSystem>().Update(1f / 30f);
            Assert.That(em.GetComponent<WFFlareLauncherComponent>(launcher).NextBurst, Is.GreaterThan(TimeSpan.Zero),
                "Manual DISPENSE must really fire through the unobstructed lane.");
            var launched = em.EntityQuery<WFFlareDecoyComponent>(true).ToArray();
            Assert.That(launched, Is.Not.Empty, "The real Sunny ammunition path must mark its emitted countermeasures.");
            foreach (var flare in launched)
                em.DeleteEntity(flare.Owner);
            em.RemoveComponent<GunComponent>(launcher);

            // Only the fixture's emitted flare participates in these range/arc comparisons.
            decoy = Spawn("ShipSunnyFlare", new EntityCoordinates(map.MapUid, new Vector2(10, 0)));
            em.EventBus.RaiseLocalEvent(launcher, new AmmoShotEvent { FiredProjectiles = new List<EntityUid> { decoy } });
            Assert.That(em.GetComponent<WFFlareDecoyComponent>(decoy).ProtectedGrid, Is.EqualTo(map.Grid.Owner));
            for (var i = 0; i < 5; i++)
            {
                var missile = Spawn(null, new EntityCoordinates(map.MapUid, new Vector2(20, 0)));
                seekers.Add(missile);
                var projectile = em.AddComponent<ProjectileComponent>(missile);
                var seeker = em.AddComponent<TargetSeekingComponent>(missile);
                seeker.Launched = true;
                seeker.ScanArc = 90;
                em.System<TargetSeekingSystem>().SetSeekerTarget((missile, seeker), map.Grid.Owner);
                transforms.SetWorldRotation(missile, Angle.FromWorldVec(-Vector2.UnitX));
                if (i == 1) seeker.DetectionRange = 5;
                if (i == 2) transforms.SetWorldRotation(missile, Angle.FromWorldVec(Vector2.UnitY));
                if (i == 3) projectile.Shooter = consoleUid;
                if (i == 4) seeker.SeekingDisabled = true;
            }
            var settings = em.EnsureComponent<WFCombatConsoleComponent>(consoleUid);
            settings.Automatic = false;
            ui.CloseUi(consoleUid, FireControlConsoleUiKey.Key, actor);
            settings.Threats = 99;
            ui.OpenUi(consoleUid, FireControlConsoleUiKey.Key, actor);
            Assert.That(ui.TryGetUiState<FireControlConsoleBoundInterfaceState>(consoleUid, FireControlConsoleUiKey.Key, out var snapshot), Is.True);
            Assert.That(snapshot!.Combat.Threats, Is.EqualTo(3),
                "Reopening must rescan real threats immediately instead of replaying stale cached alerts.");
            ui.CloseUi(consoleUid, FireControlConsoleUiKey.Key, actor);
        });
        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<TargetSeekingComponent>(seekers[0]).CurrentTarget, Is.EqualTo(decoy),
                "A closer emitted flare inside the seeker's arc must distract an already locked missile.");
            foreach (var missile in seekers.Skip(1))
                Assert.That(em.GetComponent<TargetSeekingComponent>(missile).CurrentTarget, Is.EqualTo(map.Grid.Owner),
                    "Out-of-range, rearward, friendly and disabled seekers must keep their existing target.");
            Assert.That(em.System<WFCombatConsoleSystem>().GetState(consoleUid,
                em.GetComponent<FireControlConsoleComponent>(consoleUid)).Threats, Is.EqualTo(2),
                "The distracted missile must disappear from the ship's live lock count.");
            for (var x = 3; x <= 5; x++)
            for (var y = 0; y <= 2; y++)
            {
                if (x == 4 && (y == 0 || y == 1))
                    continue;
                entities.Add(em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid.Owner, new Vector2(x + 0.5f, y + 0.5f))));
            }
            Assert.That(em.System<FireControlSystem>().WfFlareTarget(launcher, out _), Is.False,
                "Countermeasures must not bypass a hull that blocks every firing lane.");
            foreach (var missile in seekers)
                em.DeleteEntity(missile);
            foreach (var entity in entities)
            {
                if (em.EntityExists(entity))
                    em.DeleteEntity(entity);
            }
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
