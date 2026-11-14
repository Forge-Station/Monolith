using System.Linq;
using Content.Client._WF.AsteroidMining;
using Content.Shared._WF.AsteroidMining;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._Mono.FireControl.UI;

public sealed partial class FireControlWindow
{
    /// <summary>How long a power step shows before the server's value, in case the server never applies it.</summary>
    private static readonly TimeSpan WfMiningPendingTimeout = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _wfTiming = default!;

    private Control _wfMiningPanel = default!;
    private Label _wfMiningPower = default!;
    private Label _wfMiningStatus = default!;
    private Label _wfMiningIdle = default!;
    private Control _wfMiningScan = default!;
    private Label _wfMiningRock = default!;
    private BoxContainer _wfMiningOres = default!;
    private readonly List<Label> _wfMiningOreLabels = new();
    private Label _wfMiningInert = default!;
    private Label _wfMiningResistance = default!;
    private Label _wfMiningHeat = default!;
    private Label _wfMiningYield = default!;
    private WFMiningChargeMeter _wfMiningMeter = default!;
    private ProgressBar _wfMiningProgress = default!;
    private Label _wfMiningOvercharge = default!;
    private WFMiningBeamGunneryState _wfMining;
    private float? _wfMiningPending;
    private TimeSpan _wfMiningPendingUntil;

    /// <summary>
    /// Builds the gunner's mining laser panel: the ship's laser power and a scan of the rock under fire. It stays hidden
    /// until the ship reports a mining laser.
    /// </summary>
    private Control WfCreateMiningPanel()
    {
        _wfMiningPower = Label("wf-mining-gunnery-power", Cream);
        _wfMiningPower.ToolTip = Loc.GetString("wf-mining-gunnery-power-help");
        _wfMiningPower.MouseFilter = MouseFilterMode.Pass;
        var steps = WFMiningGauges.PowerStepButtons(WfStepMiningPower);

        _wfMiningStatus = Label("wf-mining-gunnery-idle", Muted);
        _wfMiningStatus.ClipText = true;
        _wfMiningIdle = Label("wf-mining-gunnery-idle", Muted);
        _wfMiningIdle.ClipText = true;
        _wfMiningIdle.ToolTip = Loc.GetString("wf-mining-gunnery-idle-help");
        _wfMiningIdle.MouseFilter = MouseFilterMode.Pass;

        _wfMiningRock = Label("wf-mining-gunnery-idle", Cream);
        _wfMiningRock.ClipText = true;
        _wfMiningOres = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _wfMiningInert = Label("wf-mining-gunnery-idle", Muted);
        _wfMiningResistance = Label("wf-mining-gunnery-idle", Muted);
        _wfMiningHeat = Label("wf-mining-gunnery-idle", Cream);
        _wfMiningHeat.ClipText = true;
        _wfMiningYield = Label("wf-mining-gunnery-idle", Green);
        _wfMiningYield.ClipText = true;
        _wfMiningYield.ToolTip = Loc.GetString("wf-mining-gunnery-yield-help");
        _wfMiningYield.MouseFilter = MouseFilterMode.Pass;
        _wfMiningMeter = new WFMiningChargeMeter { ToolTip = Loc.GetString("wf-mining-gunnery-meter-help") };
        _wfMiningProgress = new ProgressBar { MinValue = 0, MaxValue = 1, MinHeight = 12, HorizontalExpand = true };
        _wfMiningOvercharge = Label("wf-mining-gunnery-overcharge", Red);
        _wfMiningOvercharge.ClipText = true;
        _wfMiningOvercharge.ToolTip = Loc.GetString("wf-mining-gunnery-overcharge-help");
        _wfMiningOvercharge.MouseFilter = MouseFilterMode.Pass;
        _wfMiningOvercharge.Visible = false;
        var scan = Column(_wfMiningRock, _wfMiningOres, _wfMiningInert, _wfMiningResistance, _wfMiningHeat, _wfMiningMeter,
            Label("wf-mining-gunnery-progress", Muted), _wfMiningProgress, _wfMiningYield, _wfMiningOvercharge);
        scan.SeparationOverride = 2;
        scan.Visible = false;
        _wfMiningScan = scan;

        var body = Column(_wfMiningPower, steps, _wfMiningStatus, _wfMiningIdle, scan);
        body.SeparationOverride = 4;
        var panel = Panel("wf-mining-gunnery-title", body);
        panel.Name = "WfGunneryMining";
        panel.SetWidth = WfShieldPanelWidth;
        panel.HorizontalExpand = false;
        panel.Visible = false;
        return _wfMiningPanel = panel;
    }

    private void WfStepMiningPower(float step)
    {
        if (_wfMining.Lasers == 0)
            return;

        var power = MathF.Round(Math.Clamp((_wfMiningPending ?? _wfMining.Power) + step, 0f, 1f), 2);
        _wfMiningPending = power;
        _wfMiningPendingUntil = _wfTiming.RealTime + WfMiningPendingTimeout;
        CombatMessage?.Invoke(new WFMiningBeamPowerMessage(power));
        WfRefreshMining();
    }

    /// <summary>Refreshes the mining laser panel from the server's scan.</summary>
    public void WfUpdateMining(WFMiningBeamGunneryState state)
    {
        _wfMining = state;
        if (_wfMiningPending is { } pending &&
            (MathF.Abs(pending - state.Power) < 0.005f || _wfTiming.RealTime > _wfMiningPendingUntil))
            _wfMiningPending = null;
        WfRefreshMining();
    }

    private void WfRefreshMining()
    {
        var state = _wfMining;
        _wfMiningPanel.Visible = state.Lasers > 0;
        if (state.Lasers == 0)
            return;

        var power = _wfMiningPending ?? state.Power;
        _wfMiningPower.Text = Loc.GetString("wf-mining-gunnery-power", ("power", (int) MathF.Round(power * 100)));
        _wfMiningStatus.Text = state.Cooldown > 0f
            ? Loc.GetString("wf-mining-gunnery-cooldown", ("seconds", (int) MathF.Ceiling(state.Cooldown)))
            : Loc.GetString("wf-mining-gunnery-lasers", ("count", state.Lasers));
        _wfMiningStatus.FontColorOverride = state.Cooldown > 0f ? Red : Muted;

        _wfMiningIdle.Visible = state.Target == null;
        _wfMiningScan.Visible = state.Target != null;
        if (state.Target is not { } rock)
            return;

        _wfMiningRock.Text = Loc.GetString("wf-mining-gunnery-rock", ("name", rock.Name), ("mass", (int) MathF.Round(rock.Mass)));
        WFMiningGauges.ShowOres(_wfMiningOres, _wfMiningOreLabels, rock.Ores);
        _wfMiningInert.Text = Loc.GetString("wf-mining-gunnery-inert", ("percent", (int) MathF.Round((1f - rock.Ores.Sum(o => o.Fraction)) * 100)));
        _wfMiningResistance.Text = Loc.GetString("wf-mining-gunnery-resistance", ("percent", (int) MathF.Round(rock.Resistance * 100)));

        _wfMiningHeat.Text = Loc.GetString("wf-mining-gunnery-heat", ("charge", (int) MathF.Round(rock.Charge * 100)),
            ("low", (int) MathF.Round(rock.WindowLow * 100)), ("high", (int) MathF.Round(rock.WindowHigh * 100)));
        _wfMiningMeter.Charge = rock.Charge;
        _wfMiningMeter.WindowLow = rock.WindowLow;
        _wfMiningMeter.WindowHigh = rock.WindowHigh;
        _wfMiningMeter.Overcharging = rock.Overcharge > 0f;
        _wfMiningProgress.Value = rock.Progress;
        _wfMiningYield.Text = Loc.GetString("wf-mining-gunnery-yield", ("percent", (int) MathF.Round(rock.Yield * 100)));
        _wfMiningYield.FontColorOverride = rock.Yield >= 0.995f ? Green : Accent;
        _wfMiningOvercharge.Visible = rock.Overcharge > 0f;
    }
}
