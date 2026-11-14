using Content.Shared.Shuttles.Components;

namespace Content.Client.Shuttles.UI;

public sealed partial class NavScreen
{
    /// <summary>Gives the helm's radar the waypoint panel, with flying to them through the helm's autopilot.</summary>
    public void WfEnableWaypoints() => NavRadar.WfEnableWaypoints(ShuttleConsoleUiKey.Key, canFly: true);
}
