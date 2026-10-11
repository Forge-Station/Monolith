using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Shared._WF.ShipShields;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._WF.ShipShields;

/// <summary>Shows the installed generator's operating limits without crowding allocation controls.</summary>
public sealed class WFShipShieldStatsWindow : DefaultWindow
{
    private readonly RichTextLabel _name = new();
    private readonly RichTextLabel _capacity = new();
    private readonly RichTextLabel _limit = new();
    private readonly RichTextLabel _repair = new();
    private readonly RichTextLabel _recharge = new();
    private readonly RichTextLabel _idle = new();
    private readonly RichTextLabel _maximum = new();
    private readonly RichTextLabel _lockout = new();

    /// <summary>Creates a compact scrolling generator specification panel, with instrument chrome only for instrument consoles.</summary>
    public WFShipShieldStatsWindow(bool instruments = true)
    {
        Title = Loc.GetString("wf-shield-stats-title");
        MinSize = new Vector2(340f, 300f);
        SetSize = new Vector2(440f, 420f);
        var scroll = new ScrollContainer { HScrollEnabled = false, ReserveScrollbarSpace = true };
        var values = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 12,
            Margin = new Thickness(8),
            HorizontalExpand = true,
        };
        foreach (var label in new[] { _name, _capacity, _limit, _repair, _recharge, _idle, _maximum, _lockout })
        {
            label.HorizontalExpand = true;
            values.AddChild(instruments ? (Control) new WFGlassReadout(label) : label);
        }
        values.AddChild(new RichTextLabel { Text = Loc.GetString("wf-shield-stats-recovery-note") });
        scroll.AddChild(values);
        Contents.AddChild(scroll);
        if (instruments)
            WFInstrumentTheme.Install(this);
    }

    /// <summary>Displays authoritative runtime values for the selected installed emitter.</summary>
    public void UpdateStats(WFShipShieldGeneratorStats stats)
    {
        _name.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("wf-shield-stats-generator", ("name", stats.Name))));
        _capacity.Text = Loc.GetString("wf-shield-stats-capacity", ("value", MathF.Round(stats.Capacity, 0)));
        _limit.Text = Loc.GetString("wf-shield-stats-overload-limit", ("value", MathF.Round(stats.DamageLimit, 0)));
        _repair.Text = Loc.GetString("wf-shield-stats-online-repair", ("value", MathF.Round(stats.HealPerSecond, 1)));
        _recharge.Text = Loc.GetString("wf-shield-stats-offline-repair", ("value", MathF.Round(stats.RechargePerSecond, 1)));
        _idle.Text = Loc.GetString("wf-shield-stats-idle-power", ("value", MathF.Round(stats.BasePowerWatts / 1000f, 1)));
        _maximum.Text = Loc.GetString("wf-shield-stats-max-power", ("value", MathF.Round(stats.MaximumPowerWatts / 1000f, 1)));
        _lockout.Text = Loc.GetString("wf-shield-stats-overload-lockout", ("value", MathF.Round(stats.OverloadSeconds, 1)));
    }
}
