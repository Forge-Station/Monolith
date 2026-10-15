using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._Forge.KIAS;
using Content.Client._Forge.KIAS.Controllers;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using System.Globalization;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasControllerLayoutTests
{
    [Test]
    public async Task NativeWindowsArrangeWithEmptyAndPopulatedGraphs()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Client.WaitAssertion(() =>
        {
            using var management = new KiasWindow();
            management.UpdateState(new KiasManagementState { Online = true, Automation = "2 cards" });
            using var programmer = new KiasControllerWindow();
            programmer.UpdateState(new KiasControllerEditorState());
            var populated = new KiasControllerEditorState { Online = true, Editing = true, HasCard = true, Revision = 5, Name = "Layout" };
            populated.Nodes.Add(new() { Id = 1, Kind = KiasNodeKind.OnStart, Ports = KiasGraphCatalog.InternalPorts(KiasNodeKind.OnStart) });
            populated.Nodes.Add(new() { Id = 2, Kind = KiasNodeKind.Timer, X = 350, Y = 30, Ports = KiasGraphCatalog.InternalPorts(KiasNodeKind.Timer) });
            populated.Wires.Add(new() { FromNode = 1, FromPort = "Started", ToNode = 2, ToPort = "Trigger" });
            populated.Profiles.Add(new() { Id = "Power" });
            populated.Devices.Add(new() { Name = new string('W', 200), Profile = "Power", Identifier = "ABCDEF012345" });
            populated.Devices.Add(new() { Name = "Collision peer", Profile = "Power", Identifier = "ABCDEF112345" });
            programmer.UpdateState(populated);
            using var rack = new KiasControllerRackWindow();
            var rackState = new KiasControllerRackState();
            for (var i = 0; i < 8; i++) rackState.Slots.Add(new());
            rack.UpdateState(rackState);
            foreach (var window in new Robust.Client.UserInterface.Control[] { management, programmer, rack })
            foreach (var size in new[] { new Vector2(1200, 720), new Vector2(850, 500), new Vector2(1600, 900) })
            {
                window.SetSize = size;
                window.Measure(size);
                window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                Assert.That(float.IsFinite(window.DesiredSize.X) && float.IsFinite(window.DesiredSize.Y), Is.True);
                Assert.That(Descendants(window).OfType<ScrollContainer>().All(scroll => !scroll.HScrollEnabled), Is.True);
                if (window == management)
                {
                    foreach (var header in Descendants(management).Where(control => control.Name is "Status" or "Counts"))
                    {
                        Assert.That(header.Size.X, Is.GreaterThan(100));
                        Assert.That(header.Size.Y, Is.GreaterThan(0));
                        Assert.That(header.GlobalPosition.X, Is.InRange(0f, size.X));
                    }
                }
                if (window == programmer)
                {
                    var deviceButton = Descendants(programmer).OfType<Button>().Single(button => button.Text?.Contains(populated.Devices[0].Name) == true);
                    var palette = deviceButton.Parent!;
                    var ancestor = palette.Parent!;
                    while (ancestor is not ScrollContainer) ancestor = ancestor.Parent!;
                    var scroll = (ScrollContainer) ancestor;
                    Assert.That(deviceButton.Size.X, Is.InRange(100f, 320f));
                    Assert.That(palette.Size.X, Is.LessThanOrEqualTo(scroll.Size.X));
                    Assert.That(deviceButton.Label.GlobalPosition.X, Is.InRange(scroll.GlobalPosition.X, scroll.GlobalPosition.X + scroll.Size.X));
                    Assert.That(deviceButton.Label.Size.X, Is.GreaterThan(100));
                    Assert.That(deviceButton.Label.Align, Is.EqualTo(Label.AlignMode.Left));
                    Assert.That(deviceButton.Text, Does.StartWith(populated.Devices[0].Name));
                    Assert.That(deviceButton.ToolTip, Does.Contain("ABCDEF012345"));
                    Assert.That(Descendants(deviceButton).OfType<RichTextLabel>().Any(label => label.Text?.Contains("#ABCDEF0") == true), Is.True);
                    Assert.That(Descendants(programmer).OfType<Label>().Any(label => label.Text == Loc.GetString("kias-controller-template")), Is.True);
                    Assert.That(Descendants(programmer).OfType<KiasGraphCanvas>().Single().Size.X, Is.GreaterThan(160));
                }
            }
            populated.Online = false;
            populated.Mapping = true;
            programmer.UpdateState(populated);
            Assert.That(Descendants(programmer).OfType<KiasGraphCanvas>().Single().CanEdit, Is.True);
            Assert.That(Descendants(programmer).OfType<Button>().Single(button => button.Text == Loc.GetString("kias-controller-write")).Disabled, Is.False);
            populated.Mapping = false;
            programmer.UpdateState(populated);
            Assert.That(Descendants(programmer).OfType<KiasGraphCanvas>().Single().CanEdit, Is.False);
            Assert.That(Descendants(programmer).OfType<Button>().Single(button => button.Text == Loc.GetString("kias-controller-write")).Disabled, Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InspectorCapabilitiesSummariesAndLocalWindowsStayReadable()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Client.WaitAssertion(() =>
        {
            using var programmer = new KiasControllerWindow();
            programmer.UpdateState(new KiasControllerEditorState { Online = true, HasCard = true, Editing = true });
            foreach (var kind in Enum.GetValues<KiasNodeKind>())
            {
                var config = new KiasNodeConfig { Text = new string('W', 256), Number = 42, Seconds = 5, EnumDomain = KiasEnumDomain.Alert, Enum = 3 };
                var node = new KiasGraphNodeView { Id = 1, Kind = kind, Config = config, Profile = "Speaker",
                    Ports = KiasGraphCatalog.InternalPorts(kind, config.EnumDomain), Group = "BRIDGE", Room = "Long room" };
                programmer.SelectNode(node);
                var fields = Descendants(programmer).Where(control => control.Name?.StartsWith("kias-controller-") == true).Select(control => control.Name).ToArray();
                Assert.That(fields.Contains("kias-controller-text"), Is.EqualTo(kind is KiasNodeKind.StringConstant or KiasNodeKind.StringLatch));
                Assert.That(fields.Contains("kias-controller-filter-room"), Is.EqualTo(kind is KiasNodeKind.Any or KiasNodeKind.All));
                foreach (var size in new[] { new Vector2(850, 500), new Vector2(1200, 720), new Vector2(1600, 900) })
                {
                    programmer.SetSize = size;
                    programmer.Measure(size); programmer.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    var inspector = Descendants(programmer).Single(control => control.Name == "KiasInspector");
                    Assert.That(inspector.Size.X, Is.InRange(300f, 340f));
                    Assert.That(inspector.GlobalPosition.X + inspector.Size.X, Is.LessThanOrEqualTo(size.X));
                }
                if (kind == KiasNodeKind.NumberConstant) Assert.That(KiasControllerLabels.Summary(node), Is.EqualTo("42"));
                foreach (var port in node.Ports)
                    Assert.That(KiasControllerLabels.PortHelp(port), Does.Contain(KiasControllerLabels.Description(port)));
            }
            var signal = KiasGraphCatalog.Port("Out", KiasPortType.Signal, true);
            var boolean = KiasGraphCatalog.Port("In", KiasPortType.Bool);
            Assert.That(KiasControllerLabels.Incompatibility(signal, boolean), Is.Not.Null);
            Assert.That(KiasControllerLabels.Incompatibility(signal, KiasGraphCatalog.Port("In", KiasPortType.Signal)), Is.Null);
            using var local = new KiasLocalWindow();
            foreach (var state in new BoundUserInterfaceState[] {
                new KiasServiceState { Mode = KiasServiceMode.Group, Group = "BRIDGE", CurrentGroup = "OLD", TargetName = new string('W', 200) },
                new KiasLightState { Name = new string('W', 200), Group = "BRIDGE", Brightness = 1 },
                new KiasRecorderState { Entries = new() { new string('W', 400) } } })
            {
                local.UpdateState(state);
                local.Measure(new Vector2(360, 300)); local.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(360, 300)));
                Assert.That(Descendants(local).OfType<ScrollContainer>().All(scroll => !scroll.HScrollEnabled), Is.True);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RussianEntitiesAndEveryGraphPortHaveLocalizedNamesAndHelp()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        await pair.Client.WaitAssertion(() =>
        {
            var loc = pair.Client.ResolveDependency<ILocalizationManager>();
            loc.SetCulture(CultureInfo.GetCultureInfo("ru-RU"));
            var prototypes = pair.Client.ResolveDependency<IPrototypeManager>();
            foreach (var prototype in prototypes.EnumeratePrototypes<EntityPrototype>().Where(p => !p.Abstract && p.ID.StartsWith("Kias")))
            {
                var name = loc.GetString($"ent-{prototype.ID}");
                Assert.That(name, Does.Contain("KIAS"), prototype.ID);
                Assert.That(name.Any(character => character is >= '\u0400' and <= '\u04ff'), Is.True, prototype.ID);
                Assert.That(loc.GetString($"ent-{prototype.ID}.desc"), Is.Not.EqualTo($"ent-{prototype.ID}.desc"));
            }
            void Check(KiasGraphPort port)
            {
                Assert.That(loc.HasString(port.Name), Is.True, port.Name);
                Assert.That(loc.HasString(port.Description), Is.True, port.Description);
                Assert.That(KiasControllerLabels.PortHelp(port), Does.Not.Contain(port.Description));
            }
            foreach (var profile in prototypes.EnumeratePrototypes<KiasControllerProfilePrototype>())
            {
                Assert.That(profile.Ports.Select(p => p.Description).Distinct().Count(), Is.EqualTo(profile.Ports.Count), profile.ID);
                foreach (var port in KiasGraphCatalog.DevicePorts(profile.Ports)) Check(port);
            }
            foreach (var kind in Enum.GetValues<KiasNodeKind>().Where(k => !KiasGraphCatalog.External(k)))
                foreach (var port in KiasGraphCatalog.InternalPorts(kind)) Check(port);
            foreach (var domain in Enum.GetValues<KiasEnumDomain>().Where(d => d != KiasEnumDomain.Unspecified))
                for (var value = 0; value < 4; value++)
                    Assert.That(KiasControllerLabels.EnumValue(domain, value), Is.Not.EqualTo(value.ToString()));
            Assert.That(KiasControllerLabels.Type(KiasGraphCatalog.Port("Pulse", KiasPortType.Signal)), Is.EqualTo("Импульс"));
        });
        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
