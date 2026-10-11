using System.Numerics;
using Content.Client._WF.CombatConsole;
using Robust.Client.UserInterface;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    /// <summary>The size the helm opens at, tall enough for every page to fit without scrolling.</summary>
    public static readonly Vector2 WfDefaultSize = new(1240, 960);

    private void WfInitializeInstruments()
    {
        MinSize = new Vector2(960, 600);
        FitWindow(this, WfDefaultSize);
        WfInstallHeader();
        NavModeButton.Text = Loc.GetString("wf-console-nav");
        MapModeButton.Text = Loc.GetString("wf-console-map");
        DockModeButton.Text = Loc.GetString("wf-console-dock");
        NavContainer.WfRefitInstruments(WfCreateCockpitButton());
        ShipContainer.WfRefitInstruments();
        _shieldScreen.WfRefitInstruments();
        MapContainer.WfRefitInstruments();
        DockContainer.WfRefitInstruments();

        foreach (var control in new Control[] { CollisionBanner, CaptureBanner, CameraBar, ModeButtons, Contents })
        {
            Detach(control);
            control.SetWidth = float.NaN;
            control.HorizontalAlignment = HAlignment.Stretch;
            control.HorizontalExpand = true;
            control.Margin = new Thickness(0);
        }
        CameraBar.Visible = NavContainer.Visible;
        NavContainer.OnVisibilityChanged += control => CameraBar.Visible = WfCockpitActive || control.Visible;
        Contents.RectClipContent = true;
        ModeButtons.MinHeight = 36;
        ModeButtons.SeparationOverride = 5;
        var body = Column(CollisionBanner, CaptureBanner, ModeButtons, CameraBar, Contents);
        body.Margin = new Thickness(10);
        body.HorizontalExpand = body.VerticalExpand = true;
        ContentsContainer.DisposeAllChildren();
        ContentsContainer.AddChild(body);
        Install(this);
    }

    /// <summary>Puts the flight deck caption in the title bar.</summary>
    private void WfInstallHeader()
    {
        Title = Loc.GetString("wf-console-helm-title");
        WindowTitle.FontColorOverride = Accent;
    }
}
