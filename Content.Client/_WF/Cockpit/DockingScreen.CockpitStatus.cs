using System.Linq;
using Content.Client._WF.Cockpit;

namespace Content.Client.Shuttles.UI;

public sealed partial class DockingScreen
{
    /// <summary>Lights docking only when a port on this ship is actually connected.</summary>
    public WFCockpitStatusReading WfCockpitDockedStatus()
    {
        if (DockingControl.GridEntity is not { } grid || DockingControl.DockState == null)
            return WFCockpitStatusReading.Docked(null);
        var connected = Docks.TryGetValue(_entManager.GetNetEntity(grid), out var ports) && ports.Any(port => port.Connected);
        return WFCockpitStatusReading.Docked(connected);
    }
}
