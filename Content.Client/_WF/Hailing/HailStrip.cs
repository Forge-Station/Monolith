using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Shared._WF.Hailing;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.Hailing;

/// <summary>
/// The hail strip on the caption row of the cockpit's communications housing: one line of status and the
/// keys for whatever the console's hail needs next. The row had nothing beside its caption, and the strip
/// asks for no height of its own, so the chat underneath keeps every pixel it had.
/// </summary>
public sealed class HailStrip : BoxContainer
{
    [Dependency] private IEntityManager _entManager = default!;

    /// <summary>Wide enough for the longest caption, since console keys clip their text.</summary>
    private const float KeyWidth = 78f;

    private const float PollInterval = 0.2f;

    /// <summary>Seconds the reason a hail stopped stays up.</summary>
    private const float NoticeTime = 6f;

    private const float PulseRate = 1.5f;

    private static readonly Color RingColor = Color.FromHex("#ffb030");
    private static readonly Color CallColor = Color.FromHex("#8fd0ff");

    /// <summary>The pilot answered or declined the incoming hail.</summary>
    public event Action<bool>? AnswerRequested;

    /// <summary>The pilot cancelled their hail or closed the channel.</summary>
    public event Action? EndRequested;

    /// <summary>The hail window should come up: to pick a ship to hail, or to see the open channel.</summary>
    public event Action? ViewRequested;

    private readonly Label _status;
    private readonly Button _primary;
    private readonly Button _secondary;

    private EntityUid? _console;
    private bool _shown;
    private HailState? _state;
    private string _ship = string.Empty;
    private string? _notice;
    private float _noticeTime;
    private float _pollAccumulator;
    private float _pulse;

    public HailStrip()
    {
        IoCManager.InjectDependencies(this);

        Name = "HailStrip";
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 4;
        HorizontalExpand = true;

        AddChild(_status = new Label
        {
            Name = "HailStripStatus",
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            Align = Label.AlignMode.Right,
            ClipText = true,
            MouseFilter = MouseFilterMode.Pass,
        });

        AddChild(_primary = Key("HailStripPrimary"));
        AddChild(_secondary = Key("HailStripSecondary"));

        _primary.OnPressed += _ =>
        {
            if (_state == HailState.Ringing)
                AnswerRequested?.Invoke(true);
            else
                ViewRequested?.Invoke();
        };

        _secondary.OnPressed += _ =>
        {
            if (_state == HailState.Ringing)
                AnswerRequested?.Invoke(false);
            else
                EndRequested?.Invoke();
        };

        Refresh();
    }

    /// <summary>
    /// Points the strip at a console, or nothing.
    /// </summary>
    public void SetConsole(EntityUid? console)
    {
        if (_console == console)
            return;

        _console = console;
        Refresh();
    }

    /// <summary>
    /// Says why a hail or call stopped, for a few seconds.
    /// </summary>
    public void ShowEnded(HailEndReason reason, string ship)
    {
        _notice = HailWindow.EndText(reason, ship);
        _noticeTime = NoticeTime;
        _shown = false;
        Refresh();
    }

    // The caption beside the strip sets the row's height. The keys are a little taller and centre over it,
    // which costs the chat nothing.
    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        return new Vector2(base.MeasureOverride(availableSize).X, 0f);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        _pollAccumulator += args.DeltaSeconds;

        if (_noticeTime > 0f)
        {
            _noticeTime -= args.DeltaSeconds;

            if (_noticeTime <= 0f)
                _shown = false;
        }

        if (_pollAccumulator >= PollInterval || !_shown)
            Refresh();

        if (_state != HailState.Ringing)
            return;

        _pulse += args.DeltaSeconds * PulseRate;
        _status.Modulate = Color.White.WithAlpha(0.45f + MathF.Abs(MathF.Sin(_pulse * MathF.PI)) * 0.55f);
    }

    private void Refresh()
    {
        _pollAccumulator = 0f;

        HailConsoleComponent? hail = null;

        if (_console != null)
            _entManager.TryGetComponent(_console, out hail);

        var state = hail?.State;
        var ship = hail?.PeerShip ?? string.Empty;

        if (_shown && state == _state && ship == _ship)
            return;

        // A new hail outranks the word about the last one.
        if (state != null && _state == null)
            _noticeTime = 0f;

        _shown = true;
        _state = state;
        _ship = ship;
        _pulse = 0f;
        _status.Modulate = Color.White;

        switch (state)
        {
            case HailState.Ringing:
                Status(Loc.GetString("hail-strip-incoming", ("ship", ship)), RingColor);
                Keys("hail-strip-answer", "hail-strip-decline");
                break;

            case HailState.Calling:
                Status(Loc.GetString("hail-strip-calling", ("ship", ship)), CallColor);
                Keys(null, "hail-strip-cancel");
                break;

            case HailState.Connected:
                Status(Loc.GetString("hail-strip-open", ("ship", ship)), WFInstrumentTheme.Green);
                Keys("hail-strip-view", "hail-strip-end");
                break;

            default:
                var notice = _noticeTime > 0f && _notice != null;
                Status(notice ? _notice! : string.Empty, WFInstrumentTheme.Muted);
                Keys("hail-strip-hail", null);
                break;
        }
    }

    private void Status(string text, Color color)
    {
        _status.Text = text;
        _status.FontColorOverride = color;

        // One clipped line is all there is room for, so the whole of it is a hover away.
        _status.ToolTip = text.Length == 0 ? null : text;
    }

    private void Keys(string? primary, string? secondary)
    {
        _primary.Visible = primary != null;
        _secondary.Visible = secondary != null;

        if (primary != null)
            _primary.Text = Loc.GetString(primary);

        if (secondary != null)
            _secondary.Text = Loc.GetString(secondary);
    }

    private static Button Key(string name)
    {
        var key = new Button
        {
            Name = name,
            SetWidth = KeyWidth,
            VerticalAlignment = VAlignment.Center,
        };

        // A header key is the slim kind made for caption rows.
        key.AddStyleClass("WfHeader");
        WFInstrumentTheme.Switch(key);
        return key;
    }
}
