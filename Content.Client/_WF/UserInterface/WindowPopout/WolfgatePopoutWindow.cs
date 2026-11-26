using System.ComponentModel;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Timing;

namespace Content.Client._WF.UserInterface.WindowPopout;

/// <summary>
/// OS window hosting an in-game window. The in-game window is moved in whole, so <see cref="BaseWindow.IsOpen"/>,
/// <see cref="BaseWindow.Close"/> and the code that owns the window keep working while it is popped out.
/// </summary>
public sealed class WolfgatePopoutWindow : OSWindow
{
    private static readonly Vector2 AutoSize = new(float.NaN, float.NaN);

    private static readonly AttachedProperty[] MarginProperties =
    {
        LayoutContainer.MarginLeftProperty,
        LayoutContainer.MarginTopProperty,
        LayoutContainer.MarginRightProperty,
        LayoutContainer.MarginBottomProperty,
    };

    private readonly BaseWindow _window;
    private readonly float[] _dockedMargins = new float[MarginProperties.Length];
    private Vector2 _dockedSetSize;
    private bool _dockedResizable;
    private bool _docking;

    public WolfgatePopoutWindow(BaseWindow window)
    {
        _window = window;
        Title = WindowTitle(window) ?? Loc.GetString("wf-window-popout-default-title");
        SetSize = window.Size;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>
    /// Opens the OS window and moves the in-game window into it.
    /// </summary>
    public void PopOut()
    {
        _dockedSetSize = _window.SetSize;
        _dockedResizable = _window.Resizable;
        for (var i = 0; i < MarginProperties.Length; i++)
        {
            _dockedMargins[i] = _window.GetValue<float>(MarginProperties[i]);
        }

        Show();

        _window.Orphan();
        // The OS window does the moving and resizing now, and the window fills it.
        _window.Resizable = false;
        _window.SetSize = AutoSize;
        AddChild(_window);
    }

    /// <summary>
    /// Puts the window back in the game window where it was, then closes the OS window.
    /// </summary>
    public void Dock()
    {
        ReturnToGame();
        Close();
    }

    private void ReturnToGame()
    {
        if (_window.Parent != this)
            return;

        _docking = true;
        var size = _window.Size;
        _window.Orphan();

        var root = UserInterfaceManager.WindowRoot;
        root.AddChild(_window);

        // Keep a resize made while popped out, unless the window sizes itself.
        if (!float.IsNaN(_dockedSetSize.X) && !float.IsNaN(_dockedSetSize.Y))
            _window.SetSize = root.Size.X > 0 && root.Size.Y > 0 ? Vector2.Min(size, root.Size) : size;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // OSWindow pins SetSize to the window size in UI units, which goes stale when the DPI scale changes
        // (dragging onto a monitor with other scaling). Stretching to the root follows the real window instead.
        if (!float.IsNaN(SetWidth) || !float.IsNaN(SetHeight))
            SetSize = AutoSize;
    }

    protected override void ChildRemoved(Control child)
    {
        base.ChildRemoved(child);

        if (child != _window)
            return;

        // Undo the popped-out layout so a later Open() in the game window looks as before. The margins are restored
        // as they were rather than through LayoutContainer.SetPosition, which shifts them by the control's current
        // position and would walk an anchored window off-screen a little further on every pop out.
        _window.Resizable = _dockedResizable;
        _window.SetSize = _dockedSetSize;
        for (var i = 0; i < MarginProperties.Length; i++)
        {
            _window.SetValue(MarginProperties[i], _dockedMargins[i]);
        }

        // The window was closed or disposed by its owner, so the OS window goes too.
        if (!_docking)
            UserInterfaceManager.DeferAction(Close);
    }

    private void OnClosing(CancelEventArgs args)
    {
        if (_window.Parent != this)
            return;

        // The OS close button closes the UI, like the in-game close button.
        _window.Close();

        // A window that refuses to close goes back into the game instead of vanishing with the OS window.
        ReturnToGame();
    }

    private void OnClosed()
    {
        // Reached without Closing when the OS destroys the window directly.
        if (_window.Parent == this)
            _window.Close();
    }

    private static string? WindowTitle(BaseWindow window)
    {
        return window switch
        {
            FancyWindow fancy => fancy.Title,
            DefaultWindow plain => plain.Title,
            _ => null,
        };
    }
}
