namespace Content.Client.UserInterface.Screens;

public sealed partial class DefaultGameScreen
{
    private float? _wfCockpitAlertsWidth;

    /// <summary>Fits live alerts into the cockpit bank until the normal HUD returns.</summary>
    public void WfSetCockpitAlertsWidth(float? width)
    {
        if (_wfCockpitAlertsWidth == width)
            return;
        _wfCockpitAlertsWidth = width;
        ResizeAlertsContainer();
    }
}
