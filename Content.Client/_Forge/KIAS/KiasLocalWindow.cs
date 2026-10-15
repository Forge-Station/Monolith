using System.Numerics;
using System.Linq;
using Content.Client.UserInterface.Controls;
using Content.Shared._Forge.KIAS;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Forge.KIAS;

public class KiasLocalWindow : FancyWindow
{
    public event Action<KiasDeviceSettingsMessage>? SettingsChanged;
    public event Action<KiasServiceMode>? ModeChanged;
    public event Action<string>? MessageChanged;
    public event Action? RefreshRequested;
    private string _details = string.Empty;
    private readonly RichTextLabel _wrappedDetails = new();
    private readonly LineEdit _color = new();
    private readonly LineEdit _brightness = new();
    private readonly BoxContainer _logRows = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly LineEdit _room = new();
    private readonly LineEdit _range = new();
    private readonly LineEdit _group = new();
    private readonly LineEdit _message = new();
    private readonly OptionButton _positions = new();
    private readonly OptionButton[] _signals = { new(), new(), new(), new() };
    private readonly CheckBox _locked = new();
    private readonly OptionButton _mode = new();
    private readonly OptionButton _page = new();
    private readonly LineEdit _filter = new();
    private readonly Button _save = new();
    private BoundUserInterfaceState? _state;
    private readonly BoxContainer _box;
    private KiasCoverageNavControl? _radar;

    public KiasLocalWindow()
    {
        SetSize = new Vector2(430, 380);
        MinSize = new Vector2(360, 300);
        Resizable = true;
        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8) };
        _box = box;
        XamlChildren.Add(KiasUi.Panel(box));
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        content.AddChild(_wrappedDetails);
        content.AddChild(_logRows);
        scroll.AddChild(content);
        box.AddChild(scroll);
        _room.PlaceHolder = Loc.GetString("kias-mode-room");
        _group.PlaceHolder = Loc.GetString("kias-mode-group");
        _message.PlaceHolder = Loc.GetString("kias-custom-message");
        _range.PlaceHolder = Loc.GetString("kias-sensor-range");
        _locked.Text = Loc.GetString("kias-lock-registration");
        _color.PlaceHolder = Loc.GetString("kias-light-color");
        _brightness.PlaceHolder = Loc.GetString("kias-light-brightness");
        _filter.PlaceHolder = Loc.GetString("kias-log-filter");
        foreach (var mode in Enum.GetValues<KiasServiceMode>())
        {
            if (mode == KiasServiceMode.Monitor) continue;
            _mode.AddItem(Loc.GetString($"kias-mode-{mode.ToString().ToLowerInvariant()}"), (int) mode);
        }
        _mode.OnItemSelected += args => { _mode.SelectId(args.Id); ModeChanged?.Invoke((KiasServiceMode) args.Id); };
        foreach (var page in Enum.GetValues<KiasDisplayPage>())
            _page.AddItem(Loc.GetString($"kias-page-{page.ToString().ToLowerInvariant()}"), (int) page);
        _page.OnItemSelected += args => _page.SelectId(args.Id);
        _filter.OnTextChanged += _ => { if (_state != null) UpdateState(_state); };
        foreach (var (control, key) in new (Robust.Client.UserInterface.Control, string)[]
                 { (_filter, "kias-log-filter"), (_mode, "kias-service-mode"), (_page, "kias-display-page"),
                     (_room, "kias-mode-room"), (_range, "kias-sensor-range"), (_group, "kias-group-name"),
                     (_message, "kias-custom-message"), (_color, "kias-light-color"), (_brightness, "kias-light-brightness") })
            content.AddChild(KiasUi.Field(control, key));
        content.AddChild(_locked);
        for (var count = 2; count <= 4; count++) _positions.AddItem(count.ToString(), count);
        _positions.OnItemSelected += args => { _positions.SelectId(args.Id); ShowSignals(args.Id); };
        content.AddChild(KiasUi.Field(_positions, "kias-rotary-positions"));
        for (var index = 0; index < _signals.Length; index++)
        {
            var choice = _signals[index];
            for (var signal = 0; signal < 4; signal++) choice.AddItem(Loc.GetString("kias-rotary-signal", ("number", signal + 1)), signal);
            choice.OnItemSelected += args => choice.SelectId(args.Id);
            content.AddChild(KiasUi.Field(choice, $"kias-rotary-position-{index + 1}"));
        }
        _save.Text = Loc.GetString("kias-save");
        _save.OnPressed += _ => Save();
        box.AddChild(_save);
        var refresh = new Button { Text = Loc.GetString("kias-refresh") };
        refresh.OnPressed += _ => RefreshRequested?.Invoke();
        box.AddChild(refresh);
    }

    public void UpdateState(BoundUserInterfaceState state)
    {
        _state = state;
        var geometry = state switch { KiasSensorState sensor => sensor.Geometry, KiasServiceState service => service.Geometry, _ => null };
        if (geometry is { Large: true } && _radar == null)
        {
            _radar = new KiasCoverageNavControl { MinSize = new Vector2(360, 300), VerticalExpand = true };
            _box.AddChild(_radar);
            SetSize = new Vector2(600, 680);
        }
        if (_radar != null)
        {
            _radar.Visible = geometry is { Large: true };
            _radar.SetCoverage(geometry);
        }
        _mode.Visible = state is KiasServiceState;
        _page.Visible = state is KiasWallState;
        _filter.Visible = state is KiasRecorderState;
        _room.Visible = state is KiasLocalState;
        var sensorSettings = state is KiasServiceState { Mode: KiasServiceMode.Diagnose or KiasServiceMode.Monitor, SensorRange: not null };
        _range.Visible = state is KiasSensorState or KiasWirelessState || sensorSettings;
        if (sensorSettings && state is KiasServiceState { SensorRange: { } selectedRange } && !_range.HasKeyboardFocus()) _range.Text = selectedRange.ToString();
        _group.Visible = state is KiasSpeakerState or KiasWirelessState or KiasLightState or KiasServiceState { Mode: KiasServiceMode.Group };
        _color.Visible = _brightness.Visible = state is KiasLightState;
        _logRows.Visible = state is KiasRecorderState;
        _message.Visible = state is KiasSpeakerState or KiasServiceState { Mode: KiasServiceMode.Link or KiasServiceMode.Room };
        _locked.Visible = state is KiasCrewState;
        var rotarySettings = state is KiasServiceState { Mode: KiasServiceMode.Diagnose or KiasServiceMode.Monitor, RotaryPositions: >= 2 };
        _positions.Visible = _positions.Parent!.Visible = rotarySettings;
        if (rotarySettings && state is KiasServiceState rotaryState)
        {
            if (_positions.SelectedId < 2 || _lastRotaryTarget != rotaryState.Target)
            {
                _positions.SelectId(Math.Clamp(rotaryState.RotaryPositions, 2, 4));
                for (var index = 0; index < _signals.Length; index++)
                    _signals[index].SelectId(index < rotaryState.RotarySignals.Count ? rotaryState.RotarySignals[index] : index);
                _lastRotaryTarget = rotaryState.Target;
            }
            ShowSignals(_positions.SelectedId);
        }
        else ShowSignals(0);
        _save.Visible = state is not KiasRecorderState && (state is not KiasServiceState serviceState
            || serviceState.Mode is KiasServiceMode.Link or KiasServiceMode.Room or KiasServiceMode.Group || rotarySettings || sensorSettings);
        if (state is KiasLocalState device)
        {
            Title = device.Name;
            _details = Loc.GetString($"kias-status-{device.Status.ToString().ToLowerInvariant()}");
            if (!_room.HasKeyboardFocus()) _room.Text = device.Room;
        }
        switch (state)
        {
            case KiasWallState wall:
                Title = Loc.GetString("ent-KiasDisplay");
                _details = Loc.GetString("kias-overview-counts", ("devices", wall.Entities), ("crew", wall.Crew)) + "\n" + wall.Details;
                _page.SelectId((int) wall.Page);
                break;
            case KiasRecorderState recorder:
                Title = Loc.GetString("ent-KiasRecorder");
                _details = Loc.GetString(recorder.Online ? "kias-online" : "kias-status-offline");
                _logRows.RemoveAllChildren();
                foreach (var entry in recorder.Entries.Where(entry => entry.Contains(_filter.Text, StringComparison.OrdinalIgnoreCase)))
                    _logRows.AddChild(new Label { Text = entry, ClipText = true, ToolTip = entry, Modulate = entry.Contains("[!]") ? Robust.Shared.Maths.Color.OrangeRed : Robust.Shared.Maths.Color.White });
                break;
            case KiasServiceState service:
                Title = Loc.GetString("ent-KiasServiceTool");
                _mode.SelectId((int) (service.Mode == KiasServiceMode.Monitor ? KiasServiceMode.Diagnose : service.Mode));
                _details = Loc.GetString("kias-service-target", ("source", service.SourceName), ("target", service.TargetName)) + "\n" + service.Details;
                if (service.Mode == KiasServiceMode.Group)
                {
                    if (!_group.HasKeyboardFocus()) _group.Text = service.Group;
                    _details += "\n" + Loc.GetString("kias-service-current-group", ("kind", Loc.GetString($"kias-service-group-{service.GroupKind}")), ("group", service.CurrentGroup));
                }
                if (!_message.HasKeyboardFocus()) _message.Text = service.Message;
                break;
            case KiasScannerState scanner:
                var modules = new[] { KiasScannerModules.Motion, KiasScannerModules.Identity, KiasScannerModules.Biometric, KiasScannerModules.Radiation, KiasScannerModules.Spectral, KiasScannerModules.Connector, KiasScannerModules.Optical, KiasScannerModules.Threat };
                _details += $"\n{Loc.GetString("kias-scanner-modules")}: {string.Join(", ", modules.Where(module => (scanner.Modules & module) != 0).Select(module => Loc.GetString($"kias-module-{module.ToString().ToLowerInvariant()}")))}";
                if (scanner.Geometry is { } room)
                {
                    var status = Loc.TryGetString("kias-room-status-" + room.RoomStatus.ToLowerInvariant(), out var localized) ? localized : room.RoomStatus;
                    _details += "\n" + Loc.GetString(room.RoomStatus == "ExteriorSector" ? "kias-exterior-coverage" : "kias-room-coverage", ("status", status), ("tiles", room.TileCount), ("doors", room.DoorCount), ("revision", room.Revision));
                    if (room.RoomStatus == "ExteriorSector")
                        _details += "\n" + Loc.GetString("kias-exterior-range", ("range", room.Radius));
                }
                break;
            case KiasSensorState sensor:
                _details += $"\n{Loc.GetString("kias-sensor-range")}: {sensor.Range} m / {sensor.Arc}°";
                if (!_range.HasKeyboardFocus()) _range.Text = sensor.Range.ToString();
                break;
            case KiasCrewState crew:
                _details += "\n" + Loc.GetString("kias-crew-detected", ("crew", crew.DetectedCrew)) + "\n" + string.Join("\n", crew.Registered);
                _locked.Pressed = crew.Locked;
                break;
            case KiasSpeakerState speaker:
                _details += "\n" + Loc.GetString("kias-speaker-link-help");
                if (!_group.HasKeyboardFocus()) _group.Text = speaker.Group;
                if (!_message.HasKeyboardFocus()) _message.Text = speaker.Message;
                break;
            case KiasResourceState resource:
                _details += "\n" + resource.Details;
                break;
            case KiasWirelessState wireless:
                _details += "\n" + Loc.GetString("kias-wireless-trusted", ("devices", string.Join(", ", wireless.TrustedTransmitters)));
                if (!_group.HasKeyboardFocus()) _group.Text = wireless.Channel;
                if (!_range.HasKeyboardFocus()) _range.Text = wireless.Range.ToString();
                break;
            case KiasLightState light:
                if (!_group.HasKeyboardFocus()) _group.Text = light.Group;
                if (!_color.HasKeyboardFocus()) _color.Text = light.Color;
                if (!_brightness.HasKeyboardFocus()) _brightness.Text = light.Brightness.ToString();
                break;
        }
        _wrappedDetails.ToolTip = _details;
        _wrappedDetails.SetMessage(Robust.Shared.Utility.FormattedMessage.FromUnformatted(_details));
        foreach (var control in new Robust.Client.UserInterface.Control[] { _filter, _mode, _page, _room, _range, _group, _message, _color, _brightness })
            control.Parent!.Visible = control.Visible;
    }

    private void Save()
    {
        if (_state is KiasServiceState service)
        {
            if (service.Mode is KiasServiceMode.Diagnose or KiasServiceMode.Monitor && service.RotaryPositions >= 2)
            {
                SettingsChanged?.Invoke(new KiasDeviceSettingsMessage { RotaryPositions = _positions.SelectedId,
                    RotarySignals = _signals.Take(_positions.SelectedId).Select(choice => choice.SelectedId).ToList() });
                _lastRotaryTarget = null;
                return;
            }
            if (service.Mode is KiasServiceMode.Diagnose or KiasServiceMode.Monitor && service.SensorRange != null)
            {
                if (float.TryParse(_range.Text, out var selectedRange) && float.IsFinite(selectedRange))
                    SettingsChanged?.Invoke(new KiasDeviceSettingsMessage { Range = selectedRange });
                return;
            }
            MessageChanged?.Invoke(service.Mode == KiasServiceMode.Group ? _group.Text : _message.Text);
            return;
        }
        var range = 0f;
        var brightness = 0.8f;
        if (_brightness.Visible && !float.TryParse(_brightness.Text, out brightness)) return;
        if (_range.Visible && !float.TryParse(_range.Text, out range)) return;
        SettingsChanged?.Invoke(new KiasDeviceSettingsMessage { Room = _room.Text, Group = _group.Text, Color = _color.Text, Brightness = brightness,
            Message = _message.Text, Range = range, LockRegistration = _locked.Pressed, Page = (KiasDisplayPage) _page.SelectedId });
    }

    private NetEntity? _lastRotaryTarget;
    private void ShowSignals(int positions)
    {
        for (var index = 0; index < _signals.Length; index++)
            _signals[index].Visible = _signals[index].Parent!.Visible = index < positions;
    }
}

public sealed class KiasWallWindow : KiasLocalWindow;
public sealed class KiasRecorderWindow : KiasLocalWindow;
public sealed class KiasServiceWindow : KiasLocalWindow;
public sealed class KiasScannerWindow : KiasLocalWindow;
public sealed class KiasSensorWindow : KiasLocalWindow;
public sealed class KiasCrewWindow : KiasLocalWindow;
public sealed class KiasSpeakerWindow : KiasLocalWindow;
