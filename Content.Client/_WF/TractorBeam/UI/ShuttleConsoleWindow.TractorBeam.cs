namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    /// <summary>Shows which ships' tractor beams hold this one.</summary>
    private void WfUpdateTractorCapture(string[] sources)
    {
        CaptureBanner.SetSources(sources);
    }
}
