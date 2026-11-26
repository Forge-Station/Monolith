using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Shared._WF.NpcCrew;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.NpcCrew;

public sealed partial class WFCrewSetupWindow
{
    private readonly OptionButton _objectiveKind = new();
    private readonly OptionButton _objectiveTarget = new() { Filterable = true };
    private readonly LineEdit _duration = new() { Text = "0" };
    private readonly LineEdit _objectiveRange = new() { Text = "100" };
    private readonly LineEdit _objectiveX = new() { Text = "0" };
    private readonly LineEdit _objectiveY = new() { Text = "0" };
    private readonly BoxContainer _queueRows = Column(6);
    private readonly Label _queueTitle = new() { StyleClasses = { "LabelHeading" } };
    private readonly RichTextLabel _queueNotice = new();
    private readonly RichTextLabel _objectiveHelp = new();
    private BoxContainer _objectiveTargetLine = default!;
    private BoxContainer _objectivePositionLine = default!;
    private BoxContainer _objectiveTimingLine = default!;
    private BoxContainer _objectiveRangeLine = default!;
    private BoxContainer _liveButtons = default!;
    private BoxContainer _draftButtons = default!;
    private Button _addObjective = default!;
    private Button _cancelEdit = default!;
    private int? _editing;

    private void BuildObjectives(BoxContainer body)
    {
        var queue = Column();
        queue.AddChild(_queueTitle);
        queue.AddChild(_queueNotice);
        queue.AddChild(new ScrollContainer { MinHeight = 90, MaxHeight = 220, HScrollEnabled = false, ReserveScrollbarSpace = true, Children = { _queueRows } });
        _liveButtons = Buttons(Button("queue-pause", () => SendQueue(WFCrewSetupAction.Pause)),
            Button("queue-resume", () => SendQueue(WFCrewSetupAction.Resume)),
            Button("queue-skip", () => SendQueue(WFCrewSetupAction.Skip)), Button("edit-queue", BeginQueueEdit));
        _draftButtons = Buttons(Button("replace-queue", () => SendQueue(WFCrewSetupAction.Objectives)), Button("discard-draft", () =>
        {
            if (CurrentEdits is not { } edits)
                return;
            edits.Queue = null;
            edits.Version++;
            _editing = null;
            RenderQueue();
        }));
        queue.AddChild(_liveButtons);
        queue.AddChild(_draftButtons);
        body.AddChild(Card(queue));

        var editor = Column();
        editor.AddChild(Heading("add-task"));
        foreach (var kind in Enum.GetValues<WFCrewObjectiveKind>())
            _objectiveKind.AddItem(Loc.GetString($"wf-crew-objective-{kind.ToString().ToLowerInvariant()}"), (int) kind);
        SelectOnClick(_objectiveKind);
        SelectOnClick(_objectiveTarget);
        _objectiveKind.OnItemSelected += _ => SelectObjectiveKind();
        editor.AddChild(Line("task-type", _objectiveKind));
        _objectiveTargetLine = Line("target-grid", _objectiveTarget);
        _objectivePositionLine = Line("destination", _objectiveX, _objectiveY);
        _objectiveTimingLine = Line("duration-seconds", _duration);
        _objectiveRangeLine = Line("range-metres", _objectiveRange);
        editor.AddChild(_objectiveTargetLine);
        editor.AddChild(_objectivePositionLine);
        editor.AddChild(_objectiveRangeLine);
        editor.AddChild(_objectiveTimingLine);
        editor.AddChild(_objectiveHelp);
        _addObjective = Button("append-task", AddObjective);
        _cancelEdit = Button("cancel-edit", () => { _editing = null; RenderQueue(); });
        editor.AddChild(Buttons(_addObjective, _cancelEdit));
        body.AddChild(Card(editor));
        ResetObjectiveEditor();
    }

    private static bool NeedsTarget(WFCrewObjectiveKind kind) => kind is not
        (WFCrewObjectiveKind.Hold or WFCrewObjectiveKind.GoTo or WFCrewObjectiveKind.Undock or WFCrewObjectiveKind.Repair);
    private static bool HasDuration(WFCrewObjectiveKind kind) => kind is WFCrewObjectiveKind.Hold or WFCrewObjectiveKind.Loiter
        or WFCrewObjectiveKind.Follow or WFCrewObjectiveKind.Attack or WFCrewObjectiveKind.Escort or WFCrewObjectiveKind.Circle;
    private static bool HasRange(WFCrewObjectiveKind kind) => kind is WFCrewObjectiveKind.GoTo or WFCrewObjectiveKind.Loiter
        or WFCrewObjectiveKind.Follow or WFCrewObjectiveKind.Attack or WFCrewObjectiveKind.Retreat or WFCrewObjectiveKind.Escort or WFCrewObjectiveKind.Circle;

    /// <summary>Choosing a new task supplies a useful distance without overwriting a loaded task's settings.</summary>
    private void SelectObjectiveKind()
    {
        var kind = (WFCrewObjectiveKind) _objectiveKind.SelectedId;
        var range = kind switch
        {
            WFCrewObjectiveKind.GoTo => 10,
            WFCrewObjectiveKind.Retreat => 40,
            WFCrewObjectiveKind.Circle or WFCrewObjectiveKind.Loiter => 150,
            WFCrewObjectiveKind.Attack => MathF.Max(1f, CurrentCrew?.Settings.Navigation.AttackRange ?? 350f),
            _ => 100,
        };
        _objectiveRange.Text = range.ToString(CultureInfo.InvariantCulture);
        UpdateObjectiveFields();
    }

    private void ResetObjectiveEditor()
    {
        _editing = null;
        _objectiveKind.SelectId((int) WFCrewObjectiveKind.Dock);
        _objectiveTarget.TrySelectId(-1);
        _duration.Text = "0";
        _objectiveRange.Text = "100";
        UpdateObjectiveFields();
    }

    private void UpdateObjectiveFields()
    {
        var kind = (WFCrewObjectiveKind) _objectiveKind.SelectedId;
        _objectivePositionLine.Visible = kind == WFCrewObjectiveKind.GoTo;
        _objectiveTargetLine.Visible = NeedsTarget(kind);
        _objectiveTimingLine.Visible = HasDuration(kind);
        _objectiveRangeLine.Visible = HasRange(kind);
        Plain(_objectiveHelp, Text($"task-help-{kind.ToString().ToLowerInvariant()}"));
    }

    /// <summary>Hidden fields use valid defaults so another task's unfinished input cannot invalidate this one.</summary>
    private WFCrewObjective ReadObjective()
    {
        var kind = (WFCrewObjectiveKind) _objectiveKind.SelectedId;
        return new WFCrewObjective
        {
            Kind = kind,
            Target = NeedsTarget(kind) ? (_objectiveTarget.SelectedMetadata as WFCrewSetupGrid)?.Id : null,
            Position = kind == WFCrewObjectiveKind.GoTo ? new Vector2(Number(_objectiveX), Number(_objectiveY)) : Vector2.Zero,
            Range = HasRange(kind) ? Number(_objectiveRange) : 100,
            Duration = HasDuration(kind) ? Number(_duration) : 0,
        };
    }

    private bool ValidateObjective(WFCrewObjective item)
    {
        if (NeedsTarget(item.Kind) && (item.Target == null || item.Target == _selectedCrew?.Grid || !_grids.Any(grid => grid.Id == item.Target)))
        {
            Plain(_status, Text("choose-target"));
            return false;
        }
        if (!InBounds(item.Position) || !float.IsFinite(item.Range)
            || item.Range is < 1 or > 5000 || !float.IsFinite(item.Duration) || item.Duration is < 0 or > 86400)
        {
            Plain(_status, Text("bad-numbers"));
            return false;
        }
        return true;
    }

    private void BeginQueueEdit()
    {
        if (CurrentCrew is not { } crew || CurrentEdits is not { } edits)
            return;
        edits.Queue = crew.Objectives.ToList();
        edits.Version++;
        _editing = null;
        RenderQueue();
    }

    private void AddObjective()
    {
        if (CurrentEdits is not { } edits)
            return;
        var item = ReadObjective();
        if (!ValidateObjective(item))
            return;
        if (edits.Queue == null)
        {
            SendQueue(WFCrewSetupAction.AppendObjective);
            return;
        }
        if (_editing is { } index && index < edits.Queue.Count)
            edits.Queue[index] = item;
        else if (edits.Queue.Count < 64)
            edits.Queue.Add(item);
        else
        {
            Plain(_status, Text("queue-full"));
            return;
        }
        edits.Version++;
        _editing = null;
        RenderQueue();
    }

    private void SendQueue(WFCrewSetupAction action)
    {
        if (CurrentCrew is not { } crew || CurrentEdits is not { } edits)
            return;
        var objectives = new List<WFCrewObjective>();
        if (action == WFCrewSetupAction.AppendObjective)
        {
            var item = ReadObjective();
            if (!ValidateObjective(item))
                return;
            objectives.Add(item);
        }
        else if (action == WFCrewSetupAction.Objectives)
        {
            if (edits.Queue == null || edits.Queue.Any(item => !ValidateObjective(item)))
                return;
            objectives = edits.Queue.ToList();
        }
        Request(new WFCrewSetupRequest
        {
            Action = action, Grid = crew.Grid, Mission = new WFCrewMission { Group = crew.Group }, Objectives = objectives,
        });
    }

    private void RenderQueue()
    {
        var draft = CurrentEdits?.Queue;
        var items = draft ?? CurrentCrew?.Objectives ?? new List<WFCrewObjective>();
        var view = items.Select(item => new QueueItemView(item.Kind, item.Target, item.Position, item.Range, item.Duration, DescribeObjective(item))).ToList();
        if (_queueView is { } shown && shown.Crew == _selectedCrew && shown.Draft == (draft != null)
            && shown.Editing == _editing && shown.Items.SequenceEqual(view))
            return;
        _queueView = new QueueView(_selectedCrew, draft != null, _editing, view);
        _queueRows.RemoveAllChildren();
        _queueTitle.Text = Text(draft == null ? "live-queue" : "draft-queue");
        Plain(_queueNotice, Text(draft == null ? "live-queue-help" : "draft-queue-help"));
        _liveButtons.Visible = draft == null;
        _draftButtons.Visible = draft != null;
        _addObjective.Text = Text(draft == null ? "append-task" : _editing == null ? "add-to-draft" : "save-task");
        _cancelEdit.Visible = _editing != null;
        if (items.Count == 0)
            _queueRows.AddChild(Help("empty-queue"));
        for (var index = 0; index < items.Count; index++)
        {
            var at = index;
            var item = items[index];
            var row = Column(3);
            var label = new RichTextLabel();
            Plain(label, Text("queue-entry", ("number", index + 1), ("task", view[index].Description)));
            row.AddChild(label);
            if (draft != null)
            {
                var up = Button("queue-up", () => MoveObjective(at, -1));
                var down = Button("queue-down", () => MoveObjective(at, 1));
                up.Disabled = at == 0;
                down.Disabled = at == items.Count - 1;
                row.AddChild(Buttons(Button("queue-edit", () => EditObjective(at, item)), up, down, Button("remove", () =>
                {
                    if (CurrentEdits is not { Queue: { } queue } edits || at >= queue.Count)
                        return;
                    queue.RemoveAt(at);
                    edits.Version++;
                    _editing = null;
                    RenderQueue();
                })));
            }
            _queueRows.AddChild(row);
        }
    }

    private void MoveObjective(int index, int direction)
    {
        if (CurrentEdits is not { Queue: { } queue } edits || index + direction < 0 || index + direction >= queue.Count)
            return;
        (queue[index], queue[index + direction]) = (queue[index + direction], queue[index]);
        edits.Version++;
        _editing = null;
        RenderQueue();
    }

    private void EditObjective(int index, WFCrewObjective item)
    {
        if (CurrentEdits is { } edits)
            edits.Version++;
        _editing = index;
        _objectiveKind.SelectId((int) item.Kind);
        _objectiveTarget.TrySelectId(_grids.FindIndex(grid => grid.Id == item.Target));
        _duration.Text = item.Duration.ToString(CultureInfo.InvariantCulture);
        _objectiveRange.Text = item.Range.ToString(CultureInfo.InvariantCulture);
        _objectiveX.Text = item.Position.X.ToString(CultureInfo.InvariantCulture);
        _objectiveY.Text = item.Position.Y.ToString(CultureInfo.InvariantCulture);
        UpdateObjectiveFields();
        RenderQueue();
    }

    private string DescribeObjective(WFCrewObjective item)
    {
        var name = Loc.GetString($"wf-crew-objective-{item.Kind.ToString().ToLowerInvariant()}");
        if (NeedsTarget(item.Kind))
            name = Text("task-at", ("task", name), ("target", item.Target is { } target ? ShipName(target) : Text("select-target")));
        else if (item.Kind == WFCrewObjectiveKind.GoTo)
            name = Text("task-coordinates", ("task", name), ("x", item.Position.X), ("y", item.Position.Y));
        if (HasRange(item.Kind))
            name = Text("task-range", ("task", name), ("range", item.Range));
        if (HasDuration(item.Kind))
            name = item.Duration == 0 ? Text("task-indefinite", ("task", name)) : Text("task-duration", ("task", name), ("seconds", item.Duration));
        return name;
    }
}
