using Content.Client._WF.Cockpit;
using Content.Client._WF.Hailing;
using Content.Shared._WF.Hailing;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    /// <summary>The hail window wants the ships in range.</summary>
    public event Action? HailTargetsRequested;

    /// <summary>The pilot hailed a ship.</summary>
    public event Action<NetEntity>? HailRequested;

    /// <summary>The pilot answered or declined an incoming hail.</summary>
    public event Action<bool>? HailAnswerRequested;

    /// <summary>The pilot cancelled their hail or closed the channel.</summary>
    public event Action? HailEndRequested;

    /// <summary>The pilot sent a line down the channel.</summary>
    public event Action<string>? HailSayRequested;

    private HailWindow? _hailWindow;
    private HailStrip _hailStrip = default!;
    private EntityUid? _hailConsole;

    private void WfHailInitialize()
    {
        HailButton.OnPressed += _ => WfHailShow();
        HailButton.ChannelOpened += WfHailShow;
        _hailStrip = WfHailCreateStrip();
    }

    /// <summary>
    /// Hands the console its hail window. The interface owns it, so it closes with the console.
    /// </summary>
    public void WfHailAttach(HailWindow window)
    {
        _hailWindow = window;
        window.TargetsRequested += () => HailTargetsRequested?.Invoke();
        window.HailRequested += ship => HailRequested?.Invoke(ship);
        window.AnswerRequested += accept => HailAnswerRequested?.Invoke(accept);
        window.EndRequested += () => HailEndRequested?.Invoke();
        window.SayRequested += text => HailSayRequested?.Invoke(text);
    }

    /// <summary>
    /// Puts the hail strip on the caption row of the cockpit's communications housing, and takes it back
    /// out when the cockpit's lease ends.
    /// </summary>
    public void WfHailJoinComms(Control housing, WFCockpitLease lease)
    {
        // The housing is a caption over its content, and nothing else uses the caption's row.
        if (housing.ChildCount == 0 || housing.GetChild(0) is not BoxContainer body || body.ChildCount == 0)
            return;

        // A cockpit torn down without its lease takes the strip with it.
        if (_hailStrip.Disposed)
            _hailStrip = WfHailCreateStrip();

        var strip = _hailStrip;
        var caption = body.GetChild(0);

        var row = new BoxContainer
        {
            Name = "CockpitCommsCaption",
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            HorizontalExpand = true,
        };

        caption.Orphan();
        strip.Orphan();
        row.AddChild(caption);
        row.AddChild(strip);
        body.AddChild(row);
        row.SetPositionInParent(0);
        lease.Remember(strip.Orphan);
    }

    private HailStrip WfHailCreateStrip()
    {
        var strip = new HailStrip();
        strip.AnswerRequested += accept => HailAnswerRequested?.Invoke(accept);
        strip.EndRequested += () => HailEndRequested?.Invoke();
        strip.ViewRequested += WfHailShow;
        strip.SetConsole(_hailConsole);
        return strip;
    }

    private void WfHailShow()
    {
        if (_hailWindow == null || _hailWindow.Disposed)
            return;

        if (_hailWindow.IsOpen)
            _hailWindow.MoveToFront();
        else
            _hailWindow.OpenCenteredRight();
    }

    private void WfHailUpdateState(EntityUid console)
    {
        _hailConsole = console;
        HailButton.SetConsole(console);
        _hailStrip.SetConsole(console);
        _hailWindow?.SetConsole(console);
    }

    /// <summary>Feeds the hail window the ships in range.</summary>
    public void WfHailTargets(List<HailTarget> targets)
    {
        _hailWindow?.UpdateTargets(targets);
    }

    /// <summary>Shows why a hail or call stopped.</summary>
    public void WfHailEnded(HailEndReason reason, string ship)
    {
        HailButton.ShowEnded(reason);
        _hailStrip.ShowEnded(reason, ship);
        _hailWindow?.ShowEnded(reason, ship);
    }
}
