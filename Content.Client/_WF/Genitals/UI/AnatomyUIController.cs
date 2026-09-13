using System.Diagnostics.CodeAnalysis;
using Content.Client._Common.Consent;
using Content.Client.Gameplay;
using Content.Client.Lobby;
using Content.Client.Options.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.EscapeMenu;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared._Common.Consent;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Prototypes;
using Content.Shared.Input;
using Content.Shared.Preferences;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;
using Robust.Shared.Input.Binding;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client._WF.Genitals.UI;

/// <summary>
/// The Anatomy panel: the top-bar button that takes the consent cog's slot for opted-in players, the keybind, the window
/// and the one-time adult content notice. Panel changes go to the server as owner requests for the local entity.
/// </summary>
[UsedImplicitly]
public sealed partial class AnatomyUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>,
    IOnSystemChanged<GenitalsVisualizerSystem>
{
    [Dependency] private IClientConsentManager _consentManager = default!;
    [Dependency] private IClientPreferencesManager _prefs = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    /// <summary>Notice version this build shows; wf.anatomy_notice_seen below it shows the notice.</summary>
    private const int NoticeVersion = 1;

    /// <summary>Minimum time between two arousal requests (5 Hz), well inside the server's request budget (RequestBurst, RequestInterval).</summary>
    private static readonly TimeSpan ArousalSendInterval = TimeSpan.FromSeconds(0.2);

    private AnatomyWindow? _window;
    private AdultContentNoticeWindow? _notice;

    /// <summary>The notice was shown this session. Closing it without OK shows it again next session.</summary>
    private bool _noticeShown;

    private TimeSpan _lastArousalSent;

    /// <summary>Latest arousal value waiting for the send interval to pass.</summary>
    private byte? _pendingArousal;

    private MenuButton? AnatomyButton => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.AnatomyButton;
    private MenuButton? ConsentButton => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.ConsentButton;

    private GenitalSettingsPrototype Settings => GenitalProfileValidator.GetSettings(_proto);

    public override void Initialize()
    {
        base.Initialize();

        _consentManager.OnServerDataLoaded += OnConsentLoaded;
        _cfg.OnValueChanged(WolfgateCVars.AnatomyEnabled, _ => UpdateButtons());
        _player.LocalPlayerAttached += OnLocalPlayerChanged;
        _player.LocalPlayerDetached += OnLocalPlayerChanged;
    }

    public void OnStateEntered(GameplayState state)
    {
        _window = UIManager.CreateWindow<AnatomyWindow>();
        _window.OnOpen += () => SetButtonPressed(true);
        _window.OnClose += () => SetButtonPressed(false);
        _window.OnRevealModeSelected += mode => Send(new AnatomySetRevealModeRequestEvent(mode));
        _window.OnUndergarmentSelected += (slot, worn) => Send(new AnatomySetUndergarmentRequestEvent(slot, worn));
        _window.OnVisibilitySelected += (slot, visibility) => Send(new AnatomySetVisibilityRequestEvent(slot, visibility));
        _window.OnStripConsentSelected += SetStripConsent;
        _window.OnArousalSelected += QueueArousal;
        _window.OnSaveDefaultsPressed += SaveDefaults;
        _window.OnConsentSettingsPressed += OpenConsentSettings;

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OpenAnatomyPanel, InputCmdHandler.FromDelegate(_ => ToggleWindow()))
            .Register<AnatomyUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        _pendingArousal = null;
        if (_window != null)
        {
            // Leaving the state can dispose the window with the rest of the UI before this runs.
            if (!_window.Disposed)
                _window.Orphan();

            _window = null;
        }

        CommandBinds.Unregister<AnatomyUIController>();
    }

    public void OnSystemLoaded(GenitalsVisualizerSystem system)
    {
        system.AnatomyChanged += OnAnatomyChanged;
    }

    public void OnSystemUnloaded(GenitalsVisualizerSystem system)
    {
        system.AnatomyChanged -= OnAnatomyChanged;
    }

    public void LoadButton()
    {
        if (AnatomyButton is { } button)
            button.OnPressed += AnatomyButtonPressed;

        UpdateButtons();
    }

    public void UnloadButton()
    {
        if (AnatomyButton is { } button)
            button.OnPressed -= AnatomyButtonPressed;
    }

    /// <summary>Opted-in players see the Anatomy button in the consent cog's slot; everyone else keeps the cog. Opting out closes the window.</summary>
    public void UpdateButtons()
    {
        var optedIn = IsOptedIn();
        if (AnatomyButton is { } anatomy)
            anatomy.Visible = optedIn;

        if (ConsentButton is { } consent)
            consent.Visible = !optedIn;

        if (optedIn)
        {
            RefreshWindow();
            return;
        }

        _pendingArousal = null;
        if (_window == null)
            return;

        if (_window.IsOpen)
            _window.Close();

        _window.Clear();
    }

    /// <summary>Kill switch on and the local player's saved master switch on. False until the server has sent consent settings.</summary>
    /// <remarks>Reads the CVar, not the system's copy: this also runs from the CVar's own change callback.</remarks>
    private bool IsOptedIn()
    {
        return _cfg.GetCVar(WolfgateCVars.AnatomyEnabled)
               && EntityManager.EntitySysManager.TryGetEntitySystem(out ClientGenitalConsentSystem? consent)
               && consent.ViewerHasMaster();
    }

    private void AnatomyButtonPressed(ButtonEventArgs args)
    {
        ToggleWindow();
    }

    /// <summary>Opens or closes the window. Only opted-in players open it: the keybind follows the same gate as the button.</summary>
    private void ToggleWindow()
    {
        if (_window == null)
            return;

        if (_window.IsOpen)
        {
            AnatomyButton?.SetClickPressed(false);
            _window.Close();
            return;
        }

        if (!IsOptedIn())
        {
            SetButtonPressed(false);
            return;
        }

        AnatomyButton?.SetClickPressed(true);
        RefreshWindow(true);
        _window.OpenCentered();
    }

    private void SetButtonPressed(bool pressed)
    {
        if (AnatomyButton is { } button)
            button.Pressed = pressed;
    }

    private void OnConsentLoaded()
    {
        UpdateButtons();
        TryShowNotice();
    }

    private void OnLocalPlayerChanged(EntityUid uid)
    {
        _pendingArousal = null;
        RefreshWindow();
    }

    /// <summary>The visualizer refreshed an entity; the panel follows the local one (state, prediction, clothing, surgery).</summary>
    private void OnAnatomyChanged(EntityUid uid)
    {
        if (uid == _player.LocalEntity)
            RefreshWindow();
    }

    private void RefreshWindow(bool force = false)
    {
        if (_window == null || (!force && !_window.IsOpen))
            return;

        var body = _player.LocalEntity;
        var canSave = body != null && TryGetDefaultsTarget(body.Value, out _, out _);
        _window.Refresh(body, IsOptedIn(), canSave, GetStripConsent(), _pendingArousal);
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_pendingArousal is { } value && _timing.RealTime - _lastArousalSent >= ArousalSendInterval)
            SendArousal(value);
    }

    /// <summary>Sends at most every ArousalSendInterval; a value that arrives sooner waits, and only the latest one is kept.</summary>
    private void QueueArousal(byte value)
    {
        if (_timing.RealTime - _lastArousalSent >= ArousalSendInterval)
        {
            SendArousal(value);
            return;
        }

        _pendingArousal = value;
    }

    private void SendArousal(byte value)
    {
        _pendingArousal = null;

        // Nothing to change: the last server state already has this value. The predicted value is not compared, since the
        // server may still defer or refuse it.
        if (_player.LocalEntity is { } body
            && EntityManager.TryGetComponent<GenitalsComponent>(body, out var genitals)
            && genitals.ConfirmedArousal == value)
            return;

        _lastArousalSent = _timing.RealTime;
        Send(new AnatomySetArousalRequestEvent(value));
    }

    /// <summary>Raises an owner request, predicted locally and validated again on the server.</summary>
    private void Send<T>(T request) where T : EntityEventArgs
    {
        if (_player.LocalSession == null)
            return;

        EntityManager.RaisePredictiveEvent(request);
    }

    /// <summary>The saved UndergarmentStrip value, or null while consent has not loaded or the toggle is not configured.</summary>
    private bool? GetStripConsent()
    {
        if (!_consentManager.HasLoaded || Settings.StripConsent is not { } strip)
            return null;

        return _consentManager.GetConsentSettings().Toggles.TryGetValue(strip, out var state) && state == "on";
    }

    /// <summary>Flips UndergarmentStrip directly, keeping every other saved consent setting. Revoking puts back removals by others (server).</summary>
    private void SetStripConsent(bool on)
    {
        if (!_consentManager.HasLoaded || Settings.StripConsent is not { } strip)
            return;

        var current = _consentManager.GetConsentSettings();
        var toggles = new Dictionary<ProtoId<ConsentTogglePrototype>, string>(current.Toggles)
        {
            [strip] = on ? "on" : "off",
        };

        _consentManager.UpdateConsent(new PlayerConsentSettings(current.Freetext, toggles));
    }

    private void OpenConsentSettings()
    {
        UIManager.GetUIController<OptionsUIController>().OpenWindow(OptionsMenu.ConsentTabIndex);
    }

    /// <summary>The selected character slot, only when its name matches the attached body's, so defaults never land on another character.</summary>
    private bool TryGetDefaultsTarget(EntityUid body, [NotNullWhen(true)] out HumanoidCharacterProfile? profile, out int slot)
    {
        profile = null;
        slot = 0;

        if (_prefs.Preferences is not { } prefs
            || !prefs.Characters.TryGetValue(prefs.SelectedCharacterIndex, out var selected)
            || selected is not HumanoidCharacterProfile humanoid
            || humanoid.Genitals.LoadFailed
            || !EntityManager.TryGetComponent<MetaDataComponent>(body, out var meta)
            || humanoid.Name != meta.EntityName)
            return false;

        profile = humanoid;
        slot = prefs.SelectedCharacterIndex;
        return true;
    }

    /// <summary>Writes the current reveal mode and per-organ visibility into the selected character's anatomy.</summary>
    private void SaveDefaults()
    {
        if (_player.LocalEntity is not { } body
            || !EntityManager.TryGetComponent<GenitalsComponent>(body, out var genitals)
            || !TryGetDefaultsTarget(body, out var profile, out var slot))
            return;

        var visibility = genitals.Visibility;
        var anatomy = profile.Genitals.WithRevealMode(genitals.RevealMode);
        if (anatomy.Penis is { } penis)
            anatomy = anatomy.WithPenis(penis.With(visibility: visibility.Penis));

        if (anatomy.Testicles is { } testicles)
            anatomy = anatomy.WithTesticles(testicles.With(visibility: visibility.Testicles));

        if (anatomy.Vagina is { } vagina)
            anatomy = anatomy.WithVagina(vagina.With(visibility: visibility.Vagina));

        if (anatomy.Breasts is { } breasts)
            anatomy = anatomy.WithBreasts(breasts.With(visibility: visibility.Breasts));

        _prefs.UpdateCharacter(profile.WithGenitals(anatomy), slot);
        RefreshWindow();
    }

    /// <summary>Shows the adult content notice once the saved master switch is on and this client has not acknowledged it.</summary>
    private void TryShowNotice()
    {
        if (_noticeShown || !IsOptedIn() || _cfg.GetCVar(WolfgateCVars.AnatomyNoticeSeen) >= NoticeVersion)
            return;

        _noticeShown = true;
        _notice = UIManager.CreateWindow<AdultContentNoticeWindow>();
        _notice.OnOpenConsentSettings += OpenConsentSettings;
        _notice.OnAcknowledged += AcknowledgeNotice;
        _notice.OnClose += () => _notice = null;
        _notice.OpenCentered();
    }

    private void AcknowledgeNotice()
    {
        _cfg.SetCVar(WolfgateCVars.AnatomyNoticeSeen, NoticeVersion);
        _cfg.SaveToFile();
        _notice?.Close();
    }
}
