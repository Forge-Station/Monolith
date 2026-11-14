using System.Numerics;
using Content.Client._WF.Cockpit;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;
using Robust.Shared.Map;

namespace Content.Client.UserInterface.Controls;

public partial class MapGridControl
{
    [Dependency] private IInputManager _wfCockpitInput = default!;

    private bool _wfCockpitPanDown;
    private bool _wfCockpitPanning;
    private Vector2? _wfCockpitMouse;

    /// <summary>Uses shared cockpit panning and compact map annotations.</summary>
    public bool WfCockpitControls { get; private set; }

    /// <summary>Enables right-mouse panning until the borrowed map returns to its console.</summary>
    public void WfCockpitInteraction(WFCockpitLease lease)
    {
        var enabled = WfCockpitControls;
        WfCockpitControls = true;
        _draggin = false;
        WfEndCockpitPan();
        lease.Remember(() =>
        {
            WfCockpitControls = enabled;
            _draggin = false;
            WfEndCockpitPan();
        });
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _wfCockpitInput.FirstChanceOnKeyEvent += WfCockpitPanKey;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        WfUpdateCockpitPan(_wfCockpitInput.MouseScreenPosition, DisplayManager.IsFocused);
    }

    protected override void VisibilityChanged(bool newVisible)
    {
        base.VisibilityChanged(newVisible);
        if (WfCockpitControls)
            WfEndCockpitPan();
    }

    protected override void ExitedTree()
    {
        _wfCockpitInput.FirstChanceOnKeyEvent -= WfCockpitPanKey;
        base.ExitedTree();
        if (WfCockpitControls)
            WfEndCockpitPan();
    }

    private void WfCockpitPanKey(KeyEventArgs args, KeyEventType type) =>
        WfCockpitPanKey(args, type, _wfCockpitInput.MouseScreenPosition);

    /// <summary>Captures only a fresh right-button press on this plot before context menus can open.</summary>
    private void WfCockpitPanKey(KeyEventArgs args, KeyEventType type, ScreenCoordinates mouse)
    {
        if (args.Key != Keyboard.Key.MouseRight)
            return;
        if (_wfCockpitPanDown)
        {
            args.Handle();
            if (type == KeyEventType.Up)
                WfEndCockpitPan();
            return;
        }
        if (args.Handled || type != KeyEventType.Down || args.IsRepeat || !WfCockpitControls ||
            !VisibleInTree || !DisplayManager.IsFocused || !mouse.IsValid || args.Shift || args.Control || args.Alt ||
            UserInterfaceManager.MouseGetControl(mouse) != this)
            return;
        args.Handle();
        _wfCockpitPanDown = _wfCockpitPanning = true;
        _wfCockpitMouse = mouse.Position;
    }

    private void WfUpdateCockpitPan(ScreenCoordinates mouse, bool focused)
    {
        if (!WfCockpitControls)
            return;
        if (!VisibleInTree || !focused || !_wfCockpitPanDown)
        {
            WfEndCockpitPan();
            return;
        }

        if (!_wfCockpitPanning)
            return;
        if (!mouse.IsValid || mouse.Window != Window?.Id || MinimapScale <= 0f)
        {
            _wfCockpitMouse = null;
            return;
        }

        if (_wfCockpitMouse is { } last)
        {
            var delta = mouse.Position - last;
            if (delta != Vector2.Zero)
            {
                Recentering = false;
                Offset -= new Vector2(delta.X, -delta.Y) / MinimapScale;
                WfCockpitPanMoved();
            }
        }
        _wfCockpitMouse = mouse.Position;
    }

    /// <summary>Updates map-specific tracking after a right-mouse pan.</summary>
    protected virtual void WfCockpitPanMoved()
    {
    }

    private void WfEndCockpitPan()
    {
        _wfCockpitPanDown = false;
        _wfCockpitPanning = false;
        _wfCockpitMouse = null;
    }
}
