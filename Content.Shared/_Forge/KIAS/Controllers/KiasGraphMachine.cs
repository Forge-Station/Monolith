using System.Linq;

namespace Content.Shared._Forge.KIAS.Controllers;

public sealed class KiasGraphMachine
{
    public const int DefaultBudget = 1024;
    public readonly KiasCompiledGraph Graph;
    public bool Active { get; private set; }
    public string Fault { get; private set; } = string.Empty;
    public int Budget = DefaultBudget;
    public int LastEvaluations { get; private set; }
    private readonly Action<KiasControllerNode, string, KiasGraphValue> _command;
    private readonly Action<int, double, uint> _schedule;
    private readonly Func<double> _time;
    private readonly Queue<(KiasGraphEndpoint Port, KiasGraphValue Value)> _queue = new();
    private readonly Dictionary<KiasGraphEndpoint, KiasGraphValue> _inputs = new();
    private readonly Dictionary<KiasGraphEndpoint, KiasGraphValue> _outputs = new();
    private readonly Dictionary<int, KiasGraphValue> _memory = new();
    private readonly Dictionary<int, uint> _timerTokens = new();
    private readonly Dictionary<(int Node, string Key), double> _cooldowns = new();
    private bool _draining;
    private uint _nextToken;

    public KiasGraphMachine(KiasCompiledGraph graph, Action<KiasControllerNode, string, KiasGraphValue> command,
        Action<int, double, uint> schedule, Func<double> time)
    {
        Graph = graph; _command = command; _schedule = schedule; _time = time;
    }

    public void Stop()
    {
        Active = false;
        _queue.Clear(); _inputs.Clear(); _outputs.Clear(); _memory.Clear(); _timerTokens.Clear(); _cooldowns.Clear();
    }

    public void Start(IReadOnlyDictionary<int, int>? matches = null)
    {
        Stop(); Fault = string.Empty; Active = true;
        foreach (var node in Graph.Nodes.Values)
        {
            switch (node.Kind)
            {
                case KiasNodeKind.BoolConstant: Output(node.Id, "Value", KiasGraphValue.Boolean(node.Config.Bool)); break;
                case KiasNodeKind.NumberConstant: Output(node.Id, "Value", KiasGraphValue.Numeric(node.Config.Number)); break;
                case KiasNodeKind.StringConstant: Output(node.Id, "Value", KiasGraphValue.String(node.Config.Text)); break;
                case KiasNodeKind.EnumConstant: Output(node.Id, "Value", KiasGraphValue.Enumeration(node.Config.Enum)); break;
                case KiasNodeKind.Latch: case KiasNodeKind.Toggle:
                    _memory[node.Id] = KiasGraphValue.Boolean(node.Config.Bool);
                    Output(node.Id, "Value", _memory[node.Id]); break;
                case KiasNodeKind.Counter:
                    _memory[node.Id] = KiasGraphValue.Numeric(node.Config.Number);
                    Output(node.Id, "Value", _memory[node.Id]); break;
                case KiasNodeKind.StringLatch:
                    _memory[node.Id] = KiasGraphValue.String(node.Config.Text);
                    Output(node.Id, "Stored", _memory[node.Id]); break;
                case KiasNodeKind.Clock:
                    _memory[node.Id] = KiasGraphValue.Boolean(node.Config.Bool);
                    if (node.Config.Bool) Schedule(node); break;
            }
        }
        Drain();
        if (matches != null) foreach (var (node, count) in matches) MatchCount(node, count);
        foreach (var node in Graph.Nodes.Values)
            if (node.Kind == KiasNodeKind.OnStart) Output(node.Id, "Started", KiasGraphValue.Pulse);
        Drain();
    }

    public void EmitExternal(int node, string port, KiasGraphValue value, EntityUid? source = null)
    {
        if (!Active || !Graph.Nodes.TryGetValue(node, out var record) || !KiasGraphCatalog.External(record.Kind)) return;
        Output(node, "$Source", KiasGraphValue.Reference(source), true);
        Output(node, port, value, true);
        Drain();
    }

    public void MatchCount(int node, int count)
    {
        if (!Active) return;
        Output(node, "$MatchedCount", KiasGraphValue.Numeric(count));
        Output(node, "$OnlineCount", KiasGraphValue.Numeric(count));
        Output(node, "$HasAny", KiasGraphValue.Boolean(count > 0));
        Drain();
    }

    public void Input(int node, string port, KiasGraphValue value)
    {
        if (!Active) return;
        _queue.Enqueue((new(node, port), value));
        Drain();
    }

    public void TimerElapsed(int id, uint token)
    {
        if (!Active || !_timerTokens.TryGetValue(id, out var current) || token != current || !Graph.Nodes.TryGetValue(id, out var node)) return;
        _timerTokens.Remove(id);
        if (node.Kind == KiasNodeKind.Clock)
        {
            if (!_memory.GetValueOrDefault(id).Bool) return;
            Output(id, "Tick", KiasGraphValue.Pulse);
            Schedule(node);
        }
        else if (node.Kind == KiasNodeKind.Timer) Output(id, "Elapsed", KiasGraphValue.Pulse);
        Drain();
    }

    public KiasGraphValue Value(int node, string port) => _outputs.GetValueOrDefault(new(node, port));
    public KiasGraphValue InputValue(int node, string port) => _inputs.GetValueOrDefault(new(node, port));

    public int ProbeExternal(int node, string port, KiasGraphValue value, EntityUid source, int budget = 2048)
    {
        if (budget <= 0) return 0;
        var now = _time();
        var until = now + 5;
        var timers = new List<(int Node, double Due, uint Token)>();
        var probe = new KiasGraphMachine(Graph, (_, _, _) => { },
            (id, delay, token) =>
            {
                if (timers.Count < 128) timers.Add((id, now + delay, token));
            }, () => now) { Active = Active, Budget = Math.Min(Budget, budget), _nextToken = _nextToken };
        foreach (var (key, item) in _inputs) probe._inputs[key] = item;
        foreach (var (key, item) in _outputs) probe._outputs[key] = item;
        foreach (var (key, item) in _memory) probe._memory[key] = item;
        foreach (var (key, item) in _timerTokens) probe._timerTokens[key] = item;
        foreach (var (key, item) in _cooldowns) probe._cooldowns[key] = item;
        probe.EmitExternal(node, port, value, source);
        var evaluations = probe.LastEvaluations;
        for (var i = 0; i < 128 && probe.Active && evaluations < budget && timers.Count > 0; i++)
        {
            var next = timers.MinBy(timer => timer.Due);
            if (next.Due > until) break;
            timers.Remove(next);
            now = next.Due;
            probe.Budget = Math.Min(Budget, budget - evaluations);
            probe.TimerElapsed(next.Node, next.Token);
            evaluations += probe.LastEvaluations;
        }
        probe.Stop();
        return Math.Min(evaluations, budget);
    }

    private void Schedule(KiasControllerNode node)
    {
        var token = ++_nextToken;
        _timerTokens[node.Id] = token;
        _schedule(node.Id, node.Config.Seconds, token);
    }

    private void Output(int node, string port, KiasGraphValue value, bool force = false)
    {
        if (!Active) return;
        var endpoint = new KiasGraphEndpoint(node, port);
        if (!Graph.Ports.TryGetValue(endpoint, out var descriptor) || descriptor.Direction != KiasPortDirection.Output || descriptor.Type != value.Type) return;
        if (!force && value.Type != KiasPortType.Signal && _outputs.TryGetValue(endpoint, out var old) && old == value) return;
        _outputs[endpoint] = value;
        if (!Graph.Outgoing.TryGetValue(endpoint, out var targets)) return;
        foreach (var target in targets) _queue.Enqueue((target, value));
    }

    private void Drain()
    {
        if (_draining || !Active) return;
        _draining = true;
        LastEvaluations = 0;
        try
        {
            while (Active && _queue.TryDequeue(out var work))
            {
                if (++LastEvaluations > Budget) { Fault = "evaluation-budget"; Stop(); break; }
                if (!Graph.Ports.TryGetValue(work.Port, out var port) || port.Direction != KiasPortDirection.Input || port.Type != work.Value.Type) continue;
                _inputs[work.Port] = work.Value;
                var node = Graph.Nodes[work.Port.Node];
                if (KiasGraphCatalog.External(node.Kind)) _command(node, work.Port.Port, work.Value);
                else Evaluate(node, work.Port.Port);
            }
        }
        finally { _draining = false; }
    }

    private void Evaluate(KiasControllerNode node, string changed)
    {
        KiasGraphValue Read(string port) => _inputs.GetValueOrDefault(new(node.Id, port));
        void Bool(bool value) => Output(node.Id, "Value", KiasGraphValue.Boolean(value));
        var a = Read("A"); var b = Read("B");
        switch (node.Kind)
        {
            case KiasNodeKind.And: Bool(a.Bool && b.Bool); break;
            case KiasNodeKind.Or: Bool(a.Bool || b.Bool); break;
            case KiasNodeKind.Xor: Bool(a.Bool != b.Bool); break;
            case KiasNodeKind.Not: Bool(!a.Bool); break;
            case KiasNodeKind.Nand: Bool(!(a.Bool && b.Bool)); break;
            case KiasNodeKind.Nor: Bool(!(a.Bool || b.Bool)); break;
            case KiasNodeKind.Xnor: Bool(a.Bool == b.Bool); break;
            case KiasNodeKind.If:
                if (changed == "Trigger") Output(node.Id, Read("Condition").Bool ? "True" : "False", KiasGraphValue.Pulse);
                break;
            case KiasNodeKind.Timer:
                if (changed == "Cancel") _timerTokens.Remove(node.Id);
                else if (changed == "Trigger") Schedule(node);
                break;
            case KiasNodeKind.Clock:
                var enabled = changed == "Enabled" ? Read("Enabled").Bool : _memory.GetValueOrDefault(node.Id).Bool;
                _memory[node.Id] = KiasGraphValue.Boolean(enabled);
                _timerTokens.Remove(node.Id);
                if (enabled) Schedule(node);
                break;
            case KiasNodeKind.Cooldown:
                var key = (node.Id, Read("Key").Text ?? string.Empty);
                if (changed == "Reset") _cooldowns.Remove(key);
                else if (changed == "Trigger" && _cooldowns.GetValueOrDefault(key, double.NegativeInfinity) <= _time())
                {
                    if (_cooldowns.Count >= 512 && !_cooldowns.ContainsKey(key))
                    {
                        foreach (var expired in _cooldowns.Where(pair => pair.Value <= _time()).Select(pair => pair.Key).ToArray()) _cooldowns.Remove(expired);
                        if (_cooldowns.Count >= 512) { Fault = "cooldown-budget"; Stop(); break; }
                    }
                    _cooldowns[key] = _time() + node.Config.Seconds;
                    Output(node.Id, "Ready", KiasGraphValue.Pulse);
                }
                break;
            case KiasNodeKind.Latch: case KiasNodeKind.Toggle:
                var value = changed != "Reset" && (node.Kind == KiasNodeKind.Latch || !_memory.GetValueOrDefault(node.Id).Bool);
                _memory[node.Id] = KiasGraphValue.Boolean(value); Bool(value); break;
            case KiasNodeKind.StringLatch:
                if (changed is not ("Store" or "Reset")) break;
                _memory[node.Id] = changed == "Reset" ? KiasGraphValue.String(node.Config.Text) : KiasGraphValue.String(Read("Value").Text ?? string.Empty);
                Output(node.Id, "Stored", _memory[node.Id], true);
                if (changed == "Store") Output(node.Id, "Saved", KiasGraphValue.Pulse);
                break;
            case KiasNodeKind.Counter:
                var count = changed == "Reset" ? node.Config.Number
                    : Math.Clamp(_memory.GetValueOrDefault(node.Id).Number + (changed == "Decrement" ? -1 : 1), -1e12, 1e12);
                _memory[node.Id] = KiasGraphValue.Numeric(count); Output(node.Id, "Value", _memory[node.Id]); break;
            case KiasNodeKind.Edge:
                var next = Read("Value").Bool;
                var previous = _memory.GetValueOrDefault(node.Id).Bool;
                _memory[node.Id] = KiasGraphValue.Boolean(next);
                if (next != previous) Output(node.Id, next ? "Rising" : "Falling", KiasGraphValue.Pulse);
                break;
            case KiasNodeKind.NumberCompare:
                Bool(node.Config.Comparison switch
                {
                    KiasComparison.Equal => a.Number == b.Number, KiasComparison.NotEqual => a.Number != b.Number,
                    KiasComparison.Less => a.Number < b.Number, KiasComparison.LessEqual => a.Number <= b.Number,
                    KiasComparison.Greater => a.Number > b.Number, _ => a.Number >= b.Number
                }); break;
            case KiasNodeKind.BoolCompare: case KiasNodeKind.StringCompare: case KiasNodeKind.EnumCompare:
                var equal = node.Kind switch
                {
                    KiasNodeKind.BoolCompare => a.Bool == b.Bool,
                    KiasNodeKind.StringCompare => string.Equals(a.Text, b.Text, StringComparison.Ordinal),
                    _ => a.Enum == b.Enum
                };
                Bool(node.Config.Comparison == KiasComparison.NotEqual ? !equal : equal); break;
        }
    }
}
