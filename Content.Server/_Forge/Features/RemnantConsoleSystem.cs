using Content.Shared._Forge.Features;
using Content.Shared._Forge.Features.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Forge.Features;

public sealed class RemnantConsoleSystem : SharedRemnantConsoleSystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDeviceLinkSystem _deviceLink = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RemnantConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<RemnantConsoleComponent, RemnantConsolePressButtonMessage>(OnPress);
        SubscribeLocalEvent<RemnantConsoleComponent, AfterInteractUsingEvent>(OnAfterInteractUsing);
        SubscribeLocalEvent<RemnantConsoleComponent, ExaminedEvent>(OnExamined);
    }

    private void OnUiOpened(EntityUid uid, RemnantConsoleComponent component, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, component);
    }

    private void OnPress(EntityUid uid, RemnantConsoleComponent component, RemnantConsolePressButtonMessage args)
    {
        if (args.Index < 0 || args.Index >= component.Buttons.Count)
            return;

        var button = component.Buttons[args.Index];
        _deviceLink.InvokePort(uid, button.Port);
        _audio.PlayPvs("/Audio/Machines/machine_switch.ogg", uid, AudioParams.Default.WithVolume(-2f));
        UpdateUi(uid, component);
    }

    private void OnAfterInteractUsing(EntityUid uid, RemnantConsoleComponent component, AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        if (!HasComp<NetworkConfiguratorComponent>(args.Used))
            return;

        args.Handled = true;
        _popup.PopupEntity(GetProbeText(component), uid, args.User, PopupType.Medium);
    }

    private void OnExamined(EntityUid uid, RemnantConsoleComponent component, ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("remnant-console-examine-hint"));
    }

    private void UpdateUi(EntityUid uid, RemnantConsoleComponent component)
    {
        var labels = new List<string>(component.Buttons.Count);
        foreach (var button in component.Buttons)
        {
            labels.Add(Loc.TryGetString(button.Label, out var localized) ? localized : button.Label);
        }

        _ui.SetUiState(uid, RemnantConsoleUiKey.Key, new RemnantConsoleUiState(labels));
    }

    private string GetProbeText(RemnantConsoleComponent component)
    {
        if (!string.IsNullOrWhiteSpace(component.Message))
        {
            return Loc.TryGetString(component.Message, out var localized)
                ? localized
                : component.Message;
        }

        if (component.Buttons.Count == 0)
            return Loc.GetString("remnant-console-probe-empty");

        var lines = new List<string> { Loc.GetString("remnant-console-probe-buttons") };
        foreach (var button in component.Buttons)
        {
            var label = Loc.TryGetString(button.Label, out var localized) ? localized : button.Label;
            lines.Add(Loc.GetString("remnant-console-probe-button-line", ("label", label), ("port", button.Port)));
        }

        return string.Join('\n', lines);
    }
}
