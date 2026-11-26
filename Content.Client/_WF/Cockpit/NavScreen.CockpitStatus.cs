using Content.Client._WF.Cockpit;

namespace Content.Client.Shuttles.UI;

public sealed partial class NavScreen
{
    /// <summary>Reads the existing navigation mode without depending on button visibility.</summary>
    public WFCockpitStatusReading WfCockpitDampeningStatus(bool parking) =>
        WFCockpitStatusReading.Dampening(_shuttleEntity == null ? null : NavRadar.DampeningMode, parking);
}
