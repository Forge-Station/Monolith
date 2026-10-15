using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Content.Server._Forge.KIAS;
using Content.Server._Forge.KIAS.Controllers;
using Content.Server.Power.EntitySystems;
using Content.Server._Mono.FireControl;
using Content.Server._Mono.SpaceArtillery.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared._Mono.Company;
using Content.Shared.Atmos;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Content.Server.Ame.Components;
using Content.Server.Ame.EntitySystems;
using Content.Server.DeviceLinking.Systems;
using Content.Shared.DeviceLinking;
using Content.Shared._Crescent.ShipShields;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Spawners;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Chemistry.Components;
using Content.Shared.Mind;
using Content.Shared._NF.Shipyard.Components;
using Robust.Server.Player;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Client._Forge.KIAS.Controllers;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Localization;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasBriarCrossGateTests
{
    [TestCase("scanners")]
    [TestCase("power-data")]
    [TestCase("speakers")]
    [TestCase("suppression")]
    [TestCase("pdc")]
    [TestCase("ame-adapter")]
    [TestCase("racks")]
    [TestCase("programmer")]
    [TestCase("programmer-ui")]
    [TestCase("docking")]
    public async Task RealBriarDevicesRespondAndRecover(string check)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = check is "programmer" or "programmer-ui", Dirty = true, ServerSeed = 20261009 });
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        var io = em.System<KiasControllerIoSystem>();
        var kias = em.System<KiasSystem>();
        EntityUid grid = default, card = default;
        EntityUid pdcGun = default, programmer = default;
        var evidence = new List<object>();
        var toneSeen = new HashSet<EntityUid>();
        var foamSeen = new HashSet<EntityUid>();
        var fireTiles = new Dictionary<EntityUid, Vector2i>();

        EntityUid[] OnGrid<T>() where T : Component
        {
            var result = new List<EntityUid>();
            var query = em.AllEntityQueryEnumerator<T, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var transform))
                if (transform.GridUid == grid) result.Add(uid);
            return result.ToArray();
        }

        async Task Advance(int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                await pair.RunTicksSync(1);
                await server.WaitAssertion(() =>
                {
                    foreach (var speaker in OnGrid<KiasSpeakerComponent>())
                        if (em.GetComponent<KiasSpeakerComponent>(speaker).Tone is { } tone && em.EntityExists(tone)) toneSeen.Add(speaker);
                    foreach (var foam in OnGrid<SmokeComponent>()) foamSeen.Add(foam);
                });
            }
        }

        void Write(string status, string? error = null)
        {
            var output = Environment.GetEnvironmentVariable("KIAS_LAB_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) return;
            Directory.CreateDirectory(output);
            var mapPath = Path.Combine(KiasTestArtifacts.RepositoryRoot, "Resources/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml");
            File.WriteAllText(Path.Combine(output, $"cross-{check}.json"), JsonSerializer.Serialize(new
            {
                status, error, check, evidence, mapSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(mapPath))).ToLowerInvariant()
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        try
        {
            await server.WaitAssertion(() =>
            {
                maps.CreateMap(out var mapId, runMapInit: false);
                Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(mapId, new ResPath("/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml"), out var loaded), Is.True);
                grid = loaded!.Value.Owner;
                foreach (var uid in OnGrid<KiasControllerCardComponent>()) em.GetComponent<KiasControllerCardComponent>(uid).Enabled = check == "racks";
                card = OnGrid<KiasControllerCardComponent>().First();
                if (check is "programmer" or "programmer-ui")
                    programmer = em.SpawnEntity("KiasControllerProgrammer", check == "programmer-ui"
                        ? new EntityCoordinates(grid, new Vector2(-2.5f, 5.5f))
                        : em.GetComponent<TransformComponent>(OnGrid<KiasControllerRackComponent>().First()).Coordinates);
                if (check == "pdc")
                {
                    var original = OnGrid<SpaceArtilleryComponent>().MaxBy(uid => em.GetComponent<TransformComponent>(uid).LocalPosition.Y);
                    var coordinates = em.GetComponent<TransformComponent>(original).Coordinates;
                    em.DeleteEntity(original);
                    pdcGun = em.SpawnEntity("WeaponTurretL85Autocannon", coordinates);
                }
                if (check is "speakers" or "suppression" or "ame-adapter")
                {
                    var program = new KiasControllerProgram();
                    program.Nodes.Add(new() { Id = 1, Kind = KiasNodeKind.Any, Profile = "Automation" });
                    var targets = check == "speakers" ? OnGrid<KiasSpeakerComponent>() : check == "suppression" ? OnGrid<KiasSuppressionComponent>() : OnGrid<KiasDeviceAdapterComponent>();
                    for (var i = 0; i < targets.Length; i++)
                    {
                        program.Nodes.Add(new() { Id = i + 2, Kind = KiasNodeKind.Specific, Binding = targets[i], Profile = check == "speakers" ? "Speaker" : check == "suppression" ? "Suppression" : "DeviceAdapter" });
                        program.Wires.Add(new() { FromNode = 1, FromPort = "HullDamage", ToNode = i + 2, ToPort = check == "speakers" ? "Alarm" : check == "suppression" ? "Trigger" : "On" });
                    }
                    var component = em.GetComponent<KiasControllerCardComponent>(card);
                    component.Program = program;
                    component.Enabled = true;
                }
                maps.InitializeMap(mapId);
                foreach (var uid in OnGrid<BatteryComponent>()) em.System<BatterySystem>().SetCharge(uid, em.GetComponent<BatteryComponent>(uid).MaxCharge);
            });
            await pair.RunTicksSync(660);
            await server.WaitAssertion(() => Assert.That(OnGrid<KiasDeviceComponent>().All(kias.IsOnline), Is.True));
            if (check == "ame-adapter")
                await server.WaitAssertion(() =>
                {
                    var ame = OnGrid<AmeControllerComponent>().Single();
                    em.System<AmeControllerSystem>().SetInjecting(ame, false);
                    var adapter = OnGrid<KiasDeviceAdapterComponent>().Single(uid => em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "KiasDeviceAdapter");
                    Assert.That(em.System<DeviceLinkSystem>().GetLinks(adapter, ame).Any(link => link.source == "On" && link.sink == "On"), Is.True, "Saved Briar must contain the real AME adapter binding.");
                });

            if (check == "suppression")
                await server.WaitAssertion(() =>
                {
                    var atmos = em.System<AtmosphereSystem>();
                    foreach (var uid in OnGrid<KiasSuppressionComponent>())
                    {
                        var origin = maps.TileIndicesFor((grid, em.GetComponent<MapGridComponent>(grid)), em.GetComponent<TransformComponent>(uid).Coordinates);
                        var forward = em.GetComponent<TransformComponent>(uid).LocalRotation.ToWorldVec();
                        var facing = origin + new Vector2i((int)MathF.Round(forward.X), (int)MathF.Round(forward.Y));
                        var tile = new[] { facing, origin, origin + new Vector2i(0, -1), origin + new Vector2i(1, 0), origin + new Vector2i(0, 1), origin + new Vector2i(-1, 0) }
                            .First(index => atmos.GetTileMixture((grid, null, null), null, index, true) is { } air && air.TotalMoles > 1);
                        var gas = atmos.GetTileMixture((grid, null, null), null, tile, true)!;
                        gas.Clear(); gas.SetMoles(Gas.Oxygen, 20); gas.SetMoles(Gas.Nitrogen, 79); gas.SetMoles(Gas.Plasma, 5); gas.Temperature = 1000;
                        atmos.HotspotExpose((grid, null), tile, 1000, 100);
                        Assert.That(atmos.IsHotspotActive(grid, tile), Is.True);
                        fireTiles.Add(uid, tile);
                    }
                });

            if (check == "programmer-ui")
            {
                EntityUid actor = default, editedCard = default;
                NetEntity programmerNet = default, adapterNet = default;
                await server.WaitAssertion(() =>
                {
                    Assert.That(kias.IsOnline(programmer), Is.True);
                    var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();
                    em.EnsureComponent<ShipOwnershipComponent>(grid).OwnerUserId = session.UserId;
                    var workPosition = em.GetComponent<TransformComponent>(programmer).Coordinates;
                    actor = em.SpawnEntity("MobHuman", new EntityCoordinates(workPosition.EntityId, workPosition.Position + new Vector2(0, -1)));
                    em.System<Content.Server.Damage.Systems.GodmodeSystem>().EnableGodmode(actor);
                    em.System<SharedMindSystem>().TransferTo(em.System<SharedMindSystem>().CreateMind(session.UserId), actor);
                    editedCard = em.SpawnEntity("KiasProgrammableController", em.GetComponent<TransformComponent>(programmer).Coordinates);
                    Assert.That(em.System<ItemSlotsSystem>().TryInsert(programmer, KiasControllerProgrammerComponent.SlotId, editedCard, actor), Is.True);
                    Assert.That(em.System<UserInterfaceSystem>().TryOpenUi(programmer, KiasControllerUiKey.Programmer, actor), Is.True);
                    programmerNet = em.GetNetEntity(programmer);
                    adapterNet = em.GetNetEntity(OnGrid<KiasDeviceAdapterComponent>().Single(uid => em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "KiasDeviceAdapter"));
                });
                await pair.RunTicksSync(20);
                KiasControllerWindow window = default!;
                var uiSize = new Vector2(1200, 720);
                static T Field<T>(object owner, string name) => (T) owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
                static IEnumerable<Control> Children(Control owner)
                {
                    foreach (var child in owner.Children)
                    {
                        yield return child;
                        foreach (var nested in Children(child)) yield return nested;
                    }
                }
                void Click(string key, Button? selectedButton = null)
                {
                    foreach (var control in Children(window)) { control.InvalidateMeasure(); control.InvalidateArrange(); }
                    window.InvalidateMeasure();
                    window.InvalidateArrange();
                    window.Measure(uiSize);
                    window.Arrange(UIBox2.FromDimensions(Vector2.Zero, uiSize));
                    var matches = Children(window).OfType<Button>().Where(control => control.Text == Loc.GetString(key)).ToArray();
                    Assert.That(selectedButton != null || matches.Length == 1, Is.True,
                        key + ": " + string.Join(" | ", Children(window).OfType<Button>().Select(control => control.Text)));
                    var button = selectedButton ?? matches.Single();
                    Assert.That(button.Disabled, Is.False, key);
                    Assert.That(button.Size.X, Is.GreaterThan(1), "Actual button must be arranged: " + key);
                    Assert.That(button.Size.Y, Is.GreaterThan(1), "Actual button must be arranged: " + key);
                    button.MuteSounds = true;
                    var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
                    ui.SetHovered(button);
                    foreach (var (method, state) in new[] { ("KeyBindDown", BoundKeyState.Down), ("KeyBindUp", BoundKeyState.Up) })
                        typeof(BaseButton).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button,
                            new object[] { new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state, new(button.GlobalPixelPosition + Vector2.One, default), true, Vector2.One, Vector2.One) });
                    ui.SetHovered(null);
                    Assert.That(Field<bool>(window, "_pending"), Is.True, "The actual editor button must send a BUI edit: " + key);
                }
                await pair.Client.WaitAssertion(() =>
                {
                    var client = pair.Client.ResolveDependency<IEntityManager>();
                    var bui = client.GetComponent<UserInterfaceComponent>(client.GetEntity(programmerNet)).ClientOpenInterfaces[KiasControllerUiKey.Programmer];
                    window = Field<KiasControllerWindow>(bui, "_editor");
                });
                async Task Select(Predicate<KiasGraphNodeView> predicate)
                {
                    await pair.Client.WaitAssertion(() =>
                    {
                        var canvas = Field<KiasGraphCanvas>(window, "_canvas");
                        var node = Field<KiasControllerEditorState>(window, "_state").Nodes.First(item => predicate(item));
                        var position = (Vector2) typeof(KiasGraphCanvas).GetMethod("Screen", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, new object[] { new Vector2(node.X + 20, node.Y + 20) })!;
                        foreach (var (method, state) in new[] { ("KeyBindDown", BoundKeyState.Down), ("KeyBindUp", BoundKeyState.Up) })
                            typeof(KiasGraphCanvas).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas,
                                new object[] { new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state, new(canvas.GlobalPixelPosition + position, default), true, position, position) });
                    });
                    await pair.RunTicksSync(15);
                }
                foreach (var size in new[] { new Vector2(850, 500), new Vector2(1200, 720), new Vector2(1600, 900) })
                {
                    uiSize = size;
                    var name = $"Длинное русское имя программы {size.X}×{size.Y}";
                    await server.WaitAssertion(() => Assert.That(em.System<UserInterfaceSystem>().IsUiOpen(programmer, KiasControllerUiKey.Programmer, actor), Is.True,
                        "The native UI session must remain open for the editor fixture."));
                    await pair.Client.WaitAssertion(() =>
                    {
                        window.SetSize = size;
                        window.Measure(size);
                        window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                        var state = Field<KiasControllerEditorState>(window, "_state");
                        Field<OptionButton>(window, "_presets").SelectId(state.Presets.IndexOf("battle-flash"));
                        Click("kias-controller-load-preset");
                    });
                    await pair.RunTicksSync(15);
                    await server.WaitAssertion(()
                        => Assert.That(em.GetComponent<KiasControllerProgrammerComponent>(programmer).Draft!.Name, Is.EqualTo("battle-flash")));
                    await pair.Client.WaitAssertion(() =>
                    {
                        var canvas = Field<KiasGraphCanvas>(window, "_canvas");
                        var node = Field<KiasControllerEditorState>(window, "_state").Nodes.First(item => item.Profile == "Automation");
                        var screen = (Vector2) typeof(KiasGraphCanvas).GetMethod("Screen", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, new object[] { new Vector2(node.X + 20, node.Y + 20) })!;
                        var end = screen + new Vector2(20, 20) * canvas.UIScale * Field<float>(canvas, "_zoom");
                        object Key(Vector2 position, BoundKeyState state) => new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state, new(canvas.GlobalPixelPosition + position, default), true, position, position);
                        typeof(KiasGraphCanvas).GetMethod("KeyBindDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, new[] { Key(screen, BoundKeyState.Down) });
                        typeof(KiasGraphCanvas).GetMethod("MouseMove", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas,
                            new object[] { new GUIMouseMoveEventArgs(Vector2.Zero, canvas, end, default, end, end) });
                        typeof(KiasGraphCanvas).GetMethod("KeyBindUp", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, new[] { Key(end, BoundKeyState.Up) });
                    });
                    await pair.RunTicksSync(15);
                    await server.WaitAssertion(() =>
                    {
                        var node = em.GetComponent<KiasControllerProgrammerComponent>(programmer).Draft!.Nodes.First(item => item.Profile == "Automation");
                        Assert.That(node.X, Is.EqualTo(20).Within(.01));
                        Assert.That(node.Y, Is.EqualTo(20).Within(.01));
                    });
                    await Select(node => node.Kind == KiasNodeKind.NumberConstant);
                    await pair.Client.WaitAssertion(() =>
                    {
                        Children(window).OfType<LineEdit>().Single(control => control.Name == "kias-controller-number").Text = "0.25";
                        Click("kias-save");
                    });
                    await pair.RunTicksSync(15);
                    await server.WaitAssertion(()
                        => Assert.That(em.GetComponent<KiasControllerProgrammerComponent>(programmer).Draft!.Nodes.First(node => node.Kind == KiasNodeKind.NumberConstant).Config.Number, Is.EqualTo(.25)));
                    await Select(node => node.Kind == KiasNodeKind.EnumConstant && node.Ports.Any(port => port.EnumDomain == KiasEnumDomain.AudioChannel));
                    await pair.Client.WaitAssertion(() =>
                    {
                        var state = Field<KiasControllerEditorState>(window, "_state");
                        Assert.That(state.Nodes.SelectMany(node => node.Ports).Any(port => port.Type == KiasPortType.Signal), Is.True);
                        Assert.That(state.Nodes.SelectMany(node => node.Ports).Any(port => port.Type == KiasPortType.Bool), Is.True);
                        var picker = Children(window).OfType<OptionButton>().Single(control => control.Name == "kias-controller-enum-domain");
                        Assert.That(picker.SelectedId, Is.EqualTo((int) KiasEnumDomain.AudioChannel));
                        Children(window).OfType<OptionButton>().Single(control => control.Name == "kias-controller-enum").SelectId(2);
                        Click("kias-save");
                    });
                    await pair.RunTicksSync(15);
                    await server.WaitAssertion(()
                        => Assert.That(em.GetComponent<KiasControllerProgrammerComponent>(programmer).Draft!.Nodes.Any(node => node.Kind == KiasNodeKind.EnumConstant && node.Config.EnumDomain == KiasEnumDomain.AudioChannel && node.Config.Enum == 2), Is.True));
                    await Select(node => node.Kind == KiasNodeKind.All && node.Profile == "Speaker");
                    await pair.Client.WaitAssertion(() =>
                    {
                        Children(window).OfType<LineEdit>().Single(control => control.Name == "kias-controller-filter-room").Text = "Тестовая комната";
                        Children(window).OfType<LineEdit>().Single(control => control.Name == "kias-controller-filter-group").Text = "Тестовая группа";
                        Click("kias-save");
                    });
                    await pair.RunTicksSync(15);
                    await server.WaitAssertion(() =>
                    {
                        var node = em.GetComponent<KiasControllerProgrammerComponent>(programmer).Draft!.Nodes.First(item => item.Kind == KiasNodeKind.All && item.Profile == "Speaker");
                        Assert.That(node.Room, Is.EqualTo("Тестовая комната")); Assert.That(node.Group, Is.EqualTo("Тестовая группа"));
                    });
                    await pair.Client.WaitAssertion(() =>
                    {
                        var device = Field<KiasControllerEditorState>(window, "_state").Devices.Single(item => item.Profile == "DeviceAdapter" && item.Entity == adapterNet);
                        Field<LineEdit>(window, "_search").SetText(device.Identifier, invokeEvent: true);
                        var profile = Field<KiasControllerEditorState>(window, "_state").Profiles.Single(item => item.Id == device.Profile);
                        var tooltip = $"{device.Name} · #{device.Identifier}\n{KiasControllerLabels.Profile(device.Profile, profile.Ports)}";
                        var button = Children(Field<BoxContainer>(window, "_palette")).OfType<Button>().Single(control => control.ToolTip == tooltip);
                        Click("SPECIFIC adapter", button);
                    });
                    await pair.RunTicksSync(15);
                    await server.WaitAssertion(() =>
                    {
                        var node = em.GetComponent<KiasControllerProgrammerComponent>(programmer).Draft!.Nodes.Single(item => item.Kind == KiasNodeKind.Specific && item.Profile == "DeviceAdapter");
                        Assert.That(node.Binding, Is.EqualTo(em.GetEntity(adapterNet)));
                    });
                    await pair.Client.WaitAssertion(() =>
                    {
                        Field<LineEdit>(window, "_search").SetText("", invokeEvent: true);
                        var canvas = Field<KiasGraphCanvas>(window, "_canvas");
                        var anchor = canvas.PixelSize / 2;
                        typeof(KiasGraphCanvas).GetMethod("MouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas,
                            new object[] { new GUIMouseWheelEventArgs(new Vector2(0, -10), canvas, anchor, default, anchor, anchor) });
                        var state = Field<KiasControllerEditorState>(window, "_state");
                        Vector2 Port(KiasGraphNodeView node, string id)
                        {
                            var position = (Vector2) typeof(KiasGraphCanvas).GetMethod("PortPosition", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas,
                                new object[] { node, node.Ports.Single(port => port.Id == id) })!;
                            return (Vector2) typeof(KiasGraphCanvas).GetMethod("Screen", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, new object[] { position })!;
                        }
                        var start = Port(state.Nodes.First(node => node.Profile == "Automation"), "Manual");
                        var end = Port(state.Nodes.Single(node => node.Kind == KiasNodeKind.Specific && node.Profile == "DeviceAdapter"), "On");
                        Assert.That(start.X, Is.InRange(0, canvas.PixelSize.X)); Assert.That(start.Y, Is.InRange(0, canvas.PixelSize.Y));
                        Assert.That(end.X, Is.InRange(0, canvas.PixelSize.X)); Assert.That(end.Y, Is.InRange(0, canvas.PixelSize.Y));
                        object Key(Vector2 position, BoundKeyState state) => new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state, new(canvas.GlobalPixelPosition + position, default), true, position, position);
                        typeof(KiasGraphCanvas).GetMethod("KeyBindDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, new[] { Key(start, BoundKeyState.Down) });
                        typeof(KiasGraphCanvas).GetMethod("KeyBindUp", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, new[] { Key(end, BoundKeyState.Up) });
                    });
                    await pair.RunTicksSync(15);
                    await server.WaitAssertion(() =>
                    {
                        var program = em.GetComponent<KiasControllerProgrammerComponent>(programmer).Draft!;
                        var adapter = program.Nodes.Single(node => node.Kind == KiasNodeKind.Specific && node.Profile == "DeviceAdapter");
                        Assert.That(program.Wires.Any(wire => wire.FromPort == "Manual" && wire.ToNode == adapter.Id && wire.ToPort == "On"), Is.True, "A native zoomed connection must attach to the actual selected Signal port.");
                    });
                    await pair.Client.WaitAssertion(() =>
                    {
                        Field<LineEdit>(window, "_name").Text = name;
                        Click("kias-controller-rename");
                    });
                    await pair.RunTicksSync(15);
                    await pair.Client.WaitAssertion(() =>
                    {
                        Assert.That(Field<Button>(window, "_eject").Disabled, Is.True, "The real dirty editor must disable Eject.");
                        Click("kias-controller-write");
                    });
                    await pair.RunTicksSync(15);
                    await server.WaitAssertion(() =>
                    {
                        var stored = em.GetComponent<KiasControllerCardComponent>(editedCard);
                        Assert.That(stored.Program.Name, Is.EqualTo(name));
                        Assert.That(stored.Program.Nodes.First(node => node.Kind == KiasNodeKind.NumberConstant).Config.Number, Is.EqualTo(.25));
                    });
                    await pair.Client.WaitAssertion(() =>
                    {
                        Field<LineEdit>(window, "_name").Text = "Несохранённое изменение";
                        Click("kias-controller-rename");
                    });
                    await pair.RunTicksSync(15);
                    await pair.Client.WaitAssertion(() => Click("kias-controller-discard"));
                    await pair.RunTicksSync(15);
                    await server.WaitAssertion(()
                        => Assert.That(em.GetComponent<KiasControllerProgrammerComponent>(programmer).Draft!.Name, Is.EqualTo(name)));
                    await server.WaitAssertion(() => Assert.That(em.System<UserInterfaceSystem>().IsUiOpen(programmer, KiasControllerUiKey.Programmer, actor), Is.True,
                        $"actor={em.GetComponent<TransformComponent>(actor).Coordinates}; programmer={em.GetComponent<TransformComponent>(programmer).Coordinates}; online={kias.IsOnline(programmer)}; damage={em.GetComponent<DamageableComponent>(actor).TotalDamage}"));
                    await pair.Client.WaitAssertion(() => Click("kias-controller-eject"));
                    await pair.RunTicksSync(15);
                    await server.WaitAssertion(() =>
                    {
                        Assert.That(em.GetComponent<KiasControllerProgrammerComponent>(programmer).Card, Is.Null);
                        Assert.That(em.System<ItemSlotsSystem>().TryInsert(programmer, KiasControllerProgrammerComponent.SlotId, editedCard, actor), Is.True);
                    });
                    await pair.RunTicksSync(15);
                    evidence.Add(new { width = size.X, height = size.Y, actualClientButtons = true, nativeBuiNetwork = true,
                        presetLoaded = true, numericInspectorSaved = true, longRussianNameWritten = true, dirtyEjectDisabled = true, discarded = true, ejectedAndReinserted = true,
                        nativeCanvasDragSaved = true, inferredEnumInspectorSaved = true, signalAndBoolPortsPresent = true, roomAndGroupFiltersSaved = true,
                        nativeDeviceSearchAndSpecificAdapterBinding = true, nativeZoomedSignalConnectionSaved = true,
                        scope = "Connected integration client input handlers and actual server BUI; native desktop renderer captured separately." });
                }
            }
            else if (check == "programmer")
            {
                EntityUid actor = default, editedCard = default;
                await server.WaitAssertion(() =>
                {
                    Assert.That(kias.IsOnline(programmer), Is.True, "The laboratory programmer must use real Briar power and DATA.");
                    var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();
                    em.EnsureComponent<ShipOwnershipComponent>(grid).OwnerUserId = session.UserId;
                    actor = em.SpawnEntity("MobHuman", em.GetComponent<TransformComponent>(programmer).Coordinates);
                    em.System<SharedMindSystem>().TransferTo(em.System<SharedMindSystem>().CreateMind(session.UserId), actor);
                    editedCard = em.SpawnEntity("KiasProgrammableController", em.GetComponent<TransformComponent>(programmer).Coordinates);
                    Assert.That(em.System<ItemSlotsSystem>().TryInsert(programmer, KiasControllerProgrammerComponent.SlotId, editedCard, actor), Is.True);
                    Assert.That(em.System<UserInterfaceSystem>().TryOpenUi(programmer, KiasControllerUiKey.Programmer, actor), Is.True);
                    var draft = em.GetComponent<KiasControllerProgrammerComponent>(programmer);
                    var stored = em.GetComponent<KiasControllerCardComponent>(editedCard);
                    var editor = em.System<KiasControllerUiSystem>();
                    bool Edit(KiasGraphEdit action, string text = "") => editor.Edit((programmer, draft), actor,
                        new() { Edit = action, Text = text, Revision = draft.DraftRevision });
                    var presets = server.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<KiasControllerProgramPrototype>().OrderBy(p => p.ID).ToArray();
                    foreach (var preset in presets)
                    {
                        var previous = stored.Program.Name;
                        Assert.That(Edit(KiasGraphEdit.Preset, preset.ID), Is.True);
                        Assert.That(stored.Program.Name, Is.EqualTo(previous), "Loading a template must keep the physical card unchanged until WRITE.");
                        Assert.That(Edit(KiasGraphEdit.Write), Is.True);
                        Assert.That(stored.Program.Name, Is.EqualTo(preset.Program.Name));
                        Assert.That(stored.Program.Nodes.Count, Is.EqualTo(preset.Program.Nodes.Count));
                    }
                    Assert.That(Edit(KiasGraphEdit.Rename, "Несохранённый черновик"), Is.True);
                    Assert.That(Edit(KiasGraphEdit.Eject), Is.False, "Dirty card must not be ejected silently.");
                    Assert.That(Edit(KiasGraphEdit.Discard), Is.True);
                    Assert.That(draft.Draft!.Name, Is.EqualTo(stored.Program.Name));
                    Assert.That(Edit(KiasGraphEdit.Eject), Is.True);
                    Assert.That(draft.Card, Is.Null);
                    var rack = OnGrid<KiasControllerRackComponent>().First(uid => Enumerable.Range(0, KiasControllerRackComponent.SlotCount)
                        .Any(slot => em.System<SharedContainerSystem>().TryGetContainer(uid, KiasControllerRackComponent.SlotId(slot), out var c) && c.ContainedEntities.Count == 0));
                    em.System<SharedTransformSystem>().SetCoordinates(actor, em.GetComponent<TransformComponent>(rack).Coordinates);
                    var inserted = false;
                    for (var slot = 0; slot < KiasControllerRackComponent.SlotCount && !inserted; slot++)
                        inserted = em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(slot), editedCard, actor);
                    Assert.That(inserted, Is.True);
                    evidence.Add(new { poweredProgrammer = true, loadedAndWrittenPresets = presets.Length, dirtyEjectRejected = true, discardRestoredCard = true, ejectedAndReinsertedInRack = true, scope = "Real powered BUI lease and native server editor API; desktop renderer captured separately." });
                });
                await pair.RunTicksSync(90);
                await server.WaitAssertion(() => Assert.That(em.System<KiasControllerRuntimeSystem>().Running(editedCard), Is.True));
            }
            else if (check == "racks")
            {
                var slots = new List<(EntityUid Rack, string Slot, EntityUid Card)>();
                var runtime = em.System<KiasControllerRuntimeSystem>();
                await server.WaitAssertion(() =>
                {
                    Assert.That(OnGrid<KiasControllerRackComponent>(), Has.Length.EqualTo(4));
                    foreach (var rack in OnGrid<KiasControllerRackComponent>())
                    for (var slot = 0; slot < KiasControllerRackComponent.SlotCount; slot++)
                    {
                        var id = KiasControllerRackComponent.SlotId(slot);
                        Assert.That(em.System<SharedContainerSystem>().TryGetContainer(rack, id, out var container), Is.True);
                        foreach (var installed in container!.ContainedEntities.ToArray())
                        {
                            Assert.That(runtime.Running(installed), Is.True, runtime.Fault(installed));
                            slots.Add((rack, id, installed));
                            Assert.That(em.System<SharedContainerSystem>().RemoveEntity(rack, installed), Is.True);
                        }
                    }
                    Assert.That(slots, Has.Count.EqualTo(27));
                });
                await pair.RunTicksSync(90);
                await server.WaitAssertion(() =>
                {
                    foreach (var entry in slots) Assert.That(runtime.Running(entry.Card), Is.False, "Removed physical cards must have no live runtime.");
                    Assert.That(OnGrid<KiasControllerRackComponent>().Sum(runtime.RunningCount), Is.Zero);
                    foreach (var entry in slots) Assert.That(em.System<ItemSlotsSystem>().TryInsert(entry.Rack, entry.Slot, entry.Card, null), Is.True);
                });
                await pair.RunTicksSync(90);
                await server.WaitAssertion(() =>
                {
                    foreach (var entry in slots) Assert.That(runtime.Running(entry.Card), Is.True, runtime.Fault(entry.Card));
                    Assert.That(OnGrid<KiasControllerRackComponent>().Sum(runtime.RunningCount), Is.EqualTo(27));
                    evidence.Add(new { racks = 4, physicalCards = 27, removedRuntimeCount = 0, reinsertedRuntimeCount = 27 });
                });
                var dispatched = new List<(EntityUid Card, EntityUid Target, string Profile, string Port)>();
                void OnCommand(EntityUid sourceGrid, EntityUid sourceCard, EntityUid target, string profile, string port)
                {
                    if (sourceGrid == grid) dispatched.Add((sourceCard, target, profile, port));
                }
                await server.WaitAssertion(() => io.CommandDispatched += OnCommand);
                try
                {
                    EntityUid hullCard = default;
                    await server.WaitAssertion(() =>
                    {
                        hullCard = OnGrid<KiasControllerCardComponent>().Single(uid => em.GetComponent<KiasControllerCardComponent>(uid).Program.Name == "hull-damage");
                        toneSeen.Clear();
                        var wall = OnGrid<KiasHullStructureComponent>().First(uid => em.TryGetComponent<DamageableComponent>(uid, out var damageable) && damageable.Damage.DamageDict.ContainsKey("Structural"));
                        var before = em.GetComponent<DamageableComponent>(wall).TotalDamage;
                        var damage = new DamageSpecifier();
                        damage.DamageDict.Add("Structural", 50);
                        em.System<DamageableSystem>().TryChangeDamage(wall, damage);
                        Assert.That(em.GetComponent<DamageableComponent>(wall).TotalDamage, Is.GreaterThan(before));
                    });
                    await Advance(120);
                    await server.WaitAssertion(() =>
                    {
                        Assert.That(dispatched, Is.Not.Empty);
                        Assert.That(dispatched.All(command => command.Card == hullCard), Is.True, "Unrelated parallel cards must not react to HullDamage.");
                        var alarms = dispatched.Where(command => command.Profile == "Speaker" && command.Port == "Alarm").ToArray();
                        Assert.That(alarms, Has.Length.EqualTo(10));
                        Assert.That(alarms.Select(command => command.Target).Distinct().Count(), Is.EqualTo(10));
                        Assert.That(toneSeen.Count, Is.EqualTo(10));
                        Assert.That(dispatched.Count(command => command.Profile == "Recorder" && command.Port == "Record"), Is.EqualTo(1));
                        foreach (var entry in slots) Assert.That(runtime.Running(entry.Card), Is.True, runtime.Fault(entry.Card));
                        evidence.Add(new { parallelCards = 27, nativeHullDamage = true, matchingPhysicalCard = hullCard.ToString(), addressedSpeakerCommands = 10, actualSpeakerTones = 10, recorderCommands = 1, unrelatedCardCommands = 0 });
                    });
                }
                finally
                {
                    await server.WaitPost(() => io.CommandDispatched -= OnCommand);
                }
            }
            else if (check == "scanners")
            {
                var scanners = OnGrid<KiasRoomScannerComponent>();
                Assert.That(scanners, Has.Length.EqualTo(21));
                foreach (var scanner in scanners)
                {
                    var topology = em.System<KiasRoomTopologySystem>();
                    if (!topology.TryGetScannerRoom(scanner, out var room, out var roomStatus))
                    {
                        Assert.That(roomStatus, Is.EqualTo(KiasRoomStatus.NoInteriorSeed));
                        Assert.That(em.GetComponent<KiasRoomScannerComponent>(scanner).Entities, Is.Zero);
                        evidence.Add(new { scanner = scanner.ToString(), status = roomStatus.ToString(), mappingActionRequired = true });
                        continue;
                    }
                    var interior = room.Tiles.First();
                    var interiorCoordinates = new EntityCoordinates(grid, interior.X + .5f, interior.Y + .5f);
                    var people = new List<EntityUid>();
                    foreach (var count in new[] { 0, 1, 3, 4 })
                    {
                        await server.WaitAssertion(() =>
                        {
                            while (people.Count < count) people.Add(em.SpawnEntity("BorgChassisGeneric", interiorCoordinates));
                            foreach (var person in people)
                                Assert.That(em.System<KiasCrewSystem>().ScannersCovering(grid, person, KiasScannerModules.Motion), Does.Contain(scanner));
                        });
                        await pair.RunTicksSync(90);
                        await server.WaitAssertion(() =>
                        {
                            var actual = em.GetComponent<KiasRoomScannerComponent>(scanner).Entities;
                            evidence.Add(new { scanner = scanner.ToString(), expected = count, actual });
                            Assert.That(actual, Is.EqualTo(count));
                        });
                    }
                    await server.WaitAssertion(() =>
                    {
                        var traveller = people[0];
                        foreach (var tile in room.Tiles)
                        {
                            em.System<SharedTransformSystem>().SetCoordinates(traveller, new EntityCoordinates(grid, tile.X + .5f, tile.Y + .5f));
                            Assert.That(em.System<KiasCrewSystem>().ScannersCovering(grid, traveller, KiasScannerModules.Motion), Does.Contain(scanner));
                        }
                        em.System<SharedTransformSystem>().SetCoordinates(traveller, new EntityCoordinates(grid, 1000, 1000));
                        Assert.That(em.System<KiasCrewSystem>().ScannersCovering(grid, traveller, KiasScannerModules.Motion), Does.Not.Contain(scanner));
                        evidence.Add(new { scanner = scanner.ToString(), nativeEnterAndExitRoom = true, roomId = room.Id, tiles = room.Tiles.Count });
                    });
                    await server.WaitAssertion(() => { foreach (var person in people) em.DeleteEntity(person); });
                    await pair.RunTicksSync(90);
                    await server.WaitAssertion(() => Assert.That(em.GetComponent<KiasRoomScannerComponent>(scanner).Entities, Is.Zero));
                }
            }
            else if (check == "power-data")
            {
                var receivers = OnGrid<Content.Server.Power.Components.ApcPowerReceiverComponent>();
                var devices = OnGrid<KiasDeviceComponent>();
                await server.WaitAssertion(() =>
                {
                    foreach (var uid in receivers) em.System<PowerReceiverSystem>().TogglePower(uid, playSwitchSound: false, receiver: em.GetComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(uid));
                });
                await pair.RunTicksSync(120);
                await server.WaitAssertion(() =>
                {
                    Assert.That(devices.Where(uid => receivers.Contains(uid)).All(uid => !kias.IsOnline(uid)), Is.True);
                    evidence.Add(new { stage = "native-power-off", receivers = receivers.Length, devices = devices.Length });
                    foreach (var uid in receivers) em.System<PowerReceiverSystem>().TogglePower(uid, playSwitchSound: false, receiver: em.GetComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(uid));
                    foreach (var uid in OnGrid<BatteryComponent>()) em.System<BatterySystem>().SetCharge(uid, em.GetComponent<BatteryComponent>(uid).MaxCharge);
                });
                await pair.RunTicksSync(600);
                var cables = OnGrid<KiasDataCableComponent>();
                var cablePositions = new Dictionary<EntityUid, EntityCoordinates>();
                await server.WaitAssertion(() =>
                {
                    Assert.That(devices.All(kias.IsOnline), Is.True);
                    foreach (var uid in cables)
                    {
                        cablePositions.Add(uid, em.GetComponent<TransformComponent>(uid).Coordinates);
                        em.System<SharedTransformSystem>().Unanchor(uid);
                    }
                });
                await pair.RunTicksSync(120);
                await server.WaitAssertion(() =>
                {
                    Assert.That(devices.Where(uid => !em.HasComponent<KiasCoreComponent>(uid)).All(uid => !kias.IsOnline(uid)), Is.True);
                    evidence.Add(new { stage = "data-disconnected", cables = cables.Length });
                    foreach (var uid in cables)
                        if (em.EntityExists(uid)) Assert.That(em.System<SharedTransformSystem>().AnchorEntity(uid), Is.True);
                        else em.SpawnEntity("KiasDataCable", cablePositions[uid]);
                });
                await pair.RunTicksSync(600);
                await server.WaitAssertion(() => { Assert.That(devices.All(kias.IsOnline), Is.True); evidence.Add(new { stage = "restored", online = devices.Length }); });
            }
            else if (check == "docking")
            {
                EntityUid foreign = default;
                await server.WaitAssertion(() =>
                {
                    var mapId = em.GetComponent<TransformComponent>(grid).MapID;
                    Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(mapId,
                        new ResPath("/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml"), out var loaded), Is.True);
                    foreign = loaded!.Value.Owner;
                    var query = em.AllEntityQueryEnumerator<KiasCoreComponent, TransformComponent>();
                    while (query.MoveNext(out _, out var core, out var transform))
                        if (transform.GridUid == foreign) core.Enabled = false;
                    em.System<SharedTransformSystem>().SetWorldPosition(foreign, new Vector2(1000, 1000));
                    em.System<SharedTransformSystem>().SetWorldRotation(grid, Angle.FromDegrees(45));
                });
                await Advance(60);
                await server.WaitAssertion(() =>
                {
                    Assert.That(em.System<ShuttleSystem>().TryFTLDock(foreign, em.GetComponent<ShuttleComponent>(foreign), grid), Is.True,
                        "Two clean Briar grids must have a valid native docking configuration.");
                    Assert.That(em.System<DockingSystem>().AreGridsDocked(grid, foreign), Is.True);
                });
                await Advance(60);
                await server.WaitAssertion(() =>
                {
                    Assert.That(OnGrid<KiasDeviceComponent>().All(kias.IsOnline), Is.True);
                    var docks = em.System<DockingSystem>().GetDocks(grid).Where(dock => dock.Comp.Docked).ToArray();
                    Assert.That(docks, Is.Not.Empty);
                    evidence.Add(new { nativeRotatedDock = true, ownGrid = grid.ToString(), foreignGrid = foreign.ToString(),
                        dockPairs = docks.Select(dock => new { own = dock.Owner.ToString(), other = dock.Comp.DockedWith?.ToString() }).ToArray(),
                        allOriginalKiasDevicesOnline = true });
                    em.System<DockingSystem>().UndockDocks(grid);
                });
                await Advance(60);
                await server.WaitAssertion(() =>
                {
                    Assert.That(em.System<DockingSystem>().AreGridsDocked(grid, foreign), Is.False);
                    Assert.That(OnGrid<KiasDeviceComponent>().All(kias.IsOnline), Is.True);
                    evidence.Add(new { nativeUndock = true, allOriginalKiasDevicesOnline = true,
                        scope = "Native docking configuration and actual dock joints on two clean Briar grids; not an FTL completion test." });
                });
            }
            else if (check == "pdc")
            {
                await server.WaitAssertion(() =>
                {
                    foreach (var emitter in OnGrid<ShipShieldEmitterComponent>())
                        em.System<SharedPowerReceiverSystem>().SetPowerDisabled(emitter, true);
                });
                await pair.RunTicksSync(60);
                EntityUid projectile = default;
                TimeSpan previousFire = default;
                await server.WaitAssertion(() =>
                {
                    em.System<FireControlSystem>().ForceServerReconnectionOnGrid(grid);
                    Assert.That(kias.IsOnline(pdcGun), Is.True, "Test-only compatible gun must receive real Briar power and DATA.");
                    foreach (var defence in OnGrid<KiasDefenceComponent>()) em.System<KiasDefenceSystem>().SetAutomatic(defence, true);
                    previousFire = em.GetComponent<GunComponent>(pdcGun).LastFire;
                    var location = em.System<SharedTransformSystem>().GetMapCoordinates(pdcGun);
                    projectile = em.SpawnEntity("20mmBullet", new MapCoordinates(location.Position + new Vector2(80, 0), location.MapId));
                    em.RemoveComponent<TimedDespawnComponent>(projectile);
                    var weapon = em.SpawnEntity("WeaponPistolMk58", new MapCoordinates(new Vector2(3000, 3000), location.MapId));
                    em.System<SharedGunSystem>().ShootProjectile(projectile, new Vector2(-1, 0), Vector2.Zero, weapon, speed: 20);
                });
                await Advance(90);
                await server.WaitAssertion(() =>
                {
                    evidence.Add(new { gcs = em.GetComponent<FireControllableComponent>(pdcGun).ControllingServer?.ToString(),
                        canFireWeapons = em.System<FireControlSystem>().CanFireWeapons(grid), radarsOnline = OnGrid<KiasPdcRadarComponent>().Count(kias.IsOnline),
                        gunPowered = em.System<PowerReceiverSystem>().IsPowered(pdcGun),
                        automatic = OnGrid<KiasDefenceComponent>().Select(uid => em.GetComponent<KiasDefenceComponent>(uid).PdcEnabled).ToArray(),
                        clearDirections = em.System<FireControlSystem>().CheckAllDirections(pdcGun).Where(p => p.Value).Select(p => p.Key).ToArray(),
                        autoShootEnabled = em.GetComponent<AutoShootGunComponent>(pdcGun).Enabled,
                        gunCharge = em.TryGetComponent<BatteryComponent>(pdcGun, out var gunBattery) ? (float?)gunBattery.CurrentCharge : null,
                        projectileAlive = em.EntityExists(projectile),
                        projectilePosition = em.EntityExists(projectile) ? em.System<SharedTransformSystem>().GetMapCoordinates(projectile).Position.ToString() : null });
                    Assert.That(em.GetComponent<GunComponent>(pdcGun).LastFire, Is.GreaterThan(previousFire));
                    Assert.That(em.EntityExists(projectile), Is.False, "Native interceptor must destroy the incoming projectile before it reaches the hull.");
                    evidence.Add(new { poweredCompatibleFixture = pdcGun.ToString(), actualShot = true, intercepted = true });
                });
                EntityUid blocker = default;
                await server.WaitAssertion(() =>
                {
                    var position = em.GetComponent<TransformComponent>(pdcGun).LocalPosition + new Vector2(2, 0);
                    var tile = maps.GetTileRef(grid, em.GetComponent<MapGridComponent>(grid), new Vector2i(-5, 0)).Tile;
                    maps.SetTile((grid, em.GetComponent<MapGridComponent>(grid)), new Vector2i((int) MathF.Floor(position.X), (int) MathF.Floor(position.Y)), tile);
                    blocker = em.SpawnEntity("WallPlastitanium", new EntityCoordinates(grid, position));
                    Assert.That(em.GetComponent<TransformComponent>(blocker).Anchored, Is.True);
                    previousFire = em.GetComponent<GunComponent>(pdcGun).LastFire;
                    var location = em.System<SharedTransformSystem>().GetMapCoordinates(pdcGun);
                    projectile = em.SpawnEntity("20mmBullet", new MapCoordinates(location.Position + new Vector2(80, 0), location.MapId));
                    em.RemoveComponent<TimedDespawnComponent>(projectile);
                    var weapon = em.SpawnEntity("WeaponPistolMk58", new MapCoordinates(new Vector2(3000, 3000), location.MapId));
                    em.System<SharedGunSystem>().ShootProjectile(projectile, -Vector2.UnitX, Vector2.Zero, weapon, speed: 20);
                });
                await Advance(30);
                await server.WaitAssertion(() =>
                {
                    Assert.That(em.EntityExists(projectile), Is.True);
                    Assert.That(em.GetComponent<GunComponent>(pdcGun).LastFire, Is.EqualTo(previousFire), "A real wall in the firing lane must prevent automatic fire.");
                    Assert.That(em.System<FireControlSystem>().CheckAllDirections(pdcGun).Any(direction => !direction.Value), Is.True);
                    evidence.Add(new { nativeOcclusionPreventsFire = true, blocker = blocker.ToString() });
                    em.DeleteEntity(blocker);
                });
                await Advance(90);
                await server.WaitAssertion(() =>
                {
                    Assert.That(em.GetComponent<GunComponent>(pdcGun).LastFire, Is.GreaterThan(previousFire));
                    Assert.That(em.EntityExists(projectile), Is.False);
                    evidence.Add(new { nativeOcclusionRemovalRearmsInterception = true });
                });
                var overlapping = new List<EntityUid>();
                var targeted = new HashSet<EntityUid>();
                var shotTimes = new HashSet<TimeSpan>();
                await server.WaitAssertion(() =>
                {
                    previousFire = em.GetComponent<GunComponent>(pdcGun).LastFire;
                    var location = em.System<SharedTransformSystem>().GetMapCoordinates(pdcGun);
                    var weapon = em.SpawnEntity("WeaponPistolMk58", new MapCoordinates(new Vector2(3000, 3000), location.MapId));
                    foreach (var offset in new[] { -.6f, .6f })
                    {
                        var incoming = em.SpawnEntity("20mmBullet", new MapCoordinates(location.Position + new Vector2(80, offset), location.MapId));
                        em.RemoveComponent<TimedDespawnComponent>(incoming);
                        em.System<SharedGunSystem>().ShootProjectile(incoming, -Vector2.UnitX, Vector2.Zero, weapon, speed: 20);
                        overlapping.Add(incoming);
                    }
                });
                for (var tick = 0; tick < 120; tick++)
                {
                    await Advance(1);
                    await server.WaitAssertion(() =>
                    {
                        if (em.GetComponent<KiasPdcWeaponComponent>(pdcGun).Target is { } target) targeted.Add(target);
                        var fire = em.GetComponent<GunComponent>(pdcGun).LastFire;
                        if (fire > previousFire) shotTimes.Add(fire);
                    });
                }
                await server.WaitAssertion(() =>
                {
                    Assert.That(overlapping.All(uid => !em.EntityExists(uid)), Is.True, "Both real incoming projectiles in overlapping directions must be intercepted.");
                    Assert.That(shotTimes.Count, Is.GreaterThanOrEqualTo(2));
                    Assert.That(overlapping.All(targeted.Contains), Is.True);
                    evidence.Add(new { nativeOverlappingTargetsIntercepted = true, targets = overlapping.Select(uid => uid.ToString()).ToArray(), observedFireTimes = shotTimes.Count });
                });
                await Advance(30);
                await server.WaitAssertion(() =>
                {
                    Assert.That(em.GetComponent<KiasPdcWeaponComponent>(pdcGun).Target, Is.Null);
                    previousFire = em.GetComponent<GunComponent>(pdcGun).LastFire;
                    var location = em.System<SharedTransformSystem>().GetMapCoordinates(pdcGun);
                    projectile = em.SpawnEntity("20mmBullet", new MapCoordinates(location.Position + new Vector2(80, 0), location.MapId));
                    em.RemoveComponent<TimedDespawnComponent>(projectile);
                    em.System<SharedGunSystem>().ShootProjectile(projectile, -Vector2.UnitX, Vector2.Zero, pdcGun, user: pdcGun, speed: 20);
                });
                await Advance(30);
                await server.WaitAssertion(() =>
                {
                    Assert.That(em.EntityExists(projectile), Is.True);
                    Assert.That(em.GetComponent<GunComponent>(pdcGun).LastFire, Is.EqualTo(previousFire));
                    Assert.That(em.GetComponent<KiasPdcWeaponComponent>(pdcGun).Target, Is.Not.EqualTo(projectile));
                    evidence.Add(new { nativeOwnGridProjectileExcluded = true });
                    em.DeleteEntity(projectile);
                });
                await server.WaitAssertion(() =>
                {
                    var location = em.System<SharedTransformSystem>().GetMapCoordinates(pdcGun);
                    var friendly = maps.CreateGridEntity(location.MapId);
                    maps.SetTile(friendly, new Vector2i(0, 0), maps.GetTileRef(grid, em.GetComponent<MapGridComponent>(grid), new Vector2i(-5, 0)).Tile);
                    em.EnsureComponent<ShuttleComponent>(friendly);
                    em.EnsureComponent<IFFComponent>(friendly);
                    em.EnsureComponent<ShuttleFactionComponent>(grid).Faction = "NanoTrasen";
                    em.EnsureComponent<ShuttleFactionComponent>(friendly).Faction = "NanoTrasen";
                    em.EnsureComponent<CompanyComponent>(grid).CompanyName = "None";
                    em.EnsureComponent<CompanyComponent>(friendly).CompanyName = "None";
                    em.System<SharedTransformSystem>().SetWorldPosition(friendly, new Vector2(3000, 3000));
                    var weapon = em.SpawnEntity("WeaponPistolMk58", new EntityCoordinates(friendly, new Vector2(.5f, .5f)));
                    Assert.That(em.System<KiasNavigationSystem>().Classify(grid, friendly), Is.EqualTo(KiasContactDisposition.Friendly));
                    previousFire = em.GetComponent<GunComponent>(pdcGun).LastFire;
                    projectile = em.SpawnEntity("20mmBullet", new MapCoordinates(location.Position + new Vector2(80, 0), location.MapId));
                    em.RemoveComponent<TimedDespawnComponent>(projectile);
                    em.System<SharedGunSystem>().ShootProjectile(projectile, -Vector2.UnitX, Vector2.Zero, weapon, speed: 20);
                });
                await Advance(30);
                await server.WaitAssertion(() =>
                {
                    Assert.That(em.EntityExists(projectile), Is.True);
                    Assert.That(em.GetComponent<GunComponent>(pdcGun).LastFire, Is.EqualTo(previousFire), "A native Friendly IFF source must not be intercepted as an enemy.");
                    evidence.Add(new { nativeFriendlyIffProjectileExcluded = true });
                });
            }
            else
            {
                await server.WaitAssertion(() =>
                {
                    Assert.That(em.System<KiasControllerRuntimeSystem>().Running(card), Is.True, em.System<KiasControllerRuntimeSystem>().Fault(card));
                    var wall = OnGrid<KiasHullStructureComponent>().First(uid => em.TryGetComponent<DamageableComponent>(uid, out var d) && d.Damage.DamageDict.ContainsKey("Structural"));
                    var damage = new DamageSpecifier(); damage.DamageDict.Add("Structural", 50);
                    em.System<DamageableSystem>().TryChangeDamage(wall, damage);
                });
                await Advance(check == "ame-adapter" ? 10 : 120);
                await server.WaitAssertion(() =>
                {
                    if (check == "speakers")
                    {
                        var speakers = OnGrid<KiasSpeakerComponent>();
                        Assert.That(speakers, Has.Length.EqualTo(10));
                        Assert.That(toneSeen, Is.EquivalentTo(speakers));
                        evidence.Add(new { addressedSpeakers = speakers.Length, actualTone = toneSeen.Count });
                    }
                    else if (check == "ame-adapter")
                    {
                        var ame = OnGrid<AmeControllerComponent>().Single();
                        var adapter = OnGrid<KiasDeviceAdapterComponent>().Single(uid => em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "KiasDeviceAdapter");
                        Assert.That(em.GetComponent<AmeControllerComponent>(ame).Injecting, Is.True);
                        Assert.That(em.GetComponent<KiasDeviceAdapterComponent>(adapter).State, Is.True);
                        em.System<AmeControllerSystem>().SetInjecting(ame, false);
                        evidence.Add(new { ameStopped = !em.GetComponent<AmeControllerComponent>(ame).Injecting,
                            adapterOnline = kias.IsOnline(adapter), linkedSources = em.GetComponent<DeviceLinkSinkComponent>(ame).LinkedSources.Select(uid => uid.ToString()).ToArray(),
                            adapter = adapter.ToString(), actualFeedback = em.GetComponent<KiasDeviceAdapterComponent>(adapter).State });
                        Assert.That(em.GetComponent<KiasDeviceAdapterComponent>(adapter).State, Is.False);
                        evidence.Add(new { actualAmeInjection = true, nativeManualStopFeedback = true, physicalCardInput = "hull damage", savedLink = "adapter On/Off to AME On/Off" });
                    }
                    else
                    {
                        var suppressors = OnGrid<KiasSuppressionComponent>();
                        Assert.That(suppressors, Has.Length.EqualTo(13));
                        foreach (var uid in suppressors)
                        {
                            Assert.That(em.System<SharedContainerSystem>().TryGetContainer(uid, "kias-cartridge", out var slot), Is.True);
                            Assert.That(slot!.ContainedEntities, Is.Empty);
                            Assert.That(em.System<KiasActuatorSystem>().Suppress(uid), Is.False);
                        }
                        Assert.That(foamSeen.Count, Is.GreaterThanOrEqualTo(13));
                        foreach (var (uid, tile) in fireTiles)
                        {
                            var burning = em.System<AtmosphereSystem>().IsHotspotActive(grid, tile);
                            evidence.Add(new { suppressor = uid.ToString(), fireTile = tile.ToString(), stillBurning = burning });
                            Assert.That(burning, Is.False, $"Real fire next to suppressor {uid} must be extinguished by its native foam.");
                        }
                        evidence.Add(new { suppressors = suppressors.Length, consumed = suppressors.Length, nativeFoam = foamSeen.Count, emptyRepeatRejected = true, stimulus = "native hull damage through physical test card" });
                    }
                });
            }
            await server.WaitAssertion(() => Write("PASS"));
        }
        catch (Exception e)
        {
            await server.WaitPost(() => Write("FAIL", e.Message));
            throw;
        }
        await pair.CleanReturnAsync();
    }
}
