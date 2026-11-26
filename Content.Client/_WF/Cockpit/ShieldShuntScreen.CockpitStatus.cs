using Content.Client._WF.Cockpit;

namespace Content.Client._WF.ShipShields;

public sealed partial class WFShipShieldShuntScreen
{
    /// <summary>Reports shield deployment from the current server snapshot.</summary>
    public WFCockpitStatusReading WfCockpitShieldStatus() => WFCockpitStatusReading.Shield(_state);
}
