using Content.Client.Shuttles.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Cockpit;
using Content.Shared.Buckle.Components;
using Robust.Client.Player;
using Robust.Client.Replays.Playback;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._WF.Cockpit;

/// <summary>Owns the optional cockpit and returns the normal HUD when piloting eligibility ends.</summary>
public sealed partial class WFCockpitUIController : UIController
{
    /// <summary>How long a helm that opened while its pilot was buckled waits for the piloting state before giving up on entering by itself.</summary>
    private const float AutoEnterWait = 1f;

    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IReplayPlaybackManager _replay = default!;
    private WFCockpitView? _view;
    private ShuttleConsoleWindow? _console;
    private InGameScreen? _screen;
    private EntityUid? _pilot;
    private ShuttleConsoleWindow? _autoEnter;
    private float _autoEnterWaited;

    /// <summary>Whether a cockpit currently owns the screen.</summary>
    public bool Active => _view != null;

    public override void Initialize()
    {
        base.Initialize();
        UIManager.GetUIController<GameplayStateLoadController>().OnScreenUnload += () => Exit();
    }

    /// <summary>Checks seat and helm ownership without changing gameplay state; a replay viewer never enters.</summary>
    public bool CanEnter(EntityUid? console) => UIManager.ActiveScreen is InGameScreen && _replay.Replay == null &&
        _entities.System<SharedWFCockpitSystem>().CanEnter(_player.LocalEntity, console);

    /// <summary>
    /// Called as a helm window opens. With the option on and the local player already buckled, the cockpit is entered
    /// once, as soon as the helm's piloting state arrives; it is not tried again for this window.
    /// </summary>
    public void AutoEnterOnOpen(ShuttleConsoleWindow console)
    {
        _autoEnter = null;
        if (Active || !_config.GetCVar(WolfgateCVars.CockpitAutoEnter) || !IsBuckled(_player.LocalEntity))
            return;
        _autoEnter = console;
        _autoEnterWaited = 0;
    }

    private bool IsBuckled(EntityUid? actor) =>
        actor is { } uid && _entities.TryGetComponent(uid, out BuckleComponent? buckle) && buckle.BuckledTo != null;

    /// <summary>Enters for a helm that opened while buckled once it can, and drops the request when the helm closes, a cockpit opens or the wait ends.</summary>
    private void TryAutoEnter(ShuttleConsoleWindow console, float delta)
    {
        _autoEnterWaited += delta;
        if (Active || console.Disposed || !console.IsOpen)
            _autoEnter = null;
        else if (CanEnter(console.WfCockpitConsole))
        {
            _autoEnter = null;
            Enter(console);
        }
        else if (_autoEnterWaited >= AutoEnterWait)
            _autoEnter = null;
    }

    /// <summary>Rehouses the existing console and world viewport in a cockpit.</summary>
    public bool Enter(ShuttleConsoleWindow console)
    {
        if (Active || !CanEnter(console.WfCockpitConsole) ||
            UIManager.ActiveScreen is not InGameScreen screen || screen.GetWidget<MainViewport>() is not { } viewport)
            return false;
        _console = console;
        _screen = screen;
        _pilot = _player.LocalEntity;
        var lease = new WFCockpitLease();
        try
        {
            _view = new WFCockpitView(console, screen, viewport, () => Exit(), lease);
        }
        catch
        {
            lease.Restore();
            if (console.WfCockpitActive)
                console.WfSetCockpitActive(false);
            _console = null;
            _screen = null;
            _pilot = null;
            throw;
        }
        return true;
    }

    /// <summary>Restores the same chat, viewport and console controls, including unfinished input.</summary>
    public void Exit(ShuttleConsoleWindow? owner = null)
    {
        if (_view == null || owner != null && owner != _console)
            return;
        var view = _view;
        _view = null;
        _console = null;
        _screen = null;
        _pilot = null;
        view.Restore();
    }

    /// <summary>Leaves the cockpit for an Escape press that found no other window to close.</summary>
    public bool ExitOnEscape()
    {
        if (!Active)
            return false;
        Exit();
        return true;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_view != null && (_player.LocalEntity != _pilot || UIManager.ActiveScreen != _screen ||
            !CanEnter(_console?.WfCockpitConsole)))
            Exit();
        if (_autoEnter is { } pending)
            TryAutoEnter(pending, args.DeltaSeconds);
    }
}
