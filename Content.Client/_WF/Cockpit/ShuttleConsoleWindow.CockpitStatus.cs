using Content.Client._WF.Cockpit;
using Robust.Client.UserInterface;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    private bool? _wfCockpitAutopilotActive;

    /// <summary>Updates only the authoritative autopilot lamp without disrupting the current MFD.</summary>
    public void WfUpdateCockpitAutopilot(bool? active) => _wfCockpitAutopilotActive = active;

    /// <summary>Builds the permanent status bank from the console's existing live telemetry.</summary>
    public Control WfCockpitStatus() => new WFCockpitStatusLights(
        ("wf-cockpit-status-autopilot", () => WFCockpitStatusReading.Autopilot(_wfCockpitAutopilotActive)),
        ("wf-cockpit-status-ftl", MapContainer.WfCockpitFtlStatus),
        ("wf-cockpit-status-damp", () => NavContainer.WfCockpitDampeningStatus(false)),
        ("wf-cockpit-status-park", () => NavContainer.WfCockpitDampeningStatus(true)),
        ("wf-cockpit-status-dock", DockContainer.WfCockpitDockedStatus),
        ("wf-cockpit-status-shield", _shieldScreen.WfCockpitShieldStatus));
}
