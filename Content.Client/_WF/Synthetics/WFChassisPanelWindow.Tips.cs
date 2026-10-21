using System.Linq;
using System.Text;
using Content.Shared._WF.Synthetics;
using Content.Shared._WF.Wolfmed.Hud;
using Content.Shared._WF.Wolfmed.Wounds;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Synthetics;

/// <summary>Hover tips for the chassis panel: what each state, load, fault, band and control means and how to clear it.</summary>
public sealed partial class WFChassisPanelWindow
{
    /// <summary>Characters per tooltip line; the engine's tooltip does not wrap on its own.</summary>
    private const int TipWidth = 56;

    private const string BandPrefix = "wf-chassis-band-";

    private IPrototypeManager _prototypes = default!;
    private Dictionary<string, WolfmedSyntheticHudLinePrototype>? _linesByText;

    /// <summary>Lets the label take the mouse so its tooltip can show.</summary>
    private static void Hoverable(Control control) => control.MouseFilter = Control.MouseFilterMode.Pass;

    private void InitTips()
    {
        _prototypes = IoCManager.Resolve<IPrototypeManager>();

        OpenBayButton.ToolTip = Wrap(Loc.GetString("wf-chassis-ui-tip-bay-open"));
        RebootButton.ToolTip = Wrap(Loc.GetString("wf-chassis-ui-tip-reboot"));
        OverclockButton.ToolTip = Wrap(Loc.GetString("wf-chassis-ui-tip-overclock-off"));
    }

    private void UpdateControlTips(WFChassisPanelState state)
    {
        OpenBayButton.ToolTip = Wrap(Loc.GetString(state.BayOpen ? "wf-chassis-ui-tip-bay-close" : "wf-chassis-ui-tip-bay-open"));
        OverclockButton.ToolTip = Wrap(Loc.GetString(state.Overclocked ? "wf-chassis-ui-tip-overclock-on" : "wf-chassis-ui-tip-overclock-off"));
    }

    private void ApplyPartTips(PartRow row, WFChassisPartEntry entry, string stateTipKey)
    {
        row.State.ToolTip = Wrap(Loc.GetString(stateTipKey));
        row.Load.ToolTip = Wrap(Loc.GetString("wf-chassis-ui-tip-load"));
        row.Faults.ToolTip = entry.Faults.Count == 0
            ? Wrap(Loc.GetString("wf-chassis-ui-tip-faults-none"))
            : string.Join("\n", entry.Faults.Select(FaultTip));

        row.Isolate.ToolTip = Wrap(Loc.GetString(entry.Isolated ? "wf-chassis-ui-tip-restore" : "wf-chassis-ui-tip-isolate"));
        row.Detach.ToolTip = Wrap(Loc.GetString("wf-chassis-ui-tip-detach"));
    }

    private static void ApplyOrganTips(OrganRow row, WFChassisOrganEntry entry)
    {
        var bandTip = entry.Band.StartsWith(BandPrefix, StringComparison.Ordinal)
            ? "wf-chassis-ui-tip-band-" + entry.Band[BandPrefix.Length..]
            : string.Empty;
        row.Band.ToolTip = entry.Organ != null && Loc.TryGetString(bandTip, out var band)
            ? Wrap(band)
            : null;

        row.Vital.ToolTip = entry.Vital ? Wrap(Loc.GetString("wf-chassis-ui-tip-vital")) : null;
        row.Disconnect.ToolTip = Wrap(Loc.GetString("wf-chassis-ui-tip-disconnect"));
    }

    /// <summary>
    /// One fault's tip: the readout line with the HUD's advice for it and, for a wound fault, the analyzer's
    /// summary of the treatment. Falls back to the line alone when no readout prototype is found.
    /// </summary>
    public string FaultTip(string fault)
    {
        var line = Loc.GetString(fault);
        if (!LinesByText.TryGetValue(fault, out var proto))
            return Wrap(line);

        var head = !string.IsNullOrEmpty(proto.Advice) && Loc.TryGetString(proto.Advice, out var advice)
            ? Loc.GetString("wf-chassis-ui-tip-fault", ("fault", line), ("advice", advice))
            : line;

        var tip = Wrap(head);
        if (proto.Wound is { } wound &&
            _prototypes.TryIndex<WolfmedTreatmentProcedurePrototype>(wound.Id, out var procedure) &&
            Loc.TryGetString(procedure.Summary, out var summary))
        {
            tip += "\n" + Wrap(summary);
        }

        return tip;
    }

    /// <summary>Every <c>syntheticHudLine</c> by the locale key of its readout line, indexed on first use.</summary>
    private Dictionary<string, WolfmedSyntheticHudLinePrototype> LinesByText
    {
        get
        {
            if (_linesByText != null)
                return _linesByText;

            _linesByText = new Dictionary<string, WolfmedSyntheticHudLinePrototype>();
            foreach (var proto in _prototypes.EnumeratePrototypes<WolfmedSyntheticHudLinePrototype>())
                _linesByText.TryAdd(proto.Line, proto);
            return _linesByText;
        }
    }

    /// <summary>Breaks text at spaces into lines of about <see cref="TipWidth"/> characters.</summary>
    public static string Wrap(string text)
    {
        var result = new StringBuilder();
        var lineLength = 0;
        foreach (var word in text.Split(' '))
        {
            if (lineLength > 0 && lineLength + 1 + word.Length > TipWidth)
            {
                result.Append('\n');
                lineLength = 0;
            }
            else if (lineLength > 0)
            {
                result.Append(' ');
                lineLength++;
            }

            result.Append(word);
            lineLength += word.Length;
        }

        return result.ToString();
    }
}
