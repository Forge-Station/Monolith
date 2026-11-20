using System.Linq;
using Content.Client._WF.CombatConsole;
using Content.Shared._Mono.FireControl;
using Content.Shared._WF.CombatConsole;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._Mono.FireControl.UI;

public sealed partial class FireControlWindow
{
    private WFGaugeReading WfSupplyReading(NetEntity uid)
    {
        if (_currentState is not { Connected: true } state)
            return WFGaugeReading.Number(null, 0, 1, "wf-gauge-unit-rounds");
        if (!state.Combat.WeaponSupplies.TryGetValue(uid, out var supply))
        {
            var entry = state.FireControllables?.FirstOrDefault(weapon => weapon.NetEntity == uid);
            var count = entry?.AmmoCount;
            return WFGaugeReading.Number(count, 0, WFGaugeScale.Ceiling(count ?? 0, 30),
                "wf-gauge-unit-rounds", tint: count == 0 ? Red : Accent);
        }
        if (supply.Kind == WFWeaponSupplyKind.Infinite)
            return new WFGaugeReading(1, 0, 1, Loc.GetString("wf-gauge-unlimited"), Accent);
        if (supply.Kind == WFWeaponSupplyKind.Energy)
        {
            double? charge = supply.Count is { } count && supply.Capacity is > 0 ? count * 100d / supply.Capacity : null;
            return WFGaugeReading.Number(charge, 0, 100, "wf-gauge-unit-percent", tint: charge == 0 ? Red : Accent);
        }
        var reading = WFGaugeReading.Number(supply.Count, 0, Math.Max(1, (double?) supply.Capacity ?? WFGaugeScale.Ceiling(supply.Count ?? 0, 30)),
            "wf-gauge-unit-rounds", tint: supply.Count == 0 && supply.Kind != WFWeaponSupplyKind.Recharging ? Red : Accent);
        if (supply is { Kind: WFWeaponSupplyKind.Recharging, Count: 0 })
            return reading with { Text = Loc.GetString("wf-gauge-recharging") };
        return supply is { Count: { } rounds, Capacity: > 0 }
            ? reading with { Text = Loc.GetString("wf-weapon-supply-count", ("count", rounds),
                ("capacity", supply.Capacity.Value), ("unit", Loc.GetString("wf-gauge-unit-rounds"))) }
            : reading;
    }

    /// <summary>Whether this weapon is offensive; flare launchers are not.</summary>
    internal bool WfAvailableWeapon(NetEntity uid) => _wfCombat?.FlareLaunchers.Contains(uid) != true;

    /// <summary>Styles the existing selection button once, preserving every upstream handler.</summary>
    private void WfStyleWeapon(Button button, NetEntity uid)
    {
        if (button.Children.OfType<WFWeaponRow>().Any())
            return;
        button.AddStyleClass("WfNativeStyle");
        button.AddStyleClass("WfWeapon");
        button.Margin = new Thickness(0);
        button.MinWidth = 0;
        button.MinHeight = 0;
        button.RectClipContent = true;
        button.ClipText = true;
        button.Label.Visible = false;
        button.Stylesheet = null;
        button.StyleBoxOverride = new WFWeaponRowStyleBox(button);
        button.MuteSounds = true;
        button.OnPressed -= WFConsoleAudio.Press;
        button.OnPressed += WFConsoleAudio.Press;
        button.AddChild(new WFWeaponRow(button, () => WfSupplyReading(uid)));
    }

    private bool WfUpdateWeaponSupplyText(Button button, FireControllableEntry controllable)
    {
        var reading = WfSupplyReading(controllable.NetEntity);
        if (button.Text != controllable.Name)
            button.Text = controllable.Name;
        var tip = Loc.GetString("wf-console-weapon-supply", ("name", controllable.Name), ("supply", reading.Text));
        if (button.ToolTip != tip)
            button.ToolTip = tip;
        button.ModulateSelfOverride = null;
        button.Children.OfType<WFWeaponRow>().FirstOrDefault()?.Refresh(reading);
        return true;
    }
}
