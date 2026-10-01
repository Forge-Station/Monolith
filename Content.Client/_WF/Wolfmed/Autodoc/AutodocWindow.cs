using System.Linq;
using System.Numerics;
using Content.Client._WF.Wolfmed.Medical;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using static Content.Client._WF.Wolfmed.Autodoc.AutodocStyle;

namespace Content.Client._WF.Wolfmed.Autodoc;

/// <summary>
/// S.A.M.'s terminal. A header with the machine's readout and its last words, the patient on the left (the
/// analyzer's own doll and wound panel), and on the right the procedures by category, the queue with its
/// requirements, the reservoir, the hardware, and the controls.
/// </summary>
public sealed class AutodocWindow : DefaultWindow
{
    /// <summary>The bottom row. Exposed so the layout test can measure the controls that used to be squeezed out.</summary>
    public BoxContainer ControlsRow => _controls;

    private readonly IPrototypeManager _protos = IoCManager.Resolve<IPrototypeManager>();
    private readonly IGameTiming _timing = IoCManager.Resolve<IGameTiming>();

    private readonly WolfmedBodyDoll _doll = new();
    private readonly WolfmedDiagnosticPanel _diagnostics = new();

    private readonly Label _readout;
    private readonly Label _seal; // Playtest 3: SEALED / UNSEALED beside the status
    private readonly Label _stepLine;
    private readonly Label _speech;
    private readonly PanelContainer _cursor;
    private readonly Label _progressText;
    private readonly ProgressBar _progress;
    private readonly Label _patientTitle;
    private readonly Label _filterLabel;
    private readonly BoxContainer _procedures;
    private readonly BoxContainer _queueBox;
    private readonly BoxContainer _reservoirBox;
    private readonly Label _diskLabel;
    private readonly Label _moduleLabel;
    private readonly Label _autofixLabel;
    private readonly Label _trayLabel;
    private readonly Label _modeLabel;
    private readonly Button _planButton;
    private readonly Button _autoButton;
    private readonly Button _cutButton;
    private readonly Button _startButton;
    private readonly Button _pauseButton;
    private readonly Button _abortButton;
    private readonly Button _ejectButton;
    private readonly CheckBox _anaesthesia;
    private readonly Control _queueSection;
    private readonly BoxContainer _controls;
    private readonly Label _queueHeadingCount;

    private readonly HashSet<string> _collapsed = new();
    private readonly Dictionary<string, string> _surgeryCategory = new();
    private readonly Dictionary<string, string> _surgeryProgram = new();
    private readonly List<AutodocCategoryPrototype> _categories;
    private readonly string _fallbackCategory;

    private TargetBodyPart? _filter;
    private AutodocBuiState? _state;
    private float _blink;
    private string _speechText = string.Empty;

    public event Action<EntProtoId, TargetBodyPart>? OnQueueAdd;
    public event Action<int>? OnQueueRemove;
    public event Action<int, bool>? OnQueueMove;
    public event Action<AutodocControl>? OnControl;
    public event Action<bool>? OnAnaesthesia;

    public AutodocWindow()
    {
        Title = Loc.GetString("wolfmed-autodoc-window-title");
        MinSize = new Vector2(920, 520);
        SetSize = new Vector2(1040, 680);

        _categories = _protos.EnumeratePrototypes<AutodocCategoryPrototype>().OrderBy(c => c.Order).ToList();
        _fallbackCategory = _categories.FirstOrDefault(c => c.Fallback)?.ID ?? _categories.Last().ID;
        foreach (var category in _categories)
            foreach (var surgery in category.Surgeries)
                _surgeryCategory[surgery.Id] = category.ID;
        foreach (var program in _protos.EnumeratePrototypes<AutodocProgramPrototype>())
            foreach (var surgery in program.Surgeries)
                _surgeryProgram.TryAdd(surgery.Id, program.Name);

        var screen = new PanelContainer { PanelOverride = Box(Screen, margin: 8f), HorizontalExpand = true, VerticalExpand = true };
        Contents.AddChild(screen);
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        screen.AddChild(column);

        // Header: the machine's readout and what it last said.
        var header = new PanelContainer { PanelOverride = Box(Panel, Border, 8f), HorizontalExpand = true };
        var headerRows = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        header.AddChild(headerRows);
        var titleRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        titleRow.AddChild(Text(Loc.GetString("wolfmed-autodoc-ui-name"), 20, Amber, true));
        titleRow.AddChild(Text("  " + Loc.GetString("wolfmed-autodoc-ui-subtitle"), 11, AmberDim) );
        _readout = Text(string.Empty, 13, Amber, true);
        _readout.HorizontalExpand = true;
        _readout.Align = Label.AlignMode.Right;
        titleRow.AddChild(_readout);
        _seal = Text(string.Empty, 13, Good, true);
        _seal.Margin = new Thickness(12, 0, 0, 0);
        titleRow.AddChild(_seal);
        headerRows.AddChild(titleRow);

        var progressRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, SeparationOverride = 8 };
        _stepLine = Text(string.Empty, 11, AmberDim);
        _stepLine.HorizontalExpand = true;
        progressRow.AddChild(_stepLine);
        _progressText = Text(string.Empty, 11, Amber);
        progressRow.AddChild(_progressText);
        headerRows.AddChild(progressRow);

        _progress = new ProgressBar
        {
            MinValue = 0f,
            MaxValue = 1f,
            MinHeight = 6,
            HorizontalExpand = true,
            BackgroundStyleBoxOverride = Box(AmberFaint, margin: 0f),
            ForegroundStyleBoxOverride = Box(Amber, margin: 0f),
        };
        headerRows.AddChild(_progress);

        var speechRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(0, 4, 0, 0) };
        speechRow.AddChild(Text("> ", 12, Good, true));
        _speech = Text(string.Empty, 12, Good);
        speechRow.AddChild(_speech);
        _cursor = new PanelContainer
        {
            MinSize = new Vector2(8, 14),
            VerticalAlignment = VAlignment.Center,
            PanelOverride = Box(Good, margin: 0f),
        };
        speechRow.AddChild(_cursor);
        headerRows.AddChild(speechRow);
        column.AddChild(header);

        // Body: patient on the left, the console on the right.
        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6, VerticalExpand = true };
        column.AddChild(body);

        var patient = new PanelContainer { PanelOverride = Box(Panel, Border, 6f), MinWidth = 360, VerticalExpand = true };
        var patientColumn = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        patient.AddChild(patientColumn);
        _patientTitle = Text(string.Empty, 11, AmberDim, true);
        patientColumn.AddChild(Heading(Loc.GetString("wolfmed-autodoc-ui-patient")));
        patientColumn.AddChild(_patientTitle);
        var dollRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, SeparationOverride = 8 };
        dollRow.AddChild(_doll);
        var dollNotes = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalAlignment = VAlignment.Center };
        dollNotes.AddChild(Text(Loc.GetString("wolfmed-autodoc-ui-doll-hint"), 10, AmberDim));
        _filterLabel = Text(string.Empty, 11, Cyan, true);
        dollNotes.AddChild(_filterLabel);
        var clear = FlatButton(Loc.GetString("wolfmed-autodoc-ui-filter-clear"), AmberDim);
        clear.OnPressed += _ => SetFilter(null);
        dollNotes.AddChild(clear);
        dollRow.AddChild(dollNotes);
        patientColumn.AddChild(dollRow);
        patientColumn.AddChild(new TerminalScroll { VerticalExpand = true, HorizontalExpand = true, MinHeight = 80, Children = { _diagnostics } });
        body.AddChild(patient);

        var console = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, SeparationOverride = 6 };
        body.AddChild(console);

        // Procedures, grouped.
        var procedurePanel = new PanelContainer { PanelOverride = Box(Panel, Border, 6f), VerticalExpand = true, HorizontalExpand = true };
        var procedureColumn = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        procedurePanel.AddChild(procedureColumn);
        procedureColumn.AddChild(Heading(Loc.GetString("wolfmed-autodoc-ui-procedures"), Loc.GetString("wolfmed-autodoc-ui-procedures-hint")));
        _procedures = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        procedureColumn.AddChild(new TerminalScroll { VerticalExpand = true, HorizontalExpand = true, MinHeight = 80, Children = { _procedures } });
        console.AddChild(procedurePanel);

        // Queue.
        var queuePanel = new PanelContainer { PanelOverride = Box(Panel, Border, 6f), HorizontalExpand = true };
        var queueColumn = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        queuePanel.AddChild(queueColumn);
        var queueHeading = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        _queueHeadingCount = Text(string.Empty, 11, AmberDim);
        queueColumn.AddChild(Heading(Loc.GetString("wolfmed-autodoc-ui-queue")));
        queueColumn.AddChild(queueHeading);
        queueHeading.AddChild(_queueHeadingCount);
        _queueBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        queueColumn.AddChild(new TerminalScroll { HorizontalExpand = true, MinHeight = 80, Children = { _queueBox } });
        _queueSection = queuePanel;
        console.AddChild(queuePanel);

        // Reservoir and hardware, side by side.
        var lower = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, SeparationOverride = 6 };
        console.AddChild(lower);

        var reservoirPanel = new PanelContainer { PanelOverride = Box(Panel, Border, 6f), HorizontalExpand = true };
        var reservoirColumn = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        reservoirPanel.AddChild(reservoirColumn);
        reservoirColumn.AddChild(Heading(Loc.GetString("wolfmed-autodoc-ui-reservoir")));
        _reservoirBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        reservoirColumn.AddChild(_reservoirBox);
        lower.AddChild(reservoirPanel);

        var hardwarePanel = new PanelContainer { PanelOverride = Box(Panel, Border, 6f), HorizontalExpand = true };
        var hardwareColumn = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        hardwarePanel.AddChild(hardwareColumn);
        hardwareColumn.AddChild(Heading(Loc.GetString("wolfmed-autodoc-ui-hardware")));
        _diskLabel = Text(string.Empty, 11);
        _moduleLabel = Text(string.Empty, 11);
        _autofixLabel = Text(string.Empty, 11);
        _trayLabel = Text(string.Empty, 11);
        _trayLabel.ToolTip = Loc.GetString("wolfmed-autodoc-ui-tray-hint"); // Playtest 5: "delivery tray" meant nothing
        _modeLabel = Text(string.Empty, 11, AmberDim);
        hardwareColumn.AddChild(_diskLabel);
        hardwareColumn.AddChild(_moduleLabel);
        hardwareColumn.AddChild(_autofixLabel);
        hardwareColumn.AddChild(_trayLabel);
        hardwareColumn.AddChild(_modeLabel);
        lower.AddChild(hardwarePanel);

        // Controls. Anchored on the window's own column rather than inside the console, so the row is one of
        // the fixed children the layout serves before it hands what is left to the body: a BoxContainer that
        // runs out of room clamps its last children to nothing, which is how this row used to vanish.
        var controls = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, SeparationOverride = 6 };
        _controls = controls;
        _anaesthesia = new CheckBox { Text = Loc.GetString("wolfmed-autodoc-ui-anaesthesia"), Pressed = true, HorizontalExpand = true };
        _anaesthesia.Label.FontOverride = Mono(11);
        _anaesthesia.Label.FontColorOverride = Amber;
        _anaesthesia.OnToggled += args => OnAnaesthesia?.Invoke(args.Pressed);
        controls.AddChild(_anaesthesia);

        // PLAN writes the triage queue; in self-service it is FIX ME and starts as well.
        _planButton = FlatButton(Loc.GetString("wolfmed-autodoc-ui-plan"), Amber, true);
        _planButton.ToolTip = Loc.GetString("wolfmed-autodoc-ui-plan-hint");
        _planButton.OnPressed += _ => OnControl?.Invoke(AutodocControl.Plan);
        controls.AddChild(_planButton);

        _startButton = FlatButton(Loc.GetString("wolfmed-autodoc-ui-start"), Good, true);
        _startButton.OnPressed += _ => OnControl?.Invoke(AutodocControl.Start);
        _pauseButton = FlatButton(Loc.GetString("wolfmed-autodoc-ui-pause"));
        _pauseButton.OnPressed += _ => OnControl?.Invoke(AutodocControl.Pause);
        _abortButton = FlatButton(Loc.GetString("wolfmed-autodoc-ui-abort"), Alert);
        _abortButton.OnPressed += _ => OnControl?.Invoke(AutodocControl.Abort);
        _ejectButton = FlatButton(Loc.GetString("wolfmed-autodoc-ui-eject"), Alert);
        _ejectButton.OnPressed += _ => OnControl?.Invoke(AutodocControl.Eject);
        controls.AddChild(_startButton);
        controls.AddChild(_pauseButton);
        controls.AddChild(_abortButton);
        controls.AddChild(_ejectButton);

        _cutButton = FlatButton(Loc.GetString("wolfmed-autodoc-ui-cut"), Alert);
        _cutButton.ToolTip = Loc.GetString("wolfmed-autodoc-ui-cut-hint");
        _cutButton.OnPressed += _ => OnControl?.Invoke(AutodocControl.CutClothing);
        controls.AddChild(_cutButton);

        _autoButton = FlatButton(Loc.GetString("wolfmed-autodoc-ui-auto-off"), Cyan);
        _autoButton.OnPressed += _ => OnControl?.Invoke(AutodocControl.Auto);
        controls.AddChild(_autoButton);
        column.AddChild(controls);

        _doll.OnPartSelected += part => SetFilter(_filter == part ? null : part);
        _diagnostics.OnPartSelected += part => SetFilter(_filter == part ? null : part);
    }

    /// <summary>Last percent drawn into the progress label, or -1 for nothing drawn yet.</summary>
    private int _progressPercent = -1;

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _blink += args.DeltaSeconds;
        if (_blink >= 0.5f)
        {
            _blink = 0f;
            _cursor.Visible = !_cursor.Visible;
        }

        if (_state is { CurrentStep: not null, StepLength: > 0f } state && state.State == AutodocState.Step)
        {
            var remaining = (float) (state.StepEnds - _timing.CurTime).TotalSeconds;
            ShowProgress(Math.Clamp(1f - remaining / state.StepLength, 0f, 1f));
        }
    }

    private void ShowProgress(float fraction)
    {
        _progress.Value = fraction;

        // FrameUpdate calls this every frame of a step; the gauge and the label only change once a percent.
        var percent = (int) (fraction * 100);
        if (percent == _progressPercent)
            return;

        _progressPercent = percent;
        _progressText.Text = $"{Gauge(fraction, 14)} {percent}%";
    }

    private void SetFilter(TargetBodyPart? part)
    {
        _filter = part;
        _doll.SetSelected(_filter);
        _diagnostics.SetTargetedPart(_filter, false);
        _filterLabel.Text = _filter is { } chosen
            ? Loc.GetString("wolfmed-autodoc-ui-filter", ("part", PartName(chosen)))
            : Loc.GetString("wolfmed-autodoc-ui-filter-none");
        if (_state != null)
            DrawProcedures(_state);
    }

    /// <summary>Redraws the window from the server's state.</summary>
    public void Update(AutodocBuiState state)
    {
        _state = state;

        if (state.Diagnostics != null)
        {
            _doll.SetBody(state.Diagnostics.Body);
            _diagnostics.Visible = true;
            _diagnostics.Populate(state.Diagnostics);
            _patientTitle.Text = Loc.GetString("wolfmed-autodoc-ui-patient-present");
        }
        else
        {
            _doll.SetBody(null);
            _diagnostics.Clear();
            _diagnostics.Visible = false;
            _patientTitle.Text = Loc.GetString("wolfmed-autodoc-ui-patient-none");
        }

        _readout.Text = state.Status;
        _readout.FontColorOverride = state.State switch
        {
            AutodocState.Waiting or AutodocState.Paused => Cyan,
            AutodocState.Faulted => Alert,
            AutodocState.Complete => Good,
            _ => Amber,
        };

        _seal.Text = Loc.GetString(WolfmedAutodocSealText.Readout(state.Seal));
        _seal.FontColorOverride = state.Seal switch
        {
            WolfmedAutodocSeal.Sealed => Good,
            WolfmedAutodocSeal.Open => AmberDim,
            _ => Alert,
        };

        _stepLine.Text = state.CurrentStep is { } step
            ? Loc.GetString("wolfmed-autodoc-ui-step", ("step", StepName(step)))
            : Loc.GetString($"wolfmed-autodoc-ui-idle-{(state.Occupied ? "occupied" : "empty")}");
        if (state.CurrentStep != null)
            ShowProgress(state.Progress);
        else
        {
            _progress.Value = 0f;
            _progressText.Text = string.Empty;
        }

        SetSpeech(state.LastLine ?? string.Empty);
        _filterLabel.Text = _filter is { } chosen
            ? Loc.GetString("wolfmed-autodoc-ui-filter", ("part", PartName(chosen)))
            : Loc.GetString("wolfmed-autodoc-ui-filter-none");

        _diskLabel.Text = Loc.GetString("wolfmed-autodoc-ui-disk", ("disk", state.DiskProgram ?? Loc.GetString("wolfmed-autodoc-ui-none")));
        _moduleLabel.Text = Loc.GetString(state.DefibModule ? "wolfmed-autodoc-ui-module-installed" : "wolfmed-autodoc-ui-module-missing");
        _moduleLabel.FontColorOverride = state.DefibModule ? Good : AmberDim;
        _autofixLabel.Text = Loc.GetString(!state.AutofixModule ? "wolfmed-autodoc-ui-autofix-missing"
            : state.Auto ? "wolfmed-autodoc-ui-autofix-auto"
            : "wolfmed-autodoc-ui-autofix-installed");
        _autofixLabel.FontColorOverride = !state.AutofixModule ? AmberDim : state.Auto ? Cyan : Good;
        _trayLabel.Text = Loc.GetString("wolfmed-autodoc-ui-tray", ("item", state.TrayItem ?? Loc.GetString("wolfmed-autodoc-ui-none")));
        _trayLabel.FontColorOverride = state.State == AutodocState.Waiting ? Cyan : Amber;
        _modeLabel.Text = Loc.GetString(state.SelfService ? "wolfmed-autodoc-ui-mode-self" : "wolfmed-autodoc-ui-mode-operator");

        _anaesthesia.Pressed = state.Anaesthesia;

        var running = state.State is AutodocState.Preparing or AutodocState.Step or AutodocState.Waiting;
        _startButton.Disabled = running || !state.Occupied || state.Queue.Count == 0;

        _planButton.Text = Loc.GetString(state.SelfService ? "wolfmed-autodoc-ui-fixme" : "wolfmed-autodoc-ui-plan");
        _planButton.ToolTip = Loc.GetString(state.SelfService
            ? "wolfmed-autodoc-ui-fixme-hint"
            : "wolfmed-autodoc-ui-plan-hint");
        _planButton.Disabled = running || !state.Occupied;

        _autoButton.Text = Loc.GetString(state.Auto ? "wolfmed-autodoc-ui-auto-on" : "wolfmed-autodoc-ui-auto-off");
        _autoButton.Label.FontColorOverride = state.Auto ? Good : AmberDim;
        _autoButton.Disabled = !state.AutofixModule;
        _autoButton.ToolTip = Loc.GetString(state.AutofixModule
            ? "wolfmed-autodoc-ui-auto-hint"
            : "wolfmed-autodoc-ui-auto-needs-module");
        _pauseButton.Disabled = !running && state.State != AutodocState.Paused;
        _pauseButton.Text = Loc.GetString(state.State == AutodocState.Paused ? "wolfmed-autodoc-ui-resume" : "wolfmed-autodoc-ui-pause");
        _abortButton.Disabled = state.State == AutodocState.Idle;
        _ejectButton.Disabled = !state.Occupied;
        _cutButton.Disabled = !state.ClothingBlocked;
        _cutButton.Label.FontColorOverride = state.ClothingBlocked ? Alert : AmberFaint;

        DrawProcedures(state);
        DrawQueue(state);
        DrawReservoir(state);
    }

    private void SetSpeech(string text)
    {
        if (text == _speechText)
            return;

        _speechText = text;
        _speech.Text = text;
    }

    private void DrawProcedures(AutodocBuiState state)
    {
        _procedures.RemoveAllChildren();
        var busy = state.State is AutodocState.Preparing or AutodocState.Step or AutodocState.Waiting;

        if (!state.Occupied)
        {
            _procedures.AddChild(Text(Loc.GetString("wolfmed-autodoc-ui-procedures-empty"), 11, AmberDim));
            return;
        }

        var visible = state.Available
            .Where(entry => _filter == null || entry.Part == _filter)
            .ToList();
        if (visible.Count == 0)
        {
            _procedures.AddChild(Text(Loc.GetString("wolfmed-autodoc-ui-procedures-nothing"), 11, AmberDim));
            return;
        }

        var groups = visible
            .GroupBy(entry => _surgeryCategory.GetValueOrDefault(entry.Surgery.Id, _fallbackCategory))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var category in _categories)
        {
            if (!groups.TryGetValue(category.ID, out var entries))
                continue;

            var collapsed = _collapsed.Contains(category.ID);
            var known = entries.Count(entry => entry.Known);
            var header = FlatButton(
                $"{(collapsed ? "[+]" : "[-]")} {Loc.GetString(category.Name).ToUpperInvariant()}  ({known}/{entries.Count})",
                collapsed ? AmberDim : Amber);
            header.HorizontalExpand = true;
            header.TextAlign = Label.AlignMode.Left;
            if (category.Note != null)
                header.ToolTip = Loc.GetString(category.Note);
            var id = category.ID;
            header.OnPressed += _ =>
            {
                if (!_collapsed.Remove(id))
                    _collapsed.Add(id);
                if (_state != null)
                    DrawProcedures(_state);
            };
            _procedures.AddChild(header);

            if (collapsed)
                continue;

            foreach (var entry in entries.OrderBy(e => e.Part).ThenBy(e => SurgeryName(e.Surgery)))
                _procedures.AddChild(ProcedureRow(entry, busy));
        }
    }

    private Control ProcedureRow(AutodocProcedureEntry entry, bool busy)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, Margin = new Thickness(12, 1, 0, 1) };
        var colour = entry.Known ? Amber : AmberFaint;
        var name = Text(SurgeryName(entry.Surgery), 11, colour);
        name.HorizontalExpand = true;
        name.ToolTip = entry.Known
            ? SurgeryDescription(entry.Surgery)
            : Loc.GetString("wolfmed-autodoc-ui-locked", ("program", ProgramName(entry.Surgery)));
        row.AddChild(name);
        row.AddChild(Text(PartName(entry.Part).ToUpperInvariant() + "  ", 10, entry.Known ? Cyan : AmberFaint));

        if (entry.Known)
        {
            var add = FlatButton(Loc.GetString("wolfmed-autodoc-ui-queue-add"), Good);
            add.Disabled = busy;
            var captured = entry;
            add.OnPressed += _ => OnQueueAdd?.Invoke(captured.Surgery, captured.Part);
            row.AddChild(add);
        }
        else
        {
            row.AddChild(Text(Loc.GetString("wolfmed-autodoc-ui-needs-disk", ("program", ProgramName(entry.Surgery))), 10, AmberFaint));
        }

        return row;
    }

    private void DrawQueue(AutodocBuiState state)
    {
        _queueBox.RemoveAllChildren();
        _queueHeadingCount.Text = state.Queue.Count == 0
            ? Loc.GetString("wolfmed-autodoc-ui-queue-empty")
            : Loc.GetString("wolfmed-autodoc-ui-queue-count", ("count", state.Queue.Count));

        // The same bound the server applies to a move or a remove, read off the pod's state rather than off
        // each row: the buttons around the running procedure used to be live and do nothing.
        var first = AutodocQueueRules.FirstMovable(state.State);

        for (var i = 0; i < state.Queue.Count; i++)
        {
            var entry = state.Queue[i];
            var index = i;
            var current = i == 0 && state.State is AutodocState.Preparing or AutodocState.Step or AutodocState.Waiting;

            var block = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, Margin = new Thickness(0, 1, 0, 3) };
            var header = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, SeparationOverride = 4 };
            var title = Text($"{(current ? ">" : " ")}{index + 1:00}  {SurgeryName(entry.Surgery)}  [{PartName(entry.Part).ToUpperInvariant()}]", 11, current ? Good : Amber, current);
            title.HorizontalExpand = true;
            header.AddChild(title);

            if (!state.SelfService)
            {
                var up = FlatButton("^", AmberDim);
                up.Disabled = index <= first;
                up.OnPressed += _ => OnQueueMove?.Invoke(index, true);
                var down = FlatButton("v", AmberDim);
                down.Disabled = index < first || index >= state.Queue.Count - 1;
                down.OnPressed += _ => OnQueueMove?.Invoke(index, false);
                var remove = FlatButton("x", Alert);
                remove.Disabled = index < first;
                remove.OnPressed += _ => OnQueueRemove?.Invoke(index);
                header.AddChild(up);
                header.AddChild(down);
                header.AddChild(remove);
            }

            block.AddChild(header);
            foreach (var requirement in entry.Requirements)
            {
                var waiting = requirement.Contains(Loc.GetString("wolfmed-autodoc-requirement-waiting"));
                block.AddChild(Text("      " + requirement, 10, waiting ? Cyan : AmberDim));
            }

            _queueBox.AddChild(block);
        }
    }

    private void DrawReservoir(AutodocBuiState state)
    {
        _reservoirBox.RemoveAllChildren();
        var slot = 1;
        foreach (var entry in state.Reservoir)
        {
            var fraction = entry.Max > 0f ? entry.Volume / entry.Max : 0f;
            var colour = entry.Usable ? Amber : AmberFaint;
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, SeparationOverride = 6 };
            row.AddChild(Text($"{slot++}", 11, AmberDim));
            row.AddChild(Text(Gauge(fraction, 12), 11, colour));
            // Passed as a padded string, not a number, so the column stays aligned.
            row.AddChild(Text(Loc.GetString("wolfmed-autodoc-ui-reservoir-volume", ("units", $"{(int) entry.Volume,3}")), 11, colour));
            var name = Text(entry.Name, 11, colour);
            name.HorizontalExpand = true;
            name.ClipText = true;
            name.ToolTip = entry.Usable ? null : Loc.GetString("wolfmed-autodoc-ui-reservoir-unusable", ("list", state.ReservoirAccepted));
            row.AddChild(name);
            _reservoirBox.AddChild(row);
        }
    }

    private string SurgeryName(EntProtoId surgery) =>
        _protos.TryIndex(surgery, out EntityPrototype? proto) ? proto.Name : surgery.Id;

    private string SurgeryDescription(EntProtoId surgery) =>
        _protos.TryIndex(surgery, out EntityPrototype? proto) ? proto.Description : string.Empty;

    private string ProgramName(EntProtoId surgery) =>
        _surgeryProgram.TryGetValue(surgery.Id, out var name) ? Loc.GetString(name) : Loc.GetString("wolfmed-autodoc-ui-none");

    private string StepName(string step) =>
        _protos.TryIndex(step, out EntityPrototype? proto) ? proto.Name : step;

    private static string PartName(TargetBodyPart part) =>
        Loc.GetString($"wolfmed-autodoc-part-{part.ToString().ToLowerInvariant()}");
}
