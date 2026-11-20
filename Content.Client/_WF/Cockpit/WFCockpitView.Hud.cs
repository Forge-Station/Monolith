using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Alerts.Widgets;
using Content.Client.UserInterface.Systems.Chat;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Cockpit;

public sealed partial class WFCockpitView
{
    private LayoutContainer _speech = default!;
    private WFCockpitSpeechClip _speechClip = default!;
    private Control _crewAlerts = default!;
    private Control _votes = default!;

    /// <summary>Keeps critical live HUD widgets and speech lifetime updates outside the hidden character controls.</summary>
    private void InitializeHud(InGameScreen screen, Control hiddenHud)
    {
        var controls = WFCockpitLease.Descendants(hiddenHud).ToArray();
        var viewportContainer = controls.OfType<LayoutContainer>().First(control => control.Name == "ViewportContainer");
        var alerts = controls.OfType<AlertsUI>().Single();
        var votes = controls.OfType<BoxContainer>().First(control => control.Name == "VoteMenu");
        _speech = new LayoutContainer { Name = "CockpitSpeechBubbles", MouseFilter = MouseFilterMode.Ignore };
        _speech.AddStyleClass("WfNativeStyle");
        _speechClip = new WFCockpitSpeechClip { Name = "CockpitSpeechClip" };
        _speechClip.AddChild(_speech);
        var chat = UserInterfaceManager.GetUIController<ChatUIController>();
        chat.SetSpeechBubbleRoot(_speech);
        _lease.Remember(() => chat.SetSpeechBubbleRoot(viewportContainer));
        _votes = _lease.Take(votes, restoreVisibility: false);
        var liveAlerts = _lease.Take(alerts, restoreVisibility: false);
        Native(_votes);
        Native(liveAlerts);
        if (screen is DefaultGameScreen normal)
        {
            normal.WfSetCockpitAlertsWidth(128);
            _lease.Remember(() => normal.WfSetCockpitAlertsWidth(null));
        }
        else
        {
            var columns = alerts.AlertContainer.Columns;
            alerts.AlertContainer.Columns = 2;
            _lease.Remember(() => alerts.AlertContainer.Columns = columns);
        }
        _crewAlerts = new ScrollContainer
        {
            Name = "CockpitCrewAlerts",
            HScrollEnabled = false,
            VScrollEnabled = true,
            ReturnMeasure = true,
            MouseFilter = MouseFilterMode.Pass,
        };
        _crewAlerts.AddStyleClass("WfNativeStyle");
        _crewAlerts.AddChild(liveAlerts);
        AddChild(_speechClip);
        AddChild(_votes);
        AddChild(_crewAlerts);

        void Native(Control control)
        {
            if (control.HasStyleClass("WfNativeStyle"))
                return;
            control.AddStyleClass("WfNativeStyle");
            _lease.Remember(() => control.RemoveStyleClass("WfNativeStyle"));
        }
    }

    private void MeasureHud(WFCockpitPanelLayout.Frame frame, Vector2 full)
    {
        _speechClip.Measure(full);
        // A maximised MFD measured its docked alerts and votes before it could be sized.
        if (!frame.WorldShown)
            return;
        var world = frame.World.Size;
        _crewAlerts.Measure(new Vector2(AlertBankWidth, Math.Max(1, world.Y - 12)));
        _votes.Measure(new Vector2(Math.Max(140, Math.Min(340, world.X - 164)), world.Y));
    }

    private void ArrangeHud(WFCockpitPanelLayout.Frame frame)
    {
        if (!frame.WorldShown)
        {
            // Bubbles have no world view to speak over, so the clip keeps them off screen; chat still carries the speech.
            _speechClip.Arrange(UIBox2.FromDimensions(frame.Mfd.TopLeft, Vector2.Zero));
            var stripHeight = Math.Min(_crewAlerts.DesiredSize.Y, frame.Alerts.Height);
            _crewAlerts.Arrange(UIBox2.FromDimensions(frame.Alerts.TopLeft, new Vector2(frame.Alerts.Width, stripHeight)));
            _votes.Arrange(UIBox2.FromDimensions(frame.Votes.TopLeft,
                new Vector2(Math.Min(VoteWidth, frame.Votes.Width), frame.Votes.Height)));
            return;
        }
        var world = frame.World;
        // Speech bubbles use screen coordinates; the clip keeps them inside the world view without moving the origin.
        _speechClip.Arrange(world);
        var alertHeight = Math.Min(_crewAlerts.DesiredSize.Y, Math.Max(1, world.Height - 12));
        _crewAlerts.Arrange(UIBox2.FromDimensions(new Vector2(world.Right - 150, world.Top + 6), new Vector2(144, alertHeight)));
        var voteWidth = Math.Max(140, Math.Min(340, world.Width - 164));
        _votes.Arrange(UIBox2.FromDimensions(world.TopLeft + new Vector2(6),
            new Vector2(voteWidth, Math.Max(0, Math.Min(world.Height - 12, _votes.DesiredSize.Y)))));
    }
}
