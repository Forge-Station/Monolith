using Content.Client._WF.CombatConsole;
using Content.Client._WF.ShipShields;
using Content.Shared._WF.ShipShields;
using Robust.Client.UserInterface;

namespace Content.Client._Mono.FireControl.UI;

public sealed partial class FireControlWindow
{
    /// <summary>Width of the shield column on the right of the gunnery console.</summary>
    public const float WfShieldPanelWidth = 196;

    private WFShipShieldShuntScreen _wfShieldScreen = default!;
    private Control _wfShieldPanel = default!;

    /// <summary>
    /// Builds the gunner's simplified shield control. It stays hidden until the ship reports a shield generator.
    /// </summary>
    private Control WfCreateShieldPanel()
    {
        _wfShieldScreen = new WFShipShieldShuntScreen();
        _wfShieldScreen.AllocationRequested += (direction, concentration, arc) =>
            CombatMessage?.Invoke(new WFShipShieldSetShuntMessage(direction, concentration, arc));
        _wfShieldScreen.OnSetEnabled += enabled => CombatMessage?.Invoke(new WFShipShieldSetEnabledMessage(enabled));

        var panel = WFInstrumentTheme.Panel("wf-shield-helm-tab", _wfShieldScreen.WfGunneryShield(), true);
        panel.Name = "WfGunneryShields";
        panel.SetWidth = WfShieldPanelWidth;
        panel.HorizontalExpand = false;
        panel.Visible = false;
        return _wfShieldPanel = panel;
    }

    /// <summary>Refreshes the gunner's shield control from the ship's authoritative shield state.</summary>
    public void WfUpdateShields(WFShipShieldShuntState state)
    {
        _wfShieldPanel.Visible = state.Available;
        _wfShieldScreen.UpdateState(state, 0f);
    }
}
