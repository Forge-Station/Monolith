using Content.Shared._Forge.Features;
using Content.Shared._Forge.Features.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Forge.Features;

public sealed class CombinationLockSystem : SharedCombinationLockSystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDeviceLinkSystem _deviceLink = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CombinationLockComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CombinationLockComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<CombinationLockComponent, CombinationLockKeypadMessage>(OnKeypad);
        SubscribeLocalEvent<CombinationLockComponent, CombinationLockEnterMessage>(OnEnter);
        SubscribeLocalEvent<CombinationLockComponent, CombinationLockClearMessage>(OnClear);
        SubscribeLocalEvent<CombinationLockComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(EntityUid uid, CombinationLockComponent component, MapInitEvent args)
    {
        component.Code = SanitizeCode(component.Code);
        if (component.MaxLength < 1)
            component.MaxLength = 1;

        if (component.Code.Length > component.MaxLength)
            component.MaxLength = component.Code.Length;
    }

    private void OnUiOpened(EntityUid uid, CombinationLockComponent component, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, component);
    }

    private void OnKeypad(EntityUid uid, CombinationLockComponent component, CombinationLockKeypadMessage args)
    {
        if (args.Value is < 0 or > 9)
            return;

        if (component.Status != CombinationLockStatus.Idle)
        {
            component.Status = CombinationLockStatus.Idle;
            component.Entered = string.Empty;
        }

        if (component.Entered.Length >= component.MaxLength)
            return;

        component.Entered += args.Value.ToString();
        _audio.PlayPvs(component.KeypadSound, uid, AudioParams.Default.WithVolume(-2f));
        UpdateUi(uid, component);
    }

    private void OnEnter(EntityUid uid, CombinationLockComponent component, CombinationLockEnterMessage args)
    {
        var expected = SanitizeCode(component.Code);
        var success = expected.Length > 0 && component.Entered == expected;

        component.Status = success ? CombinationLockStatus.Success : CombinationLockStatus.Failure;
        _audio.PlayPvs(success ? component.SuccessSound : component.FailureSound, uid);

        if (success)
        {
            _deviceLink.InvokePort(uid, component.SuccessPort);
            _popup.PopupEntity(Loc.GetString("remnant-lock-success"), uid, args.Actor, PopupType.Small);
        }
        else
        {
            _deviceLink.InvokePort(uid, component.FailurePort);
            _popup.PopupEntity(Loc.GetString("remnant-lock-failure"), uid, args.Actor, PopupType.SmallCaution);
        }

        component.Entered = string.Empty;
        UpdateUi(uid, component);
    }

    private void OnClear(EntityUid uid, CombinationLockComponent component, CombinationLockClearMessage args)
    {
        component.Entered = string.Empty;
        component.Status = CombinationLockStatus.Idle;
        _audio.PlayPvs(component.KeypadSound, uid, AudioParams.Default.WithVolume(-2f));
        UpdateUi(uid, component);
    }

    private void OnExamined(EntityUid uid, CombinationLockComponent component, ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("remnant-lock-examine", ("length", SanitizeCode(component.Code).Length)));
    }

    private void UpdateUi(EntityUid uid, CombinationLockComponent component)
    {
        var status = component.Status switch
        {
            CombinationLockStatus.Success => CombinationLockUiStatus.Success,
            CombinationLockStatus.Failure => CombinationLockUiStatus.Failure,
            _ => CombinationLockUiStatus.Idle
        };

        _ui.SetUiState(uid, CombinationLockUiKey.Key,
            new CombinationLockUiState(component.Entered.Length, component.MaxLength, status));
    }

    private static string SanitizeCode(string code)
    {
        if (string.IsNullOrEmpty(code))
            return string.Empty;

        var buffer = new char[code.Length];
        var count = 0;
        foreach (var ch in code)
        {
            if (char.IsDigit(ch))
                buffer[count++] = ch;
        }

        return new string(buffer, 0, count);
    }
}
