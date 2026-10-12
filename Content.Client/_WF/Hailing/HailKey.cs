using Content.Shared._WF.Hailing;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Hailing;

/// <summary>
/// The HAIL key on the shuttle console's tab row. It opens the hail window and doubles as the hail lamp: its
/// caption and tint follow the console's hail, and it flashes while one is coming in. It takes no room of
/// its own, so a hail never moves the console's layout. The console can sit hidden behind the cockpit, where
/// the engine stops ticking it, so <see cref="HailKeySystem"/> ticks the key instead.
/// </summary>
public sealed class HailKey : Button
{
    [Dependency] private IEntityManager _entManager = default!;

    /// <summary>The hail is a component on the console rather than console state, so it is polled.</summary>
    private const float PollInterval = 0.2f;

    /// <summary>Seconds the key says why a hail stopped.</summary>
    private const float NoticeTime = 5f;

    private const float PulseRate = 1.5f;

    private static readonly Color RingColor = Color.FromHex("#ffb030");
    private static readonly Color CallColor = Color.FromHex("#8fd0ff");
    private static readonly Color OpenColor = Color.FromHex("#7dff8f");
    private static readonly Color NoticeColor = Color.FromHex("#b0b0b0");

    /// <summary>A hail was answered, at either end.</summary>
    public event Action? ChannelOpened;

    private EntityUid? _console;
    private HailState? _state;
    private string? _notice;
    private float _noticeTime;
    private float _pollAccumulator;
    private float _pulse;

    public HailKey()
    {
        IoCManager.InjectDependencies(this);
    }

    /// <summary>
    /// Points the key at a console, or nothing.
    /// </summary>
    public void SetConsole(EntityUid? console)
    {
        if (_console == console)
            return;

        _console = console;
        Poll();
    }

    /// <summary>
    /// Says in a word why a hail or call stopped, for a few seconds.
    /// </summary>
    public void ShowEnded(HailEndReason reason)
    {
        _notice = Loc.GetString(reason switch
        {
            HailEndReason.Closed or HailEndReason.PeerClosed => "hail-key-closed",
            HailEndReason.Declined => "hail-key-declined",
            HailEndReason.NoAnswer => "hail-key-no-answer",
            HailEndReason.Busy => "hail-key-busy",
            HailEndReason.Lost => "hail-key-lost",
            _ => "hail-key-no-link",
        });

        _noticeTime = NoticeTime;
        Show(_state, true);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();

        if (_entManager.TrySystem(out HailKeySystem? system))
            system.Track(this);
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        if (_entManager.TrySystem(out HailKeySystem? system))
            system.Untrack(this);
    }

    /// <summary>
    /// One frame of polling and flashing, run whether or not the console is on screen.
    /// </summary>
    public void Tick(float frameTime)
    {
        _pollAccumulator += frameTime;

        if (_noticeTime > 0f)
        {
            _noticeTime -= frameTime;

            if (_noticeTime <= 0f)
                Show(_state, true);
        }

        if (_pollAccumulator >= PollInterval)
            Poll();

        if (_state != HailState.Ringing)
            return;

        _pulse += frameTime * PulseRate;
        Modulate = Color.InterpolateBetween(Color.White, RingColor, MathF.Abs(MathF.Sin(_pulse * MathF.PI)));
    }

    private void Poll()
    {
        _pollAccumulator = 0f;

        HailConsoleComponent? hail = null;

        if (_console != null)
            _entManager.TryGetComponent(_console, out hail);

        Show(hail?.State, false);
    }

    private void Show(HailState? state, bool force)
    {
        if (state == _state && !force)
            return;

        var opened = state == HailState.Connected && _state != HailState.Connected;

        // A new hail outranks the word about the last one. The word itself can arrive while the old hail
        // is still on the console, and has to outlast it.
        if (state != null && _state == null)
            _noticeTime = 0f;

        _state = state;
        _pulse = 0f;

        switch (state)
        {
            case HailState.Ringing:
                Text = Loc.GetString("hail-key-incoming");
                Modulate = RingColor;
                break;

            case HailState.Calling:
                Text = Loc.GetString("hail-key-calling");
                Modulate = CallColor;
                break;

            case HailState.Connected:
                Text = Loc.GetString("hail-key-open");
                Modulate = OpenColor;
                break;

            default:
                var notice = _noticeTime > 0f && _notice != null;
                Text = notice ? _notice : Loc.GetString("hail-console-button");
                Modulate = notice ? NoticeColor : Color.White;
                break;
        }

        // An answered hail goes straight on screen. Opening a window changes the tree, so it waits for the
        // next frame rather than run inside whatever caused this poll.
        if (opened)
            UserInterfaceManager.DeferAction(() => ChannelOpened?.Invoke());
    }
}
