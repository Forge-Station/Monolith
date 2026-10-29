using Content.Client._WF.CombatConsole;
using Content.Shared._WF.CargoLoad;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.CargoLoad;

/// <summary>
/// Helm strip showing the tonnes aboard against the load where thrust bottoms out, coloured by how much thrust is lost.
/// The tooltip gives the thrust cut, FTL spool and stored materials. Hidden while the ship carries nothing.
/// </summary>
public sealed class WFCargoGauge : BoxContainer
{
    [Dependency] private IEntityManager _entities = default!;

    /// <summary>Thrust factors where the strip turns from green to amber, and from amber to red.</summary>
    private const float Fine = 0.8f;
    private const float Heavy = 0.6f;

    private readonly Func<EntityUid?> _ship;
    private readonly WFGlassGauge _gauge;
    private (float, float, float, float)? _shown;

    /// <summary>Tracks the cargo of the ship a screen is showing; control names use the fuel bank's prefix.</summary>
    public WFCargoGauge(Func<EntityUid?> ship, string prefix = "Cockpit")
    {
        IoCManager.InjectDependencies(this);
        _ship = ship;
        Name = prefix + "Cargo";
        HorizontalExpand = true;
        SetHeight = 44;
        MouseFilter = MouseFilterMode.Pass;
        _gauge = new WFGlassGauge("wf-cargo-gauge", Reading, true)
        {
            Name = prefix + "CargoStrip",
            CompactStrip = true,
            SetHeight = 44,
            MouseFilter = MouseFilterMode.Pass,
        };
        AddChild(_gauge);
        Refresh();
    }

    /// <summary>The ship's cargo, or null while it carries nothing.</summary>
    public WFCargoReadoutComponent? Readout =>
        _ship() is { } ship && _entities.TryGetComponent(ship, out WFCargoReadoutComponent? readout) && readout.Load >= WFCargoReadoutComponent.ShownLoad
            ? readout
            : null;

    private WFGaugeReading Reading()
    {
        if (Readout is not { } cargo)
            return WFGaugeReading.Number(0, 0, 1, "wf-cargo-gauge-unit");

        var tint = cargo.Thrust >= Fine ? WFInstrumentTheme.Green
            : cargo.Thrust >= Heavy ? WFInstrumentTheme.Skin.Caution
            : WFInstrumentTheme.Red;
        return WFGaugeReading.Number(cargo.Load, 0, Math.Max(cargo.FullScale, cargo.Load), "wf-cargo-gauge-unit", tint: tint);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _entities.System<WFCargoGaugeSystem>().Track(this);
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        _entities.System<WFCargoGaugeSystem>().Untrack(this);
    }

    /// <summary>Shows or hides the strip for the ship's current cargo and updates its tooltip.</summary>
    public void Refresh()
    {
        // Hidden rather than collapsed, so the strip's row or column leaves no gap for it.
        var cargo = Readout;
        Visible = cargo != null;
        if (cargo == null)
        {
            _shown = null;
            return;
        }

        var shown = (MathF.Round(cargo.Load), MathF.Round(cargo.Stored, 1), MathF.Round(cargo.Thrust, 2), MathF.Round(cargo.Spool));
        if (_shown == shown)
            return;

        _shown = shown;
        var tooltip = Loc.GetString("wf-cargo-gauge-help", ("tonnes", (int) shown.Item1),
            ("percent", (int) MathF.Round((1f - cargo.Thrust) * 100f)), ("spool", (int) shown.Item4));
        if (cargo.Stored >= 0.05f)
            tooltip += "\n" + Loc.GetString("wf-cargo-gauge-stored", ("tonnes", shown.Item2));

        ToolTip = tooltip + "\n" + Loc.GetString("wf-cargo-gauge-dump");
        _gauge.ToolTip = ToolTip;
    }
}
