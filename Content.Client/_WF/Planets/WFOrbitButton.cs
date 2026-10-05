using Content.Client._WF.Planets.Flight;
using Content.Shared._WF.Planets.Flight;
using Content.Shared._WF.Planets;
using Content.Shared.Shuttles.Components;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.Planets;

/// <summary>Shuttle console orbit, liftoff and atmosphere controls, read each frame from <see cref="WFConsoleOrbitTargetComponent"/>.</summary>
public sealed partial class WFOrbitButton : BoxContainer
{
    [Dependency] private IEntityManager _entMan = default!;

    private readonly SharedUserInterfaceSystem _ui;

    private readonly Button _liftoffButton;
    private readonly Button _orbitButton;
    private readonly Button _atmosphereButton;
    private readonly Label _liftLabel;
    private readonly Label _decayLabel;

    private WFEnterAtmosphereConfirmWindow? _confirm;
    private WFUnsanctionedOrbitConfirmWindow? _unsanctionedConfirm;

    private EntityUid? _console;

    /// <summary>Lift ratio at or above which the hull flies; matches CEZLevelsSystem.WFFullLiftRatio.</summary>
    private const float FullLift = 1f;

    /// <summary>Lift ratio under which partial lift stops helping; matches CEZLevelsSystem.WFPartialLiftRatio.</summary>
    private const float PartialLift = 0.5f;

    private static readonly Color LiftGood = Color.FromHex("#3fe05a");
    private static readonly Color LiftMarginal = Color.FromHex("#ffd23f");
    private static readonly Color LiftBad = Color.FromHex("#ff3030");

    public WFOrbitButton()
    {
        IoCManager.InjectDependencies(this);
        _ui = _entMan.System<SharedUserInterfaceSystem>();

        Orientation = LayoutOrientation.Vertical;
        // Never hide this container: hidden controls get no FrameUpdate, so only the children hide.

        _liftoffButton = new Button
        {
            TextAlign = Label.AlignMode.Center,
            Visible = false,
        };
        _liftoffButton.StyleClasses.Add("ButtonSquare");
        _liftoffButton.OnPressed += OnLiftoffPressed;

        _orbitButton = new Button
        {
            TextAlign = Label.AlignMode.Center,
        };
        _orbitButton.StyleClasses.Add("ButtonSquare");
        _orbitButton.OnPressed += OnOrbitPressed;

        _atmosphereButton = new Button
        {
            TextAlign = Label.AlignMode.Center,
            Visible = false,
        };
        _atmosphereButton.StyleClasses.Add("ButtonSquare");
        _atmosphereButton.OnPressed += OnAtmospherePressed;

        // Keep the original row budget; demand and mode details live in the tooltip.
        _liftLabel = new Label
        {
            Align = Label.AlignMode.Center,
            ClipText = true,
            MouseFilter = MouseFilterMode.Pass,
            Visible = false,
        };

        _decayLabel = new Label
        {
            Align = Label.AlignMode.Center,
            Visible = false,
        };

        AddChild(_liftoffButton);
        AddChild(_orbitButton);
        AddChild(_atmosphereButton);
        AddChild(_liftLabel);
        AddChild(_decayLabel);
    }

    /// <summary>Binds this control to the console whose interface it sits in.</summary>
    public void SetConsole(EntityUid? console)
    {
        _console = console;
    }

    /// <inheritdoc/>
    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!_entMan.TryGetComponent<WFConsoleOrbitTargetComponent>(_console, out var target))
        {
            _liftoffButton.Visible = false;
            _orbitButton.Visible = false;
            _atmosphereButton.Visible = false;
            _liftLabel.Visible = false;
            _decayLabel.Visible = false;
            return;
        }

        var liftoffVisible = target.LiftoffAvailable || target.LiftoffActive;
        _liftoffButton.Visible = liftoffVisible;
        _liftoffButton.Disabled = target.Busy;
        _liftoffButton.Text = Loc.GetString(target.LiftoffActive
            ? "wf-shuttle-console-cancel-liftoff"
            : "wf-shuttle-console-liftoff");

        if (liftoffVisible)
        {
            _orbitButton.Visible = false;
            _atmosphereButton.Visible = false;
            _decayLabel.Visible = false;
            _liftLabel.Visible = true;
            UpdateLiftDisplay(target);
            return;
        }

        _orbitButton.Visible = true;
        _orbitButton.Disabled = target.Busy || target.Planet == null;

        var planet = target.PlanetName;

        if (string.IsNullOrEmpty(planet))
        {
            _orbitButton.Text = Loc.GetString("wf-shuttle-console-orbit-none");
            _atmosphereButton.Visible = false;
            _liftLabel.Visible = false;
            _decayLabel.Visible = false;
            return;
        }

        _orbitButton.Text = Loc.GetString(target.InOrbit ? "wf-shuttle-console-leave-orbit" : "wf-shuttle-console-enter-orbit",
            ("planet", planet));

        _atmosphereButton.Visible = target.InOrbit;
        _liftLabel.Visible = target.InOrbit;
        _decayLabel.Visible = target.InOrbit;

        if (!target.InOrbit)
            return;

        // -1 means the hull is holding its own orbit.
        var decaying = target.DecaySeconds >= 0f;

        _decayLabel.Text = decaying
            ? Loc.GetString("wf-shuttle-console-orbit-decaying", ("seconds", MathF.Ceiling(target.DecaySeconds).ToString("F0")))
            : Loc.GetString("wf-shuttle-console-orbit-stable");
        _decayLabel.FontColorOverride = decaying ? LiftBad : LiftGood;

        _atmosphereButton.Disabled = target.Busy;
        _atmosphereButton.Text = Loc.GetString("wf-shuttle-console-enter-atmosphere", ("planet", planet));

        UpdateLiftDisplay(target);
    }

    /// <summary>Updates the lift row shared by atmospheric entry and grounded liftoff.</summary>
    private void UpdateLiftDisplay(WFConsoleOrbitTargetComponent target)
    {
        var lift = Loc.GetString("wf-shuttle-console-lift-ratio", ("ratio", target.LiftRatio.ToString("F2")));
        var power = Loc.GetString("wf-shuttle-console-atmosphere-power",
            ("power", (target.AtmospherePowerDemand / 1000f).ToString("N0")));
        var tooltip = lift + "\n" + power;
        if (target.AtmospherePowerDeficit)
            tooltip += "\n" + Loc.GetString("wf-shuttle-console-atmosphere-power-deficit");
        tooltip += "\n" + Loc.GetString("wf-shuttle-console-atmosphere-mode-tooltip");
        _liftLabel.ToolTip = tooltip;
        _atmosphereButton.ToolTip = tooltip;

        _liftLabel.Text = target.AtmospherePowerDeficit
            ? Loc.GetString("wf-shuttle-console-lift-power-deficit", ("ratio", target.LiftRatio.ToString("F2")))
            : lift;
        _liftLabel.FontColorOverride = target.AtmospherePowerDeficit
            ? LiftBad
            : target.LiftRatio >= FullLift
                    ? LiftGood
                    : target.LiftRatio >= PartialLift
                        ? LiftMarginal
                        : LiftBad;
    }

    /// <summary>Engages or cancels the server-side ascent latch.</summary>
    private void OnLiftoffPressed(BaseButton.ButtonEventArgs args)
    {
        if (_console is not { } console
            || !_entMan.TryGetComponent<WFConsoleOrbitTargetComponent>(console, out var target)
            || target.Busy
            || (!target.LiftoffAvailable && !target.LiftoffActive))
        {
            return;
        }

        _ui.ClientSendUiMessage(console,
            ShuttleConsoleUiKey.Key,
            new WFLiftoffMessage(_entMan.GetNetEntity(console)));
    }

    /// <summary>Asks the server for the hop; every gate is re-checked there, so a stale button can only be refused.</summary>
    private void OnOrbitPressed(BaseButton.ButtonEventArgs args)
    {
        if (_console is not { } console
            || !_entMan.TryGetComponent<WFConsoleOrbitTargetComponent>(console, out var target)
            || target.Busy)
        {
            return;
        }

        var netConsole = _entMan.GetNetEntity(console);

        if (target.InOrbit)
        {
            _ui.ClientSendUiMessage(console, ShuttleConsoleUiKey.Key, new WFLeavePlanetOrbitMessage(netConsole));
            return;
        }

        if (target.Planet is not { } planet)
            return;

        if (!target.Unsanctioned)
        {
            _ui.ClientSendUiMessage(console, ShuttleConsoleUiKey.Key, new WFEnterPlanetOrbitMessage(netConsole, planet));
            return;
        }

        _unsanctionedConfirm ??= new WFUnsanctionedOrbitConfirmWindow();
        _unsanctionedConfirm.Ask(target.PlanetName,
            () => _ui.ClientSendUiMessage(console, ShuttleConsoleUiKey.Key, new WFEnterPlanetOrbitMessage(netConsole, planet, true)));
    }

    /// <summary>Drops out of orbit, confirming first on low lift or a power deficit; the server enforces the same gate.</summary>
    private void OnAtmospherePressed(BaseButton.ButtonEventArgs args)
    {
        if (_console is not { } console
            || !_entMan.TryGetComponent<WFConsoleOrbitTargetComponent>(console, out var target)
            || target.Busy
            || !target.InOrbit)
        {
            return;
        }

        var netConsole = _entMan.GetNetEntity(console);

        if (target.LiftRatio >= FullLift && !target.AtmospherePowerDeficit)
        {
            _ui.ClientSendUiMessage(console, ShuttleConsoleUiKey.Key, new WFEnterAtmosphereMessage(netConsole, false));
            return;
        }

        _confirm ??= new WFEnterAtmosphereConfirmWindow();
        _confirm.Ask(target.PlanetName, target.LiftRatio, target.AtmospherePowerDemand, target.AtmospherePowerDeficit,
            () => _ui.ClientSendUiMessage(console, ShuttleConsoleUiKey.Key, new WFEnterAtmosphereMessage(netConsole, true)));
    }
}
