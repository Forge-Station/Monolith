using Content.Client._WF.Cockpit;
using Content.Shared._WF.Shuttles;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.Shuttles.UI;

public sealed partial class ShuttleCameraBar
{
    private (ShuttleCameraView View, float Zoom, bool LowLight)? _wfCockpitCamera;

    /// <summary>
    /// Starts outside the ship while remembering the camera presented before entry. The cockpit session
    /// message must go first, or the server saves this request as the helm's own camera.
    /// </summary>
    public void WfCockpitDefaultView()
    {
        if (_wfCockpitCamera != null)
            return;
        if (_entManager.TryGetComponent<ShuttleCameraComponent>(_player.LocalEntity, out var camera))
            Show(camera.View, camera.Zoom, camera.LowLight);
        _wfCockpitCamera = (_view, _zoom, _lowLight);
        Request(ShuttleCameraView.External, _zoom, _lowLight);
    }

    /// <summary>Restores the camera controls while the server ends the cockpit's temporary camera session.</summary>
    public void WfCockpitRestoreView()
    {
        if (_wfCockpitCamera is not { } previous)
            return;
        _wfCockpitCamera = null;
        Show(previous.View, previous.Zoom, previous.LowLight);
    }

    /// <summary>Fits permanent camera selection and zoom above the cockpit flight controls.</summary>
    public Control WfCockpitControls(WFCockpitLease lease)
    {
        lease.Take(this);
        lease.Clear(this);
        var views = new GridContainer { Columns = 3, HorizontalExpand = true };
        var captions = new[] { "wf-cockpit-camera-helm", "wf-cockpit-camera-front", "wf-cockpit-camera-rear",
            "wf-cockpit-camera-left", "wf-cockpit-camera-right", "wf-cockpit-camera-external" };
        for (var i = 0; i < _views.Length; i++)
            views.AddChild(Compact(_views[i].Button, captions[i]));
        var night = Compact(LowLightButton, "wf-cockpit-camera-night");
        night.SetWidth = 36;
        night.HorizontalExpand = false;
        var value = lease.Take(ZoomLabel);
        value.SetWidth = 44;
        value.HorizontalExpand = false;
        var controls = Column(views, Row(night, lease.Take(ZoomSlider), value));
        controls.SeparationOverride = 4;
        AddChild(controls);
        return this;

        Button Compact(Button button, string caption)
        {
            lease.Take(button);
            var text = button.Text;
            var tooltip = button.ToolTip;
            var alignment = button.Label.Align;
            var compact = button.HasStyleClass("WfCompact");
            lease.Remember(() =>
            {
                button.Text = text;
                button.ToolTip = tooltip;
                button.Label.Align = alignment;
                if (!compact)
                    button.RemoveStyleClass("WfCompact");
                Switch(button);
            });
            button.Text = Loc.GetString(caption);
            button.ToolTip = tooltip ?? text;
            button.AddStyleClass("WfCompact");
            return button;
        }
    }
}
