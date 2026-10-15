using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Client.Stylesheets;
using Content.Shared._Forge.KIAS.Controllers;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Utility;

namespace Content.Client._Forge.KIAS.Controllers;

public sealed class KiasControllerWindow : FancyWindow
{
    public event Action<KiasControllerEditMessage>? Edited;
    private readonly KiasGraphCanvas _canvas = new();
    private readonly BoxContainer _palette = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly BoxContainer _settings = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly LineEdit _search = new(), _name = new();
    private readonly Button _previousPage = new(), _nextPage = new();
    private readonly Label _catalogPage = new();
    private readonly Label _status = new() { ClipText = true };
    private readonly OptionButton _presets = new();
    private readonly OptionButton _legacy = new();
    private readonly CheckBox _enabled = new();
    private bool _updating;
    private readonly Button _write, _eject, _discard;
    private KiasControllerEditorState _state = new();
    private bool _pending;
    private readonly HashSet<string> _collapsedRooms = new();
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    private string[] _identifiers = Array.Empty<string>();
    private readonly BoxContainer _sidebar;
    public KiasControllerWindow()
    {
        Title = Loc.GetString("kias-controller-editor-title");
        SetSize = new Vector2(1200, 720); MinSize = new Vector2(850, 500); Resizable = true;
        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8), RectClipContent = true };
        XamlChildren.Add(root);
        var toolbar = new BoxContainer { Margin = new Thickness(4), SeparationOverride = 4 }; root.AddChild(KiasUi.Panel(toolbar));
        _name.HorizontalExpand = true; toolbar.AddChild(_name);
        toolbar.AddChild(Button("kias-controller-rename", () => Send(new() { Edit = KiasGraphEdit.Rename, Text = _name.Text })));
        var operations = new BoxContainer { Margin = new Thickness(4), SeparationOverride = 4 };
        root.AddChild(KiasUi.Panel(operations));
        var templates = new BoxContainer { Margin = new Thickness(4), SeparationOverride = 4 }; root.AddChild(KiasUi.Panel(templates));
        _presets.MinWidth = _legacy.MinWidth = 110;
        _presets.MaxWidth = _legacy.MaxWidth = 180;
        _presets.HorizontalExpand = _legacy.HorizontalExpand = true;
        ClipOptions(_presets); ClipOptions(_legacy);
        templates.AddChild(new Label { Text = Loc.GetString("kias-controller-template"), ToolTip = Loc.GetString("kias-controller-template"), VerticalAlignment = VAlignment.Center });
        templates.AddChild(_presets);
        _presets.OnItemSelected += args => _presets.SelectId(args.Id);
        templates.AddChild(Button("kias-controller-load-preset", () =>
        {
            if (_presets.SelectedId is >= 0 && _presets.SelectedId < _state.Presets.Count)
                Send(new() { Edit = KiasGraphEdit.Preset, Text = _state.Presets[_presets.SelectedId] });
        }));
        templates.AddChild(_legacy); _legacy.OnItemSelected += args => _legacy.SelectId(args.Id);
        templates.AddChild(Button("kias-controller-import-legacy", () =>
        {
            if (_legacy.SelectedId >= 0 && _legacy.SelectedId < _state.Legacy.Count)
                Send(new() { Edit = KiasGraphEdit.ImportLegacy, Node = _legacy.SelectedId });
        }));
        _enabled.Text = Loc.GetString("kias-protocol-enabled"); toolbar.AddChild(_enabled);
        _enabled.OnToggled += _ => { if (!_updating) Send(new() { Edit = KiasGraphEdit.Enabled, Enabled = _enabled.Pressed }); };
        _write = Button("kias-controller-write", () => Send(new() { Edit = KiasGraphEdit.Write })); operations.AddChild(_write);
        _discard = Button("kias-controller-discard", () => Send(new() { Edit = KiasGraphEdit.Discard })); operations.AddChild(_discard);
        _eject = Button("kias-controller-eject", () => Send(new() { Edit = KiasGraphEdit.Eject })); operations.AddChild(_eject);
        operations.AddChild(Button("kias-refresh", () => Send(new() { Edit = KiasGraphEdit.Refresh })));
        root.AddChild(KiasUi.Panel(_status));
        root.AddChild(new Label { Text = Loc.GetString("kias-controller-editor-help"), ClipText = true, ToolTip = Loc.GetString("kias-controller-editor-help") });
        var body = new BoxContainer { VerticalExpand = true }; root.AddChild(body);
        var sidebar = _sidebar = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, MinWidth = 215, MaxWidth = 215, Name = "KiasPalette" };
        body.AddChild(sidebar);
        _search.PlaceHolder = Loc.GetString("kias-controller-search"); sidebar.AddChild(_search);
        _search.OnTextChanged += _ =>
        {
            RebuildPalette();
            Send(new() { Edit = KiasGraphEdit.Catalog, Text = _search.Text, Node = 0 });
        };
        var pages = new BoxContainer { SeparationOverride = 4 };
        _previousPage.Text = Loc.GetString("kias-controller-page-previous");
        _nextPage.Text = Loc.GetString("kias-controller-page-next");
        _previousPage.OnPressed += _ => Send(new() { Edit = KiasGraphEdit.Catalog, Text = _search.Text, Node = Math.Max(0, _state.CatalogPage - 1) });
        _nextPage.OnPressed += _ => Send(new() { Edit = KiasGraphEdit.Catalog, Text = _search.Text, Node = _state.CatalogPage + 1 });
        pages.AddChild(_previousPage); pages.AddChild(_catalogPage); pages.AddChild(_nextPage);
        sidebar.AddChild(pages);
        var paletteScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false }; paletteScroll.AddChild(_palette); sidebar.AddChild(paletteScroll);
        var canvasPanel = KiasUi.Panel(_canvas); canvasPanel.HorizontalExpand = true; body.AddChild(canvasPanel);
        var settingsScroll = new ScrollContainer { MinWidth = 300, MaxWidth = 340, HorizontalExpand = true, HScrollEnabled = false, Name = "KiasInspector" };
        settingsScroll.AddChild(_settings); body.AddChild(KiasUi.Panel(settingsScroll));
        _canvas.Edited += Send; _canvas.Selected += SelectNode;
        _canvas.Feedback += message => { _status.Text = message; _status.ToolTip = message; };
    }
    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var width = Math.Clamp(availableSize.X - 635, 215, 310);
        if (_sidebar.MinWidth != width) _sidebar.MinWidth = _sidebar.MaxWidth = width;
        return base.MeasureOverride(availableSize);
    }
    private static void ClipOptions(Robust.Client.UserInterface.Control control)
    {
        if (control is Label label) label.ClipText = true;
        foreach (var child in control.Children) ClipOptions(child);
    }
    private static Button Button(string key, Action action)
    {
        var button = new Button { Text = Loc.GetString(key) }; button.OnPressed += _ => action(); return button;
    }
    private void Send(KiasControllerEditMessage message)
    {
        if (_pending && message.Edit is not (KiasGraphEdit.Refresh or KiasGraphEdit.Catalog)) return;
        message.Revision = _state.Revision;
        if (message.Edit != KiasGraphEdit.Catalog) { _pending = true; _canvas.CanEdit = false; }
        Edited?.Invoke(message);
    }
    public void UpdateState(KiasControllerEditorState state)
    {
        _state = state; _pending = false;
        _previousPage.Disabled = state.CatalogPage == 0;
        _nextPage.Disabled = (state.CatalogPage + 1) * 256 >= state.CatalogTotal;
        _catalogPage.Text = $"{state.CatalogPage + 1}/{Math.Max(1, (state.CatalogTotal + 255) / 256)}";
        var identifiers = state.Identifiers.Concat(state.Devices.Select(device => device.Identifier)).Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!_identifiers.SequenceEqual(identifiers))
        {
            _identifiers = identifiers;
            _aliases.Clear();
            for (var index = 0; index < identifiers.Length; index++)
            {
                var id = identifiers[index];
                var length = Math.Min(6, id.Length);
                foreach (var neighbor in new[] { index - 1, index + 1 })
                {
                    if (neighbor < 0 || neighbor >= identifiers.Length) continue;
                    var other = identifiers[neighbor];
                    var common = 0;
                    while (common < id.Length && common < other.Length && id[common] == other[common]) common++;
                    length = Math.Max(length, Math.Min(id.Length, common + 1));
                }
                _aliases[id] = id[..length];
            }
        }
        _updating = true; _enabled.Pressed = state.Enabled; _enabled.Disabled = !state.HasCard || !(state.Online || state.Mapping); _updating = false;
        _canvas.CanEdit = state.HasCard && (state.Online || state.Mapping) && state.Editing;
        _write.Disabled = !state.HasCard || !(state.Online || state.Mapping) || state.Errors.Count > 0;
        _discard.Disabled = !state.HasCard || !state.Dirty; _eject.Disabled = !state.HasCard || state.Dirty;
        _name.Text = state.Name; _name.CursorPosition = 0;
        _status.Text = Loc.GetString("kias-controller-editor-status", ("card", Loc.GetString(state.HasCard ? "kias-controller-present" : "kias-controller-absent")),
            ("online", Loc.GetString(state.Mapping ? "kias-controller-mapping" : state.Online ? "kias-status-online" : "kias-status-offline")),
            ("dirty", Loc.GetString(state.Dirty ? "kias-controller-unsaved" : "kias-controller-saved")),
            ("nodes", state.Nodes.Count), ("wires", state.Wires.Count)) + (state.Errors.Count == 0 ? string.Empty : "\n" + string.Join(", ", state.Errors.Select(KiasControllerLabels.Error)));
        _status.ToolTip = _status.Text;
        var selectedPreset = _presets.SelectedId; var selectedLegacy = _legacy.SelectedId;
        _presets.Clear(); _legacy.Clear();
        for (var i = 0; i < state.Presets.Count; i++) _presets.AddItem(Loc.TryGetString($"kias-preset-{state.Presets[i]}", out var presetTitle) ? presetTitle : state.Presets[i], i);
        if (state.Presets.Count > 0) _presets.SelectId(Math.Clamp(selectedPreset, 0, state.Presets.Count - 1));
        for (var i = 0; i < state.Legacy.Count; i++) _legacy.AddItem(Loc.TryGetString($"kias-preset-{state.Legacy[i]}", out var legacyTitle) ? legacyTitle : state.Legacy[i], i);
        if (state.Legacy.Count > 0) _legacy.SelectId(Math.Clamp(selectedLegacy, 0, state.Legacy.Count - 1));
        RebuildPalette(); _canvas.SetState(state);
    }
    private void PaletteItem(string title, KiasNodeKind kind, string profile = "", NetEntity? binding = null, string room = "", BoxContainer? category = null, string secondary = "", string? tooltip = null, bool filtered = false)
    {
        if (!filtered && _search.Text.Length > 0 && !(room + " " + title + " " + secondary).Contains(_search.Text, StringComparison.OrdinalIgnoreCase)) return;
        var button = new Button
        {
            Text = title,
            TextAlign = Label.AlignMode.Left,
            ClipText = true,
            ToolTip = tooltip ?? title,
            Disabled = !_state.HasCard || !(_state.Online || _state.Mapping)
        };
        button.Label.RemoveStyleClass(ContainerButton.StyleClassButton);
        if (secondary.Length > 0)
        {
            button.RemoveChild(button.Label);
            var lines = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 1 };
            lines.AddChild(button.Label);
            var details = new RichTextLabel { Modulate = Color.FromHex("#bacbd8") };
            details.SetMessage(FormattedMessage.FromUnformatted(secondary));
            lines.AddChild(details);
            button.AddChild(lines);
        }
        button.OnKeyBindUp += args =>
        {
            if (args.Function != EngineKeyFunctions.UIClick) return;
            var pixel = args.PointerLocation.Position - _canvas.GlobalPixelPosition;
            var position = pixel.X >= 0 && pixel.Y >= 0 && pixel.X <= _canvas.PixelSize.X && pixel.Y <= _canvas.PixelSize.Y
                ? _canvas.GraphPosition(pixel) : _canvas.Center;
            Send(new() { Edit = KiasGraphEdit.Add, Kind = kind, Profile = profile, Binding = binding, X = position.X, Y = position.Y });
        };
        (category ?? _palette).AddChild(button);
    }
    private void RebuildPalette()
    {
        _palette.RemoveAllChildren();
        foreach (var category in new[] { "logic", "values", "state" })
        {
            _palette.AddChild(new Label { Text = Loc.GetString($"kias-controller-{category}") });
            foreach (var kind in Enum.GetValues<KiasNodeKind>())
            {
                if (KiasGraphCatalog.External(kind)) continue;
                var group = kind is KiasNodeKind.BoolConstant or KiasNodeKind.NumberConstant or KiasNodeKind.StringConstant or KiasNodeKind.EnumConstant
                    ? "values" : kind is KiasNodeKind.Timer or KiasNodeKind.Clock or KiasNodeKind.Latch or KiasNodeKind.StringLatch
                        or KiasNodeKind.Toggle or KiasNodeKind.Counter or KiasNodeKind.Edge or KiasNodeKind.Cooldown ? "state" : "logic";
                if (category == group) PaletteItem(Loc.GetString($"kias-controller-node-{kind.ToString().ToLowerInvariant()}"), kind);
            }
        }
        foreach (var kind in new[] { KiasNodeKind.Any, KiasNodeKind.All })
        {
            _palette.AddChild(new Label { Text = Loc.GetString(kind == KiasNodeKind.Any ? "kias-controller-any" : "kias-controller-all") });
            foreach (var profile in _state.Profiles)
                PaletteItem($"{kind.ToString().ToUpperInvariant()} {KiasControllerLabels.Profile(profile.Id, profile.Ports)}", kind, profile.Id);
        }
        _palette.AddChild(new Label { Text = Loc.GetString("kias-controller-devices"), StyleClasses = { StyleNano.StyleClassLabelHeading }, ClipText = true });
        foreach (var room in _state.Devices.OrderByDescending(device => device.NamedRoom).ThenBy(device => device.NamedRoom ? device.Room : string.Empty, StringComparer.CurrentCulture).ThenBy(device => device.RoomOrder)
                     .GroupBy(device => device.Room))
        {
            var matching = room.Where(device => $"{device.Room} {device.Name} #{device.Identifier} {device.Profile}".Contains(_search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matching.Length == 0) continue;
            var rows = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(14, 0, 0, 0) };
            var body = new CollapsibleBody(); body.AddChild(rows);
            var heading = new CollapsibleHeading(room.Key); heading.Label.ClipText = true; heading.ToolTip = room.Key;
            heading.Label.Modulate = Color.FromHex("#bacbd8");
            var section = new Collapsible(heading, body) { Margin = new Thickness(10, 0, 0, 0), BodyVisible = _search.Text.Length > 0 || !_collapsedRooms.Contains(room.Key) };
            heading.OnToggled += args => { if (_search.Text.Length > 0) return; if (args.Pressed) _collapsedRooms.Remove(room.Key); else _collapsedRooms.Add(room.Key); };
            _palette.AddChild(section);
            foreach (var device in matching.OrderBy(device => device.Name, StringComparer.CurrentCulture).ThenBy(device => device.Identifier))
            {
                var profile = KiasControllerLabels.Profile(device.Profile, _state.Profiles.First(p => p.Id == device.Profile).Ports);
                var alias = _aliases.GetValueOrDefault(device.Identifier, device.Identifier);
                PaletteItem(device.Name, KiasNodeKind.Specific, device.Profile, device.Entity, room.Key, rows,
                    $"#{alias} · {profile}", $"{device.Name} · #{device.Identifier}\n{profile}", filtered: true);
            }
        }
    }
    private static LineEdit Field(BoxContainer section, string key, string text)
    {
        var field = new LineEdit { Text = text, CursorPosition = 0, Name = key };
        section.AddChild(KiasUi.Field(field, key)); return field;
    }
    public void SelectNode(KiasGraphNodeView? node)
    {
        _settings.RemoveAllChildren();
        if (node == null) { _settings.AddChild(KiasUi.Help(Loc.GetString("kias-controller-inspector"), Loc.GetString("kias-controller-select-help"))); return; }
        var title = KiasUi.Section(_settings, "kias-controller-node-section");
        var name = KiasGraphCatalog.External(node.Kind) ? KiasControllerLabels.Profile(node.Profile, node.Ports)
            : Loc.GetString($"kias-controller-node-{node.Kind.ToString().ToLowerInvariant()}");
        title.AddChild(new Label { Text = $"#{node.Id} · {name}", ClipText = true, ToolTip = name });
        title.AddChild(KiasUi.Help(KiasControllerLabels.Summary(node), KiasGraphCatalog.External(node.Kind)
            ? Loc.GetString("kias-controller-match-summary", ("count", node.Matched)) : Loc.GetString("kias-controller-values-help")));
        LineEdit? room = null, group = null, text = null, number = null, seconds = null;
        CheckBox? boolean = null;
        OptionButton? comparison = null, enumeration = null, domainPicker = null;
        var domain = node.Ports.FirstOrDefault(port => port.Type == KiasPortType.Enum)?.EnumDomain ?? node.Config.EnumDomain;
        if (node.Kind is KiasNodeKind.Any or KiasNodeKind.All)
        {
            var filters = KiasUi.Section(_settings, "kias-controller-selector-section");
            room = Field(filters, "kias-controller-filter-room", node.Room);
            if (node.Profile is "Speaker" or "LightController" or "Lighting") group = Field(filters, "kias-controller-filter-group", node.Group);
        }
        if (!KiasGraphCatalog.External(node.Kind) && node.Kind is KiasNodeKind.StringConstant or KiasNodeKind.StringLatch
            or KiasNodeKind.NumberConstant or KiasNodeKind.Counter or KiasNodeKind.BoolConstant or KiasNodeKind.Latch
            or KiasNodeKind.Toggle or KiasNodeKind.Timer or KiasNodeKind.Clock or KiasNodeKind.Cooldown or KiasNodeKind.EnumConstant
            or KiasNodeKind.NumberCompare or KiasNodeKind.BoolCompare or KiasNodeKind.StringCompare or KiasNodeKind.EnumCompare)
        {
            var parameters = KiasUi.Section(_settings, "kias-controller-parameters-section");
            if (node.Kind is KiasNodeKind.StringConstant or KiasNodeKind.StringLatch) text = Field(parameters, "kias-controller-text", node.Config.Text);
            if (node.Kind is KiasNodeKind.NumberConstant or KiasNodeKind.Counter) number = Field(parameters, "kias-controller-number", node.Config.Number.ToString(CultureInfo.InvariantCulture));
            if (node.Kind is KiasNodeKind.Timer or KiasNodeKind.Clock or KiasNodeKind.Cooldown) seconds = Field(parameters, "kias-controller-seconds", node.Config.Seconds.ToString(CultureInfo.InvariantCulture));
            if (node.Kind is KiasNodeKind.BoolConstant or KiasNodeKind.Clock or KiasNodeKind.Latch or KiasNodeKind.Toggle)
            {
                boolean = new CheckBox { Name = "kias-controller-bool", Text = Loc.GetString("kias-controller-initial-state"), Pressed = node.Config.Bool };
                parameters.AddChild(boolean);
            }
            if (node.Kind is KiasNodeKind.NumberCompare or KiasNodeKind.BoolCompare or KiasNodeKind.StringCompare or KiasNodeKind.EnumCompare)
            {
                comparison = new OptionButton { Name = "kias-controller-comparison" };
                foreach (var item in Enum.GetValues<KiasComparison>())
                    if (node.Kind == KiasNodeKind.NumberCompare || item is KiasComparison.Equal or KiasComparison.NotEqual)
                        comparison.AddItem(Loc.GetString($"kias-controller-comparison-{item.ToString().ToLowerInvariant()}"), (int) item);
                comparison.SelectId((int) (node.Kind == KiasNodeKind.NumberCompare || node.Config.Comparison is KiasComparison.Equal or KiasComparison.NotEqual ? node.Config.Comparison : KiasComparison.Equal)); comparison.OnItemSelected += args => comparison.SelectId(args.Id);
                parameters.AddChild(KiasUi.Field(comparison, "kias-controller-comparison"));
            }
            if (node.Kind == KiasNodeKind.EnumConstant)
            {
                domainPicker = new OptionButton { Name = "kias-controller-enum-domain" };
                enumeration = new OptionButton { Name = "kias-controller-enum" };
                foreach (var item in Enum.GetValues<KiasEnumDomain>()) domainPicker.AddItem(Loc.GetString($"kias-controller-domain-{item.ToString().ToLowerInvariant()}"), (int) item);
                domainPicker.SelectId((int) domain);
                void Values()
                {
                    enumeration.Clear();
                    if (domain == KiasEnumDomain.Unspecified) { enumeration.AddItem(Loc.GetString("kias-controller-enum-unselected"), 0); enumeration.Disabled = true; return; }
                    enumeration.Disabled = false;
                    for (var i = 0; i < 4; i++) enumeration.AddItem(KiasControllerLabels.EnumValue(domain, i), i);
                    enumeration.SelectId(Math.Clamp(node.Config.Enum, 0, 3));
                }
                Values(); domainPicker.OnItemSelected += args => { domainPicker.SelectId(args.Id); domain = (KiasEnumDomain) args.Id; Values(); };
                enumeration.OnItemSelected += args => enumeration.SelectId(args.Id);
                parameters.AddChild(KiasUi.Field(domainPicker, "kias-controller-enum-domain"));
                parameters.AddChild(KiasUi.Field(enumeration, "kias-controller-enum"));
            }
        }
        var actions = KiasUi.Section(_settings, "kias-controller-actions-section");
        if (room != null || text != null || number != null || seconds != null || boolean != null || comparison != null || enumeration != null)
            actions.AddChild(Button("kias-save", () =>
            {
                var config = node.Config.Copy();
                if (number != null && !double.TryParse(number.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out config.Number)
                    || seconds != null && !double.TryParse(seconds.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out config.Seconds)
)
                { _status.Text = Loc.GetString("kias-controller-invalid-number"); return; }
                if (enumeration != null && domain == KiasEnumDomain.Unspecified)
                { _status.Text = Loc.GetString("kias-controller-enum-unselected"); _status.ToolTip = _status.Text; return; }
                if (text != null) config.Text = text.Text;
                if (boolean != null) config.Bool = boolean.Pressed;
                if (comparison != null) config.Comparison = (KiasComparison) comparison.SelectedId;
                if (enumeration != null) { config.Enum = enumeration.SelectedId; config.EnumDomain = domain; }
                Send(new() { Edit = KiasGraphEdit.Configure, Node = node.Id, Room = room?.Text ?? "", Group = group?.Text ?? "", Config = config });
            }));
        actions.AddChild(Button("kias-controller-remove-node", () => Send(new() { Edit = KiasGraphEdit.Remove, Node = node.Id })));
        var ports = KiasUi.Section(_settings, "kias-controller-ports-section");
        foreach (var port in node.Ports)
            ports.AddChild(KiasUi.Help(KiasControllerLabels.PortTitle(port), KiasControllerLabels.Description(port)));
    }
}

public sealed class KiasControllerRackWindow : FancyWindow
{
    public event Action<KiasControllerRackMessage>? Changed;
    private readonly BoxContainer _rows = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly BoxContainer _slotRows = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly Label _overview = new() { ClipText = true };
    private readonly List<(Label Caption, Button Toggle, Button Eject)> _slots = new();
    private KiasControllerRackState _state = new();
    public KiasControllerRackWindow()
    {
        Title = Loc.GetString("kias-controller-rack-title"); SetSize = new Vector2(650, 420); MinSize = new Vector2(430, 320); Resizable = true;
        var scroll = new ScrollContainer { HScrollEnabled = false }; scroll.AddChild(_rows);
        XamlChildren.Add(KiasUi.Panel(scroll));
        _rows.AddChild(_overview);
        _rows.AddChild(_slotRows);
        var refresh = new Button { Text = Loc.GetString("kias-refresh") };
        refresh.OnPressed += _ => Changed?.Invoke(new() { Refresh = true }); _rows.AddChild(refresh);
    }
    public void UpdateState(KiasControllerRackState state)
    {
        _state = state;
        var overview = Loc.GetString("kias-controller-rack-status", ("online", Loc.GetString(state.Online ? "kias-status-online" : "kias-status-offline")), ("running", state.Running), ("load", state.Load));
        _overview.Text = _overview.ToolTip = overview;
        while (_slots.Count > state.Slots.Count)
        {
            _slotRows.Children.Last().Dispose();
            _slots.RemoveAt(_slots.Count - 1);
        }
        while (_slots.Count < state.Slots.Count)
        {
            var slot = _slots.Count;
            var row = new BoxContainer { Margin = new Thickness(6) }; _slotRows.AddChild(KiasUi.Panel(row));
            var caption = new Label { HorizontalExpand = true, ClipText = true };
            var toggle = new Button();
            var eject = new Button { Text = Loc.GetString("kias-controller-eject") };
            toggle.OnPressed += _ => Changed?.Invoke(new() { Slot = slot, Enabled = !_state.Slots[slot].Enabled });
            eject.OnPressed += _ => Changed?.Invoke(new() { Slot = slot, Eject = true });
            row.AddChild(caption); row.AddChild(toggle); row.AddChild(eject);
            _slots.Add((caption, toggle, eject));
        }
        for (var i = 0; i < state.Slots.Count; i++)
        {
            var item = state.Slots[i];
            var caption = item.Inserted
                ? $"{i + 1}. {item.Name} {(Loc.TryGetString($"kias-controller-status-{item.Status.ToLowerInvariant()}", out var status) ? status : item.Status)} {KiasControllerLabels.Error(item.Fault)}"
                : $"{i + 1}. {Loc.GetString("kias-controller-status-empty")}";
            _slots[i].Caption.Text = _slots[i].Caption.ToolTip = caption;
            _slots[i].Toggle.Text = Loc.GetString(item.Enabled ? "kias-controller-disable" : "kias-controller-enable");
            _slots[i].Toggle.Disabled = _slots[i].Eject.Disabled = !item.Inserted;
        }
    }
}
