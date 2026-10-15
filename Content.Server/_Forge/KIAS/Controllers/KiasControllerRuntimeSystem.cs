using System.Linq;
using System.Buffers;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Power;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._Forge.KIAS.Controllers;

public sealed partial class KiasControllerRuntimeSystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private KiasControllerPhysicalSystem _physical = default!;
    [Dependency] private KiasControllerIoSystem _io = default!;
    [Dependency] private IGameTiming _timing = default!;
    private sealed class Runtime
    {
        public EntityUid Card, Rack, Grid;
        public uint Revision;
        public long Epoch;
        public KiasGraphMachine? Machine;
        public string Status = "INVALID";
        public string Fault = string.Empty;
        public readonly Dictionary<int, List<EntityUid>> Matches = new();
        public readonly Dictionary<int, Timer> Timers = new();
        public readonly Dictionary<int, string[]> InputNames = new();
        public readonly List<(EntityUid Device, string Profile, string Port)> Specific = new();
        public readonly List<(EntityUid Grid, string Profile, string Port)> Selectors = new();
    }
    private sealed class Cause
    {
        public readonly uint OriginTick;
        public Cause(uint originTick) => OriginTick = originTick;
        public readonly Dictionary<(EntityUid Card, int Node, EntityUid Device, string Port), int> Commands = new();
    }
    private Cause? _cause;
    private sealed record Timer(Runtime Runtime, int Node, uint Token, double Due, long Sequence);
    private readonly record struct Actuation(Runtime Runtime, KiasControllerNode Node, EntityUid? Target, string Port,
        KiasGraphValue Value, KiasGraphValue[] Inputs, Cause Cause, uint QueuedTick);
    private readonly record struct Emission(EntityUid Device, string Profile, string Port, KiasGraphValue Value,
        Cause Cause, long Epoch, uint QueuedTick);
    private readonly SortedSet<Timer> _timers = new(Comparer<Timer>.Create((a, b) =>
        a.Due == b.Due ? a.Sequence.CompareTo(b.Sequence) : a.Due.CompareTo(b.Due)));
    private readonly Queue<Timer> _waitingTimers = new();
    private sealed record PendingDispatch(Emission Emission, (Runtime Runtime, int Node)[] Endpoints, int Count, int Offset);
    private readonly List<PendingDispatch> _waitingDispatches = new();
    private readonly Dictionary<EntityUid, Runtime> _cards = new();
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _racks = new();
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _grids = new();
    private readonly Dictionary<(EntityUid Device, string Profile, string Port), List<(Runtime Runtime, int Node)>> _specific = new();
    private readonly Dictionary<(EntityUid Grid, string Profile, string Port), List<(Runtime Runtime, int Node)>> _selectors = new();
    private readonly Queue<(EntityUid Rack, EntityUid Card)> _boots = new();
    private readonly HashSet<EntityUid> _booting = new();
    private readonly Queue<Emission> _events = new();
    private readonly Queue<(Runtime Runtime, int Node, EntityUid Device, string Port, KiasGraphValue Value, Cause Cause, uint QueuedTick)> _work = new();
    private readonly Queue<Actuation> _commands = new();
    private EntityUid? _closingGrid;
    private readonly Queue<Emission> _finalEvents = new();
    private Emission? _dispatch;
    private (Runtime Runtime, int Node)[]? _dispatchEndpoints;
    private int _dispatchCount, _dispatchOffset;
    public int PendingEventDispatchCount => _dispatchCount - _dispatchOffset
        + _waitingDispatches.Sum(pending => pending.Count - pending.Offset);
    public int PendingTimerCount => _timers.Count + _waitingTimers.Count;
    private readonly Queue<Actuation> _finalCommands = new();
    private long _sequence, _eventEpoch;
    public readonly KiasRuntimeQueueMetrics EventQueueMetrics = new(), WorkQueueMetrics = new(), CommandQueueMetrics = new();
    public readonly KiasRuntimeQueueMetrics AcceptedCommandLatency = new();
    public readonly Dictionary<(string Profile, string Port), KiasRuntimeQueueMetrics> ActuatorDispatchLatency = new();
    public long CancelledCommands, FeedbackRejectedCommands, ActuatorExceptions;
    private Actuation? _executingCommand;
    private bool _commandDispatched;
    public int RunningCount(EntityUid rack) => _racks.TryGetValue(rack, out var cards)
        ? cards.Count(uid => _cards.TryGetValue(uid, out var runtime) && Valid(runtime)) : 0;
    public string Status(EntityUid card) => _cards.TryGetValue(card, out var runtime)
        ? runtime.Machine?.Fault.Length > 0 ? "FAULT" : runtime.Status : "OFFLINE";
    public string Fault(EntityUid card) => _cards.TryGetValue(card, out var runtime)
        ? runtime.Machine?.Fault.Length > 0 ? runtime.Machine.Fault : runtime.Fault : string.Empty;
    public bool Running(EntityUid card) => _cards.TryGetValue(card, out var runtime) && Valid(runtime);
    public KiasGraphValue LastValue(EntityUid card, int node, string port) =>
        _cards.TryGetValue(card, out var runtime) && runtime.Machine != null ? runtime.Machine.Value(node, port) : default;

    public void ProbeSource(EntityUid device, string port)
    {
        if (!_kias.IsOnline(device) || Transform(device).GridUid is not { } grid) return;
        var budget = 2048;
        foreach (var profile in _io.Profiles(device))
        {
            if (!profile.StartsWith("Link.", StringComparison.Ordinal)) continue;
            var output = "out:" + port;
            void Probe(List<(Runtime Runtime, int Node)> endpoints)
            {
                foreach (var (runtime, node) in endpoints)
                {
                    if (budget <= 0) break;
                    if (Valid(runtime) && runtime.Matches.TryGetValue(node, out var matches) && matches.Contains(device))
                        budget -= runtime.Machine!.ProbeExternal(node, output, KiasGraphValue.Pulse, device, budget);
                }
            }
            if (_specific.TryGetValue((device, profile, output), out var specific)) Probe(specific);
            if (_selectors.TryGetValue((grid, profile, output), out var selectors)) Probe(selectors);
        }
    }

    public override void Initialize()
    {
        _io.Emitted += OnEmission;
        _io.CommandDispatched += OnCommandDispatched;
        SubscribeLocalEvent<KiasTopologyChangedEvent>(OnTopology, after: new[] { typeof(KiasControllerIoSystem) });
        SubscribeLocalEvent<KiasAvailabilityChangedEvent>(OnAvailability);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoved);
        SubscribeLocalEvent<KiasControllerRackComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<KiasControllerRackComponent, PowerChangedEvent>(OnPower);
        SubscribeLocalEvent<KiasControllerRackComponent, AnchorStateChangedEvent>(OnAnchor);
        SubscribeLocalEvent<KiasControllerRackComponent, EntParentChangedMessage>(OnParent);
        SubscribeLocalEvent<KiasControllerCardComponent, ComponentShutdown>(OnCardShutdown);
    }
    public override void Shutdown()
    {
        _io.Emitted -= OnEmission; _io.CommandDispatched -= OnCommandDispatched;
        ReleaseDispatch();
        foreach (var pending in _waitingDispatches)
            ArrayPool<(Runtime Runtime, int Node)>.Shared.Return(pending.Endpoints, clearArray: true);
        _waitingDispatches.Clear(); _waitingTimers.Clear();
        base.Shutdown();
    }

    private void OnCommandDispatched(EntityUid grid, EntityUid card, EntityUid device, string profile, string port)
    {
        if (!_kias.MeasureUpdates || _executingCommand is not { } command
            || command.Runtime.Card != card || command.Runtime.Grid != grid) return;
        _commandDispatched = true;
        AcceptedCommandLatency.Enqueue(0);
        AcceptedCommandLatency.Consume(command.Cause.OriginTick, _timing.CurTick.Value);
        var key = (profile, port);
        if (!ActuatorDispatchLatency.TryGetValue(key, out var metrics))
            ActuatorDispatchLatency.Add(key, metrics = new());
        metrics.Enqueue(0);
        metrics.Consume(command.Cause.OriginTick, _timing.CurTick.Value);
    }

    private void OnEmission(EntityUid device, string profile, string port, KiasGraphValue value)
    {
        if (!_specific.ContainsKey((device, profile, port))
            && (Transform(device).GridUid is not { } sourceGrid || !_selectors.ContainsKey((sourceGrid, profile, port)))) return;
        var queue = Transform(device).GridUid == _closingGrid && _closingGrid != null ? _finalEvents : _events;
        if (queue.Count + (queue == _events ? _waitingDispatches.Count : 0) < 4096)
        {
            queue.Enqueue(new(device, profile, port, value, _cause ?? new(_timing.CurTick.Value), ++_eventEpoch, _timing.CurTick.Value));
            if (_kias.MeasureUpdates) EventQueueMetrics.Enqueue(queue.Count);
        }
        else
        {
            if (_kias.MeasureUpdates) EventQueueMetrics.Rejected++;
            if (Transform(device).GridUid is { } grid)
                FaultSubscribers(grid, device, profile, port);
        }
    }
    private void FaultSubscribers(EntityUid grid, EntityUid device, string profile, string port)
    {
        if (_specific.TryGetValue((device, profile, port), out var specific))
            foreach (var (runtime, node) in specific.ToArray())
                if (runtime.Matches.TryGetValue(node, out var matches) && matches.Contains(device)) Fail(runtime, "event-queue-limit");
        if (_selectors.TryGetValue((grid, profile, port), out var selectors))
            foreach (var (runtime, node) in selectors.ToArray())
                if (runtime.Matches.TryGetValue(node, out var matches) && matches.Contains(device)) Fail(runtime, "event-queue-limit");
    }

    public override void Update(float frameTime)
    {
        if (_kias.MeasureUpdates)
        {
            if (_events.Count > 0 || _dispatch != null || _waitingDispatches.Count > 0) EventQueueMetrics.BackloggedTicks++;
            if (_work.Count > 0) WorkQueueMetrics.BackloggedTicks++;
            if (_commands.Count > 0) CommandQueueMetrics.BackloggedTicks++;
        }
        var now = _timing.CurTime.TotalSeconds;
        for (var i = Math.Min(128, _waitingTimers.Count); i > 0; i--)
        {
            var waiting = _waitingTimers.Dequeue();
            if (!_cards.TryGetValue(waiting.Runtime.Card, out var live) || live != waiting.Runtime
                || live.Timers.GetValueOrDefault(waiting.Node) != waiting) continue;
            if (_kias.TopologyPending(live.Grid)) _waitingTimers.Enqueue(waiting);
            else _timers.Add(waiting);
        }
        if (_dispatch is { } current && SourcePending(current.Device)) SuspendDispatch();
        MovePendingToBack(_events, emission => SourcePending(emission.Device)
            || _waitingDispatches.Any(pending => !TerminatingOrDeleted(emission.Device)
                && !TerminatingOrDeleted(pending.Emission.Device)
                && Transform(emission.Device).GridUid == Transform(pending.Emission.Device).GridUid));
        MovePendingToBack(_work, work => _kias.TopologyPending(work.Runtime.Grid));
        MovePendingToBack(_commands, command => _kias.TopologyPending(command.Runtime.Grid));
        if (_boots.Count == 0 && _events.Count == 0 && _dispatch == null && _work.Count == 0 && _commands.Count == 0
            && _waitingDispatches.Count == 0 && (_timers.Min is not { } nextTimer || nextTimer.Due > now)) return;
        var bootStart = System.Diagnostics.Stopwatch.GetTimestamp();
        for (var i = 0; i < 16; i++)
        {
            if (i > 0 && System.Diagnostics.Stopwatch.GetElapsedTime(bootStart).TotalMilliseconds >= 4
                || !_boots.TryDequeue(out var boot)) break;
            _booting.Remove(boot.Card);
            if (!_cards.ContainsKey(boot.Card) && Available(boot.Rack, boot.Card)) Boot(boot.Rack, boot.Card);
            else if (!TerminatingOrDeleted(boot.Card) && Transform(boot.Card).ParentUid is var parent && HasComp<KiasControllerRackComponent>(parent)) Reconcile(parent);
        }
        var evaluations = 0;
        for (var i = 0; i < 128 && evaluations < 2048 && _timers.Min is { } timer && timer.Due <= now; i++)
        {
            _timers.Remove(timer);
            if (_kias.TopologyPending(timer.Runtime.Grid))
            {
                _waitingTimers.Enqueue(timer);
                continue;
            }
            timer.Runtime.Timers.Remove(timer.Node);
            if (Valid(timer.Runtime))
            {
                timer.Runtime.Machine!.TimerElapsed(timer.Node, timer.Token);
                evaluations += timer.Runtime.Machine.LastEvaluations;
                if (!timer.Runtime.Machine.Active) Fail(timer.Runtime, timer.Runtime.Machine.Fault);
            }
            else Stop(timer.Runtime.Card);
        }
        var dispatchBudget = 4096;
        for (var i = 0; i < 64 && evaluations < 2048; i++)
        {
            if (_dispatch == null && !ResumeDispatch())
            {
                if (!_events.TryPeek(out var ev)) break;
                if (!TerminatingOrDeleted(ev.Device) && Transform(ev.Device).GridUid is { } pendingGrid
                    && _kias.TopologyPending(pendingGrid)) break;
                _events.Dequeue();
                if (_kias.MeasureUpdates) EventQueueMetrics.Consume(ev.QueuedTick, _timing.CurTick.Value);
                if (!_kias.IsOnline(ev.Device) || Transform(ev.Device).GridUid is not { } grid) continue;
                BeginDispatch(ev, grid);
            }
            if (!QueueWork(ref dispatchBudget))
            {
                if (_dispatch is { } pending && SourcePending(pending.Device)) { SuspendDispatch(); continue; }
                break;
            }
        }
        for (var i = 0; i < 256 && evaluations < 2048 && _work.TryPeek(out var work); i++)
        {
            if (_kias.TopologyPending(work.Runtime.Grid)) break;
            _work.Dequeue();
            if (_kias.MeasureUpdates) WorkQueueMetrics.Consume(work.QueuedTick, _timing.CurTick.Value);
            var runtime = work.Runtime;
            if (!Valid(runtime) || !_kias.IsOnline(work.Device)
                || !runtime.Matches.TryGetValue(work.Node, out var matches) || !matches.Contains(work.Device)) continue;
            _cause = work.Cause;
            try { runtime.Machine!.EmitExternal(work.Node, work.Port, work.Value, work.Device); }
            finally { _cause = null; }
            evaluations += runtime.Machine.LastEvaluations;
            if (!runtime.Machine.Active) Fail(runtime, runtime.Machine.Fault);
        }
        for (var i = 0; i < 128 && _commands.TryPeek(out var command); i++)
        {
            if (_kias.TopologyPending(command.Runtime.Grid)) break;
            _commands.Dequeue();
            Execute(command);
        }
    }

    private bool SourcePending(EntityUid device) => !TerminatingOrDeleted(device)
        && Transform(device).GridUid is { } grid && _kias.TopologyPending(grid);

    private static void MovePendingToBack<T>(Queue<T> queue, Func<T, bool> pending)
    {
        if (queue.Count == 0) return;
        List<T>? deferred = null;
        for (var i = queue.Count; i > 0; i--)
        {
            var item = queue.Dequeue();
            if (pending(item)) (deferred ??= new()).Add(item);
            else queue.Enqueue(item);
        }
        if (deferred != null) foreach (var item in deferred) queue.Enqueue(item);
    }

    private void SuspendDispatch()
    {
        _waitingDispatches.Add(new(_dispatch!.Value, _dispatchEndpoints!, _dispatchCount, _dispatchOffset));
        _dispatch = null; _dispatchEndpoints = null;
        _dispatchCount = _dispatchOffset = 0;
    }

    private bool ResumeDispatch()
    {
        var index = _waitingDispatches.FindIndex(pending => !SourcePending(pending.Emission.Device));
        if (index < 0) return false;
        var pending = _waitingDispatches[index];
        _waitingDispatches.RemoveAt(index);
        _dispatch = pending.Emission; _dispatchEndpoints = pending.Endpoints;
        _dispatchCount = pending.Count; _dispatchOffset = pending.Offset;
        return true;
    }

    private void Execute(Actuation command)
    {
        if (_kias.MeasureUpdates) CommandQueueMetrics.Consume(command.QueuedTick, _timing.CurTick.Value);
        if (!Valid(command.Runtime)) { if (_kias.MeasureUpdates) CancelledCommands++; return; }
        var target = command.Target ?? command.Runtime.Matches.GetValueOrDefault(command.Node.Id)?
            .FirstOrDefault(uid => _kias.IsOnline(uid) && Transform(uid).GridUid == command.Runtime.Grid);
        if (target is not { } device || device == default
            || !command.Runtime.Matches.TryGetValue(command.Node.Id, out var current) || !current.Contains(device))
        { if (_kias.MeasureUpdates) CancelledCommands++; return; }
        var key = (command.Runtime.Card, command.Node.Id, device, command.Port);
        var count = command.Cause.Commands.GetValueOrDefault(key);
        if (count >= 32 || count == 0 && command.Cause.Commands.Count >= 4096)
        { if (_kias.MeasureUpdates) FeedbackRejectedCommands++; Fail(command.Runtime, "external-feedback-budget"); return; }
        command.Cause.Commands[key] = count + 1;
        _cause = command.Cause;
        _executingCommand = command;
        _commandDispatched = false;
        try
        {
            _io.Command(command.Runtime.Grid, command.Runtime.Card, device, command.Node, command.Port, command.Value,
                new KiasCommandInputs(command.Runtime.InputNames.GetValueOrDefault(command.Node.Id, Array.Empty<string>()), command.Inputs));
            if (_kias.MeasureUpdates && !_commandDispatched) CancelledCommands++;
        }
        catch (Exception exception)
        {
            if (_kias.MeasureUpdates) ActuatorExceptions++;
            Fail(command.Runtime, "actuator-exception");
            Log.Error($"KIAS controller {command.Runtime.Card}, node {command.Node.Id}: {exception}");
        }
        finally { _cause = null; _executingCommand = null; }
    }

    public bool IsFinishingShutdown(EntityUid grid) => _closingGrid == grid;

    public void FinishBeforeShutdown(EntityUid grid, Action publish)
    {
        if (_closingGrid != null) return;
        _closingGrid = grid;
        var evaluations = 0;
        var commands = 0;
        try
        {
            publish();
            while ((_finalEvents.Count > 0 || _finalCommands.Count > 0) && evaluations < 4096 && commands < 1024)
            {
                for (var i = 0; i < 64 && evaluations < 4096 && _finalEvents.TryDequeue(out var ev); i++)
                {
                    if (_kias.MeasureUpdates) EventQueueMetrics.Consume(ev.QueuedTick, _timing.CurTick.Value);
                    void Deliver(List<(Runtime Runtime, int Node)> endpoints)
                    {
                        foreach (var (runtime, node) in endpoints.ToArray())
                        {
                            if (!Valid(runtime) || runtime.Epoch >= ev.Epoch || !_kias.IsOnline(ev.Device)
                                || !runtime.Matches.TryGetValue(node, out var matches) || !matches.Contains(ev.Device)) continue;
                            _cause = ev.Cause;
                            try { runtime.Machine!.EmitExternal(node, ev.Port, ev.Value, ev.Device); }
                            finally { _cause = null; }
                            evaluations += runtime.Machine.LastEvaluations;
                            if (!runtime.Machine.Active) Fail(runtime, runtime.Machine.Fault);
                            if (evaluations >= 4096) break;
                        }
                    }
                    if (_specific.TryGetValue((ev.Device, ev.Profile, ev.Port), out var specific)) Deliver(specific);
                    if (_selectors.TryGetValue((grid, ev.Profile, ev.Port), out var selectors)) Deliver(selectors);
                }
                for (var i = 0; i < 128 && commands < 1024 && _finalCommands.TryDequeue(out var command); i++)
                { Execute(command); commands++; }
            }
        }
        finally { _closingGrid = null; _cause = null; _finalEvents.Clear(); _finalCommands.Clear(); }
    }

    private void BeginDispatch(Emission emission, EntityUid grid)
    {
        _specific.TryGetValue((emission.Device, emission.Profile, emission.Port), out var specific);
        _selectors.TryGetValue((grid, emission.Profile, emission.Port), out var selectors);
        _dispatchCount = (specific?.Count ?? 0) + (selectors?.Count ?? 0);
        if (_dispatchCount == 0) return;
        _dispatchEndpoints = ArrayPool<(Runtime Runtime, int Node)>.Shared.Rent(_dispatchCount);
        specific?.CopyTo(_dispatchEndpoints, 0);
        selectors?.CopyTo(_dispatchEndpoints, specific?.Count ?? 0);
        _dispatch = emission;
    }

    private bool QueueWork(ref int budget)
    {
        if (_dispatch is not { } emission) return true;
        if (TerminatingOrDeleted(emission.Device))
        {
            ReleaseDispatch();
            return true;
        }
        if (Transform(emission.Device).GridUid is { } grid && _kias.TopologyPending(grid)) return false;
        if (!_kias.IsOnline(emission.Device))
        {
            ReleaseDispatch();
            return true;
        }
        while (_dispatchOffset < _dispatchCount && _work.Count < 4096 && budget > 0)
        {
            budget--;
            var (runtime, node) = _dispatchEndpoints![_dispatchOffset++];
            if (!Valid(runtime) || runtime.Epoch >= emission.Epoch
                || !runtime.Matches.TryGetValue(node, out var matches) || !matches.Contains(emission.Device)) continue;
            _work.Enqueue((runtime, node, emission.Device, emission.Port, emission.Value, emission.Cause, _timing.CurTick.Value));
            if (_kias.MeasureUpdates) WorkQueueMetrics.Enqueue(_work.Count);
        }
        if (_dispatchOffset < _dispatchCount) return false;
        ReleaseDispatch();
        return true;
    }

    private void ReleaseDispatch()
    {
        if (_dispatchEndpoints != null)
            ArrayPool<(Runtime Runtime, int Node)>.Shared.Return(_dispatchEndpoints, clearArray: true);
        _dispatchEndpoints = null;
        _dispatch = null;
        _dispatchCount = _dispatchOffset = 0;
    }

    private static readonly string[] RackSlots = Enumerable.Range(0, 8).Select(KiasControllerRackComponent.SlotId).ToArray();
    private bool Available(EntityUid rack, EntityUid card)
    {
        if (!_kias.IsOnline(rack) || TerminatingOrDeleted(card)
            || !TryComp<KiasControllerCardComponent>(card, out var component) || !component.Enabled
            || Transform(card).ParentUid != rack) return false;
        foreach (var slot in RackSlots) if (_physical.Card(rack, slot) == card) return true;
        return false;
    }

    private bool Valid(Runtime runtime) => runtime.Machine is { Active: true } && Available(runtime.Rack, runtime.Card)
        && Transform(runtime.Rack).GridUid == runtime.Grid && Comp<KiasControllerCardComponent>(runtime.Card).Revision == runtime.Revision;

    public void ContainerChanged(EntityUid rack, EntityUid? removed = null)
    {
        if (removed is { } card) Stop(card);
        Reconcile(rack);
    }
    private void OnShutdown(Entity<KiasControllerRackComponent> ent, ref ComponentShutdown args) => StopRack(ent);
    private void OnCardShutdown(Entity<KiasControllerCardComponent> ent, ref ComponentShutdown args) => Stop(ent);
    private void OnPower(Entity<KiasControllerRackComponent> ent, ref PowerChangedEvent args) { if (!args.Powered) StopRack(ent); else Reconcile(ent); }
    private void OnAnchor(Entity<KiasControllerRackComponent> ent, ref AnchorStateChangedEvent args) { if (!args.Anchored) StopRack(ent); }
    private void OnParent(Entity<KiasControllerRackComponent> ent, ref EntParentChangedMessage args) => StopRack(ent);
    private void OnAvailability(ref KiasAvailabilityChangedEvent args) { if (!args.Active) StopGrid(args.Grid); }
    private void OnGridRemoved(GridRemovalEvent args) => StopGrid(args.EntityUid);
    private void OnTopology(ref KiasTopologyChangedEvent args)
    {
        if (!TryComp<KiasGridComponent>(args.Grid, out var grid)) { StopGrid(args.Grid); return; }
        if (!_grids.TryGetValue(args.Grid, out var previous)) _grids[args.Grid] = previous = new();
        foreach (var rack in previous.ToArray())
            if (TerminatingOrDeleted(rack) || Transform(rack).GridUid != args.Grid || !grid.Devices.Contains(rack)) { StopRack(rack); previous.Remove(rack); }
        foreach (var rack in grid.Devices)
            if (HasComp<KiasControllerRackComponent>(rack)) { previous.Add(rack); Reconcile(rack); NotifyRack(rack); }
    }

    public void Reconcile(EntityUid rack)
    {
        if (!TerminatingOrDeleted(rack) && Transform(rack).GridUid is { } grid && _kias.TopologyPending(grid)) return;
        if (!_kias.IsOnline(rack)) { StopRack(rack); return; }
        if (!_racks.TryGetValue(rack, out var known)) _racks[rack] = known = new();
        var inserted = new HashSet<EntityUid>();
        for (var i = 0; i < 8; i++)
        {
            if (_physical.Card(rack, KiasControllerRackComponent.SlotId(i)) is not { } card || !Available(rack, card)) continue;
            inserted.Add(card);
            if (_cards.TryGetValue(card, out var runtime))
            {
                if (runtime.Rack != rack || runtime.Grid != Transform(rack).GridUid || runtime.Revision != Comp<KiasControllerCardComponent>(card).Revision) Stop(card);
                else { if (runtime.Machine is { Active: true }) Index(runtime); continue; }
            }
            if (_booting.Add(card)) _boots.Enqueue((rack, card));
        }
        foreach (var removed in known.Except(inserted).ToArray()) Stop(removed);
        _racks[rack] = inserted;
    }

    private void Boot(EntityUid rack, EntityUid card)
    {
        var component = Comp<KiasControllerCardComponent>(card);
        var runtime = new Runtime { Rack = rack, Card = card, Grid = Transform(rack).GridUid!.Value, Revision = component.Revision, Epoch = _eventEpoch };
        _cards[card] = runtime;
        var compilation = Compile(component.Program);
        if (!compilation.Success) { runtime.Fault = string.Join(", ", compilation.Errors); return; }
        runtime.Machine = new(compilation.Graph!, (node, port, value) => Command(runtime, node, port, value),
            (node, seconds, token) => Schedule(runtime, node, seconds, token), () => _timing.CurTime.TotalSeconds);
        foreach (var (node, names) in compilation.Graph!.DataInputs) runtime.InputNames[node] = names.ToArray();
        Index(runtime);
        runtime.Status = "RUNNING";
        runtime.Machine.Start(runtime.Matches.ToDictionary(pair => pair.Key, pair => pair.Value.Count));
        if (!runtime.Machine.Active) Fail(runtime, runtime.Machine.Fault);
        if (runtime.Machine.Active)
        {
            foreach (var node in runtime.Machine.Graph.Nodes.Values)
            {
                if (!runtime.Matches.TryGetValue(node.Id, out var matches)) continue;
                foreach (var device in matches.ToArray())
                foreach (var port in _io.Schema(node.Profile) ?? Array.Empty<KiasGraphPort>())
                {
                    if (port.Direction != KiasPortDirection.Output || port.Type == KiasPortType.Signal
                        || !_io.TryOutput(device, node.Profile, port.Id, out var value, out _)) continue;
                    runtime.Machine.EmitExternal(node.Id, port.Id, value, device);
                    if (!runtime.Machine.Active)
                    {
                        Fail(runtime, runtime.Machine.Fault);
                        return;
                    }
                }
            }
        }
        NotifyRack(rack);
    }

    private void Schedule(Runtime runtime, int node, double seconds, uint token)
    {
        if (runtime.Timers.Remove(node, out var previous)) _timers.Remove(previous);
        var timer = new Timer(runtime, node, token, _timing.CurTime.TotalSeconds + seconds, ++_sequence);
        runtime.Timers[node] = timer;
        _timers.Add(timer);
    }

    private void Command(Runtime runtime, KiasControllerNode node, string port, KiasGraphValue value)
    {
        if (value.Type is not (KiasPortType.Signal or KiasPortType.Bool)
            || !Available(runtime.Rack, runtime.Card) || !runtime.Matches.TryGetValue(node.Id, out var matches) || matches.Count == 0) return;
        var names = runtime.InputNames.GetValueOrDefault(node.Id, Array.Empty<string>());
        var inputs = names.Length == 0 ? Array.Empty<KiasGraphValue>() : new KiasGraphValue[names.Length];
        for (var i = 0; i < names.Length; i++) inputs[i] = runtime.Machine!.InputValue(node.Id, names[i]);
        void Enqueue(EntityUid? target)
        {
            var queue = runtime.Grid == _closingGrid ? _finalCommands : _commands;
            if (queue.Count < 4096)
            {
                queue.Enqueue(new(runtime, node, target, port, value, inputs, _cause ?? new(_timing.CurTick.Value), _timing.CurTick.Value));
                if (_kias.MeasureUpdates) CommandQueueMetrics.Enqueue(queue.Count);
            }
            else { if (_kias.MeasureUpdates) CommandQueueMetrics.Rejected++; Fail(runtime, "command-queue-limit"); }
        }
        if (node.Kind == KiasNodeKind.Any) { Enqueue(null); return; }
        var count = matches.Count;
        var targets = ArrayPool<EntityUid>.Shared.Rent(count);
        matches.CopyTo(targets);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var target = targets[i];
                if (!_kias.IsOnline(target) || Transform(target).GridUid != runtime.Grid) continue;
                Enqueue(target);
                if (runtime.Machine?.Active != true) break;
            }
        }
        finally { ArrayPool<EntityUid>.Shared.Return(targets, clearArray: true); }
    }

    private void Index(Runtime runtime)
    {
        Unindex(runtime);
        if (runtime.Machine == null) return;
        foreach (var node in runtime.Machine.Graph.Nodes.Values)
            if (KiasGraphCatalog.External(node.Kind)) runtime.Matches[node.Id] = _io.Match(runtime.Grid, node);
        foreach (var node in runtime.Machine.Graph.Nodes.Values)
        {
            if (!KiasGraphCatalog.External(node.Kind)) continue;
            foreach (var port in KiasGraphCatalog.DevicePorts(_io.Schema(node.Profile) ?? _io.SnapshotSchema(node) ?? Array.Empty<KiasGraphPort>()))
            {
                if (port.Direction != KiasPortDirection.Output || port.Id.StartsWith('$')) continue;
                if (!runtime.Machine.Graph.Outgoing.ContainsKey(new(node.Id, port.Id))
                    && !(port.Type == KiasPortType.Signal && runtime.Machine.Graph.Outgoing.ContainsKey(new(node.Id, "$Source")))) continue;
                if (node.Kind == KiasNodeKind.Specific && node.Binding is { } device)
                {
                    var key = (device, node.Profile, port.Id);
                    if (!_specific.TryGetValue(key, out var endpoints)) _specific[key] = endpoints = new();
                    endpoints.Add((runtime, node.Id)); runtime.Specific.Add(key);
                }
                else if (node.Kind != KiasNodeKind.Specific)
                {
                    var key = (runtime.Grid, node.Profile, port.Id);
                    if (!_selectors.TryGetValue(key, out var endpoints)) _selectors[key] = endpoints = new();
                    endpoints.Add((runtime, node.Id)); runtime.Selectors.Add(key);
                }
            }
        }
        foreach (var (node, matches) in runtime.Matches) runtime.Machine.MatchCount(node, matches.Count);
    }

    private void Unindex(Runtime runtime)
    {
        foreach (var key in runtime.Specific)
            if (_specific.TryGetValue(key, out var endpoints)) { endpoints.RemoveAll(endpoint => endpoint.Runtime == runtime); if (endpoints.Count == 0) _specific.Remove(key); }
        foreach (var key in runtime.Selectors)
            if (_selectors.TryGetValue(key, out var endpoints)) { endpoints.RemoveAll(endpoint => endpoint.Runtime == runtime); if (endpoints.Count == 0) _selectors.Remove(key); }
        runtime.Specific.Clear(); runtime.Selectors.Clear(); runtime.Matches.Clear();
    }
    private void NotifyRack(EntityUid rack)
    {
        if (!TerminatingOrDeleted(rack) && TryComp<KiasControllerRackComponent>(rack, out var component))
            EntityManager.System<KiasControllerUiSystem>().RefreshRack((rack, component));
        if (!TerminatingOrDeleted(rack) && Transform(rack).GridUid is { } grid)
            EntityManager.System<KiasDisplaySystem>().RefreshOpen(grid);
    }

    private void Fail(Runtime runtime, string reason)
    {
        runtime.Fault = reason; runtime.Status = "FAULT"; runtime.Machine?.Stop(); Unindex(runtime);
        foreach (var timer in runtime.Timers.Values) _timers.Remove(timer);
        runtime.Timers.Clear();
        NotifyRack(runtime.Rack);
    }
    private void Stop(EntityUid card)
    {
        if (!_cards.Remove(card, out var runtime)) return;
        runtime.Machine?.Stop(); Unindex(runtime);
        foreach (var timer in runtime.Timers.Values) _timers.Remove(timer);
        runtime.Timers.Clear();
        for (var i = _waitingTimers.Count; i > 0; i--)
        {
            var timer = _waitingTimers.Dequeue();
            if (timer.Runtime != runtime) _waitingTimers.Enqueue(timer);
        }
        NotifyRack(runtime.Rack);
    }
    private void StopRack(EntityUid rack)
    {
        var queued = _boots.Count;
        for (var i = 0; i < queued; i++)
        {
            var boot = _boots.Dequeue();
            if (boot.Rack == rack) _booting.Remove(boot.Card);
            else _boots.Enqueue(boot);
        }
        if (!_racks.Remove(rack, out var cards)) return;
        foreach (var card in cards) Stop(card);
    }
    private void StopGrid(EntityUid grid)
    {
        if (!_grids.Remove(grid, out var racks)) return;
        foreach (var rack in racks) StopRack(rack);
    }
}
