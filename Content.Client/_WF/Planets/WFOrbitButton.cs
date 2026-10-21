using Content.Client._WF.Planets.Flight;
using Content.Client.Stylesheets;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets.Flight;
using Content.Shared._WF.Planets;
using Content.Shared.Shuttles.Components;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.Planets;

/// <summary>
/// Shuttle console orbit, liftoff and atmosphere controls with the flight status and pre-flight checklist,
/// read each frame from <see cref="WFConsoleOrbitTargetComponent"/>.
/// </summary>
public sealed partial class WFOrbitButton : BoxContainer
{
    [Dependency] private IEntityManager _entMan = default!;

    private readonly SharedUserInterfaceSystem _ui;

    private readonly Label _phaseLabel;
    private readonly Label _readyLabel;
    private readonly Button _liftoffButton;
    private readonly Button _orbitButton;
    private readonly Button _atmosphereButton;
    private readonly Label _decayLabel;
    private readonly GridContainer _checks;
    private readonly Label _liftValue;
    private readonly Label _powerValue;
    private readonly ProgressBar _liftBar;
    private readonly RichTextLabel _holdLabel;
    private readonly RichTextLabel _fixLabel;
    private readonly RichTextLabel _hintLabel;

    private WFEnterAtmosphereConfirmWindow? _confirm;
    private WFUnsanctionedOrbitConfirmWindow? _unsanctionedConfirm;

    private EntityUid? _console;

    private readonly Dictionary<RichTextLabel, (string Text, Color? Colour)> _notes = new();
    private readonly HashSet<Control> _shown = new();
    private Color? _liftBarColour;

    /// <summary>Lift ratio at or above which the hull flies; matches CEZLevelsSystem.WFFullLiftRatio.</summary>
    private const float FullLift = 1f;

    /// <summary>Lift ratio under which partial lift stops helping; matches CEZLevelsSystem.WFPartialLiftRatio.</summary>
    private const float PartialLift = 0.5f;

    /// <summary>Vertical speed, in levels per second, under which an airborne hull counts as hovering.</summary>
    private const float HoverSpeed = 0.02f;

    /// <summary>Where the lift bar tops out, so a strong hull still shows how far past 100% it is.</summary>
    private const float LiftBarMax = 2f;

    private static readonly Color LiftGood = Color.FromHex("#3fe05a");
    private static readonly Color LiftMarginal = Color.FromHex("#ffd23f");
    private static readonly Color LiftBad = Color.FromHex("#ff3030");
    private static readonly Color Neutral = Color.FromHex("#00ff2a");

    /// <summary>The flight phase as shown, or null when the hull is not near a planet; the nav readout's status uses it.</summary>
    public string? PhaseText { get; private set; }

    public WFOrbitButton()
    {
        IoCManager.InjectDependencies(this);
        _ui = _entMan.System<SharedUserInterfaceSystem>();

        Orientation = LayoutOrientation.Vertical;
        // Never hide this container: hidden controls get no FrameUpdate, so only the children hide.

        _phaseLabel = new Label
        {
            Align = Label.AlignMode.Center,
            ClipText = true,
            Visible = false,
        };

        _readyLabel = new Label
        {
            Align = Label.AlignMode.Center,
            Visible = false,
        };

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

        _decayLabel = new Label
        {
            Align = Label.AlignMode.Center,
            Visible = false,
        };

        _liftValue = new Label { HorizontalExpand = true, Align = Label.AlignMode.Right };
        _powerValue = new Label { HorizontalExpand = true, Align = Label.AlignMode.Right };

        _checks = new GridContainer
        {
            Columns = 2,
            HorizontalExpand = true,
            Margin = new Thickness(4, 2, 4, 0),
            MouseFilter = MouseFilterMode.Pass,
            ToolTip = Loc.GetString("wf-shuttle-console-atmosphere-mode-tooltip"),
            Visible = false,
        };
        _checks.AddChild(new Label { Text = Loc.GetString("wf-flight-check-lift"), FontColorOverride = Neutral });
        _checks.AddChild(_liftValue);
        _checks.AddChild(new Label { Text = Loc.GetString("wf-flight-check-power"), FontColorOverride = Neutral });
        _checks.AddChild(_powerValue);

        _liftBar = new ProgressBar
        {
            MinValue = 0f,
            MaxValue = LiftBarMax,
            SetHeight = 4,
            Margin = new Thickness(4, 2, 4, 2),
            BackgroundStyleBoxOverride = new StyleBoxFlat(Color.FromHex("#1e1e1e")),
            Visible = false,
        };

        _holdLabel = NewNote();
        _fixLabel = NewNote();
        _hintLabel = NewNote();
        _hintLabel.StyleClasses.Add(StyleBase.StyleClassLabelSubText);

        AddChild(_phaseLabel);
        AddChild(_readyLabel);
        AddChild(_liftoffButton);
        AddChild(_orbitButton);
        AddChild(_atmosphereButton);
        AddChild(_decayLabel);
        AddChild(_checks);
        AddChild(_liftBar);
        AddChild(_holdLabel);
        AddChild(_fixLabel);
        AddChild(_hintLabel);
    }

    private static RichTextLabel NewNote()
    {
        return new RichTextLabel
        {
            HorizontalExpand = true,
            Margin = new Thickness(4, 0, 4, 0),
            Visible = false,
        };
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

        _shown.Clear();
        UpdateControls();
        ApplyVisibility();
    }

    /// <summary>Fills in this frame's controls and marks which to show.</summary>
    private void UpdateControls()
    {
        if (!_entMan.TryGetComponent<WFConsoleOrbitTargetComponent>(_console, out var target))
        {
            PhaseText = null;
            return;
        }

        UpdatePhase(target);

        var grounded = target.LiftoffAvailable || target.LiftoffActive;
        if (grounded)
        {
            UpdateGrounded(target);
            return;
        }

        if (target.Airborne)
        {
            UpdateChecklist(target);
            SetNote(_hintLabel, Loc.GetString("wf-flight-hint-air"));
            return;
        }

        Show(_orbitButton);
        _orbitButton.Disabled = target.Busy || target.Planet == null;

        var planet = target.PlanetName;
        if (string.IsNullOrEmpty(planet))
        {
            _orbitButton.Text = Loc.GetString("wf-shuttle-console-orbit-none");
            return;
        }

        _orbitButton.Text = Loc.GetString(target.InOrbit ? "wf-shuttle-console-leave-orbit" : "wf-shuttle-console-enter-orbit",
            ("planet", planet));

        if (!target.InOrbit)
            return;

        // -1 means the hull is holding its own orbit.
        var decaying = target.DecaySeconds >= 0f;
        Show(_decayLabel);
        _decayLabel.Text = decaying
            ? Loc.GetString("wf-shuttle-console-orbit-decaying", ("seconds", MathF.Ceiling(target.DecaySeconds).ToString("F0")))
            : Loc.GetString("wf-shuttle-console-orbit-stable");
        _decayLabel.FontColorOverride = decaying ? LiftBad : LiftGood;

        Show(_atmosphereButton);
        _atmosphereButton.Disabled = target.Busy;
        _atmosphereButton.Text = Loc.GetString("wf-shuttle-console-enter-atmosphere", ("planet", planet));
        _atmosphereButton.ToolTip = Loc.GetString("wf-shuttle-console-atmosphere-mode-tooltip");

        UpdateChecklist(target);

        if (CanFly(target))
            SetNote(_hintLabel, Loc.GetString("wf-flight-hint-orbit"));
    }

    /// <summary>Marks a control to be shown this frame.</summary>
    private void Show(Control control)
    {
        _shown.Add(control);
    }

    /// <summary>
    /// Shows this frame's controls and hides the rest, touching each once: a button hidden even briefly drops the
    /// click in progress.
    /// </summary>
    private void ApplyVisibility()
    {
        foreach (var child in Children)
        {
            child.Visible = _shown.Contains(child);
        }
    }

    /// <summary>Landed or lifting: the ready verdict, the liftoff button and the checklist with any hold.</summary>
    private void UpdateGrounded(WFConsoleOrbitTargetComponent target)
    {
        var ready = !target.Busy && target.Hold == null && CanFly(target);

        Show(_liftoffButton);
        _liftoffButton.Disabled = target.Busy || !target.LiftoffActive && !ready;
        _liftoffButton.Text = Loc.GetString(target.LiftoffActive
            ? "wf-shuttle-console-cancel-liftoff"
            : "wf-shuttle-console-liftoff");

        UpdateChecklist(target);

        if (target.LiftoffActive)
            return;

        Show(_readyLabel);
        _readyLabel.Text = Loc.GetString(ready ? "wf-flight-ready" : "wf-flight-not-ready");
        _readyLabel.FontColorOverride = ready ? LiftGood : LiftBad;

        if (target.Hold is { } hold)
            SetNote(_holdLabel, Loc.GetString("wf-flight-check-hold", ("reason", hold)), LiftBad);

        if (ready)
            SetNote(_hintLabel, Loc.GetString("wf-flight-hint-grounded"));
    }

    /// <summary>Lift and power rows, the lift bar and what to change when either falls short.</summary>
    private void UpdateChecklist(WFConsoleOrbitTargetComponent target)
    {
        Show(_checks);
        Show(_liftBar);

        var liftColour = target.LiftRatio >= FullLift
            ? LiftGood
            : target.LiftRatio >= PartialLift
                ? LiftMarginal
                : LiftBad;

        _liftValue.Text = Loc.GetString("wf-flight-check-lift-value", ("percent", LiftPercent(target.LiftRatio)));
        _liftValue.FontColorOverride = liftColour;
        _liftBar.Value = float.IsFinite(target.LiftRatio) ? Math.Clamp(target.LiftRatio, 0f, LiftBarMax) : LiftBarMax;
        if (_liftBarColour != liftColour)
        {
            _liftBarColour = liftColour;
            _liftBar.ForegroundStyleBoxOverride = new StyleBoxFlat(liftColour);
        }

        var shortKw = MathF.Max(1f, MathF.Ceiling(target.AtmospherePowerShortfall / 1000f)).ToString("N0");
        _powerValue.Text = target.AtmospherePowerDeficit
            ? Loc.GetString("wf-flight-check-power-short", ("short", shortKw))
            : Loc.GetString("wf-flight-check-power-ok", ("power", (target.AtmospherePowerDemand / 1000f).ToString("N0")));
        _powerValue.FontColorOverride = target.AtmospherePowerDeficit ? LiftBad : LiftGood;

        string? fix = null;
        if (target.LiftRatio < FullLift)
        {
            fix = (target.ConvertAdvice, target.AddAdvice) switch
            {
                (> 0, 0) => Loc.GetString("wf-flight-fix-convert", ("count", target.ConvertAdvice)),
                (0, > 0) => Loc.GetString("wf-flight-fix-add", ("count", target.AddAdvice)),
                (> 0, > 0) => Loc.GetString("wf-flight-fix-convert-add",
                    ("convert", target.ConvertAdvice),
                    ("add", target.AddAdvice)),
                _ => null,
            };
        }

        if (target.AtmospherePowerDeficit)
        {
            var power = Loc.GetString("wf-flight-fix-power", ("short", shortKw));
            fix = fix == null ? power : fix + "\n" + power;
        }

        if (fix != null)
            SetNote(_fixLabel, fix, LiftMarginal);
    }

    /// <summary>Works out the phase line and shows it at the top of the panel.</summary>
    private void UpdatePhase(WFConsoleOrbitTargetComponent target)
    {
        var grid = _entMan.GetComponentOrNull<TransformComponent>(_console)?.GridUid;
        string? phase;
        var colour = Neutral;

        if (target.InOrbit)
        {
            phase = Loc.GetString("wf-flight-phase-orbit", ("planet", target.PlanetName));
        }
        else if (target.LiftoffActive)
        {
            var countdown = _entMan.GetComponentOrNull<CEZPhysicsComponent>(grid)?.LaunchCountdown ?? 0f;
            phase = countdown > 0f
                ? Loc.GetString("wf-flight-phase-spooling", ("countdown", countdown.ToString("0.0")))
                : Loc.GetString("wf-flight-phase-lifting");
            colour = LiftMarginal;
        }
        else if (target.LiftoffAvailable)
        {
            phase = Loc.GetString("wf-flight-phase-landed", ("planet", target.PlanetName));
        }
        else if (_entMan.HasComponent<WFLiftLostComponent>(grid))
        {
            phase = Loc.GetString("wf-flight-phase-lift-lost");
            colour = LiftBad;
        }
        else if (target.Airborne)
        {
            var vertical = _entMan.GetComponentOrNull<CEZPhysicsComponent>(grid)?.Velocity ?? 0f;
            phase = Loc.GetString(vertical > HoverSpeed
                ? "wf-flight-phase-climbing"
                : vertical < -HoverSpeed
                    ? "wf-flight-phase-descending"
                    : "wf-flight-phase-hovering");
        }
        else
        {
            phase = null;
        }

        PhaseText = phase;

        if (phase == null)
            return;

        Show(_phaseLabel);
        _phaseLabel.Text = phase;
        _phaseLabel.FontColorOverride = colour;
    }

    private static bool CanFly(WFConsoleOrbitTargetComponent target)
    {
        return target.LiftRatio >= FullLift && !target.AtmospherePowerDeficit;
    }

    private static int LiftPercent(float ratio)
    {
        return float.IsFinite(ratio) ? (int) MathF.Floor(ratio * 100f) : 999;
    }

    /// <summary>Shows a note, rebuilding its message only when the text or colour changed.</summary>
    private void SetNote(RichTextLabel label, string text, Color? colour = null)
    {
        Show(label);
        if (_notes.TryGetValue(label, out var shown) && shown.Text == text && shown.Colour == colour)
            return;

        _notes[label] = (text, colour);
        var message = new FormattedMessage();
        if (colour is { } c)
            message.PushColor(c);
        message.AddText(text);
        if (colour != null)
            message.Pop();
        label.SetMessage(message);
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

        if (CanFly(target))
        {
            _ui.ClientSendUiMessage(console, ShuttleConsoleUiKey.Key, new WFEnterAtmosphereMessage(netConsole, false));
            return;
        }

        _confirm ??= new WFEnterAtmosphereConfirmWindow();
        _confirm.Ask(target.PlanetName, target.LiftRatio, target.AtmospherePowerDemand, target.AtmospherePowerDeficit,
            () => _ui.ClientSendUiMessage(console, ShuttleConsoleUiKey.Key, new WFEnterAtmosphereMessage(netConsole, true)));
    }
}
