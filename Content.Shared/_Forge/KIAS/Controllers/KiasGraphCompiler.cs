using System.Linq;

namespace Content.Shared._Forge.KIAS.Controllers;

public sealed class KiasCompiledGraph
{
    public readonly Dictionary<int, KiasControllerNode> Nodes = new();
    public readonly Dictionary<KiasGraphEndpoint, KiasGraphPort> Ports = new();
    public readonly Dictionary<KiasGraphEndpoint, List<KiasGraphEndpoint>> Outgoing = new();
    public readonly Dictionary<int, List<string>> DataInputs = new();
}

public sealed class KiasGraphCompilation
{
    public readonly List<string> Errors = new();
    public KiasCompiledGraph? Graph;
    public bool Success => Graph != null && Errors.Count == 0;
}

public static class KiasGraphCompiler
{
    public const int MaxNodes = 128;
    public const int MaxWires = 256;
    public const int MaxExternal = 32;
    public const int MaxFanOut = 32;
    public const double MinSeconds = 0.1;
    public const double MaxSeconds = 600;

    public static KiasGraphCompilation Compile(KiasControllerProgram program,
        Func<string, IReadOnlyList<KiasGraphPort>?> profiles,
        Func<KiasControllerNode, IReadOnlyList<KiasGraphPort>?>? snapshots = null)
    {
        var result = new KiasGraphCompilation();
        void Error(string reason) { if (result.Errors.Count < 32) result.Errors.Add(reason); }
        if (program.Version != KiasControllerProgram.CurrentVersion) Error("version");
        if (program.Name.Length is < 1 or > 64) Error("name");
        if (program.Nodes.Count > MaxNodes || program.Wires.Count > MaxWires
            || program.Nodes.Count(node => KiasGraphCatalog.External(node.Kind)) > MaxExternal)
        {
            Error("limits");
            return result;
        }
        var graph = new KiasCompiledGraph();
        foreach (var node in program.Nodes)
        {
            if (node.Id < 1 || !Enum.IsDefined(node.Kind) || graph.Nodes.ContainsKey(node.Id))
            {
                Error($"node:{node.Id}");
                continue;
            }
            if (!float.IsFinite(node.X) || !float.IsFinite(node.Y) || Math.Abs(node.X) > 100000 || Math.Abs(node.Y) > 100000
                || !double.IsFinite(node.Config.Number) || Math.Abs(node.Config.Number) > 1e12
                || !double.IsFinite(node.Config.Seconds) || node.Config.Seconds < MinSeconds || node.Config.Seconds > MaxSeconds
                || node.Config.Text.Length > 256 || !Enum.IsDefined(node.Config.Comparison)
                || node.DeviceName.Length > 256 || node.Room.Length > 64 || node.Group.Length > 32 || node.Profile.Length > 64
                || node.PortSnapshot.Count > 64 || !Enum.IsDefined(node.Config.EnumDomain))
            {
                Error($"config:{node.Id}");
                continue;
            }
            graph.Nodes.Add(node.Id, node.Copy());
            if (node.Kind is KiasNodeKind.BoolCompare or KiasNodeKind.StringCompare or KiasNodeKind.EnumCompare
                && node.Config.Comparison is not (KiasComparison.Equal or KiasComparison.NotEqual)) Error($"comparison:{node.Id}");
            List<KiasGraphPort> ports;
            if (KiasGraphCatalog.External(node.Kind))
            {
                var profile = profiles(node.Profile) ?? snapshots?.Invoke(node);
                if (profile == null || profile.Count > 60)
                {
                    Error($"profile:{node.Id}");
                    continue;
                }
                ports = KiasGraphCatalog.DevicePorts(profile);
            }
            else ports = KiasGraphCatalog.InternalPorts(node.Kind, node.Config.EnumDomain);
            foreach (var port in ports)
            {
                if (port.Direction == KiasPortDirection.Input && port.Type != KiasPortType.Signal)
                {
                    if (!graph.DataInputs.TryGetValue(node.Id, out var inputs)) graph.DataInputs[node.Id] = inputs = new();
                    inputs.Add(port.Id);
                }
                if (port.Id.Length is < 1 or > 64 || !Enum.IsDefined(port.Type) || !Enum.IsDefined(port.Direction) || !Enum.IsDefined(port.EnumDomain)
                    || !graph.Ports.TryAdd(new(node.Id, port.Id), port)) Error($"schema:{node.Id}");
            }
        }
        var enumLinks = new Dictionary<KiasGraphEndpoint, List<KiasGraphEndpoint>>();
        void EnumLink(KiasGraphEndpoint a, KiasGraphEndpoint b)
        {
            if (!enumLinks.TryGetValue(a, out var links)) enumLinks[a] = links = new();
            links.Add(b);
        }
        foreach (var node in graph.Nodes.Values.Where(node => node.Kind == KiasNodeKind.EnumCompare))
        {
            EnumLink(new(node.Id, "A"), new(node.Id, "B"));
            EnumLink(new(node.Id, "B"), new(node.Id, "A"));
        }
        var sources = new HashSet<KiasGraphEndpoint>();
        var duplicates = new HashSet<(KiasGraphEndpoint, KiasGraphEndpoint)>();
        var dependency = graph.Nodes.Keys.ToDictionary(id => id, _ => new List<int>());
        foreach (var wire in program.Wires)
        {
            var from = new KiasGraphEndpoint(wire.FromNode, wire.FromPort);
            var to = new KiasGraphEndpoint(wire.ToNode, wire.ToPort);
            if (!graph.Ports.TryGetValue(from, out var output) || !graph.Ports.TryGetValue(to, out var input))
            {
                Error("endpoint");
                continue;
            }
            if (output.Direction != KiasPortDirection.Output || input.Direction != KiasPortDirection.Input
                || output.Type != input.Type || !duplicates.Add((from, to))) { Error("wire"); continue; }
            if (output.Type == KiasPortType.Enum) { EnumLink(from, to); EnumLink(to, from); }
            if (input.Type != KiasPortType.Signal && !sources.Add(to)) Error($"multiple-source:{to.Node}:{to.Port}");
            if (!graph.Outgoing.TryGetValue(from, out var targets)) graph.Outgoing[from] = targets = new();
            targets.Add(to);
            if (targets.Count > MaxFanOut) Error("fan-out");
            if (!KiasGraphCatalog.BreaksCycle(graph.Nodes[from.Node].Kind)) dependency[from.Node].Add(to.Node);
        }
        var enumVisited = new HashSet<KiasGraphEndpoint>();
        foreach (var endpoint in graph.Ports.Where(pair => pair.Value.Type == KiasPortType.Enum).Select(pair => pair.Key))
        {
            if (!enumVisited.Add(endpoint)) continue;
            var component = new List<KiasGraphEndpoint>();
            var queue = new Queue<KiasGraphEndpoint>(); queue.Enqueue(endpoint);
            while (queue.TryDequeue(out var next))
            {
                component.Add(next);
                if (enumLinks.TryGetValue(next, out var links))
                    foreach (var link in links) if (enumVisited.Add(link)) queue.Enqueue(link);
            }
            var domains = component.Select(point => graph.Ports[point].EnumDomain).Where(domain => domain != KiasEnumDomain.Unspecified).Distinct().ToArray();
            if (domains.Length > 1) { Error("enum-domain"); continue; }
            if (domains.Length == 1)
                foreach (var point in component) graph.Ports[point].EnumDomain = domains[0];
            foreach (var point in component)
                if (graph.Nodes[point.Node].Kind == KiasNodeKind.EnumConstant && domains.Length == 1
                    && graph.Nodes[point.Node].Config.Enum is < 0 or > 3) Error($"config:{point.Node}");
        }
        var visited = new Dictionary<int, byte>();
        bool Cycle(int id)
        {
            if (visited.TryGetValue(id, out var mark)) return mark == 1;
            visited[id] = 1;
            foreach (var next in dependency[id]) if (Cycle(next)) return true;
            visited[id] = 2;
            return false;
        }
        foreach (var id in graph.Nodes.Keys) if (Cycle(id)) { Error("combinational-cycle"); break; }
        if (result.Errors.Count == 0) result.Graph = graph;
        return result;
    }
}
