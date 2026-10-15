using System.Linq;

namespace Content.Shared._Forge.KIAS.Controllers;

public static class KiasGraphCatalog
{
    public static bool External(KiasNodeKind kind) => kind is KiasNodeKind.Specific or KiasNodeKind.Any or KiasNodeKind.All;
    public static bool BreaksCycle(KiasNodeKind kind) => kind is KiasNodeKind.Timer or KiasNodeKind.Clock
        or KiasNodeKind.Latch or KiasNodeKind.Toggle or KiasNodeKind.Counter or KiasNodeKind.Edge or KiasNodeKind.Cooldown or KiasNodeKind.StringLatch;

    public static KiasGraphPort Port(string id, KiasPortType type, bool output = false) => new()
    {
        Id = id, Type = type, Direction = output ? KiasPortDirection.Output : KiasPortDirection.Input,
        Name = $"kias-controller-port-{id.TrimStart('$').ToLowerInvariant()}", Description = $"kias-controller-help-meta-{id.TrimStart('$').ToLowerInvariant()}"
    };

    public static List<KiasGraphPort> InternalPorts(KiasNodeKind kind, KiasEnumDomain domain = KiasEnumDomain.Unspecified)
    {
        KiasGraphPort Describe(string id, KiasPortType type, bool output)
        {
            var port = Port(id, type, output);
            port.Description = $"kias-controller-help-{kind.ToString().ToLowerInvariant()}-{id.ToLowerInvariant()}";
            if (type == KiasPortType.Enum) port.EnumDomain = domain;
            return port;
        }
        KiasGraphPort I(string id, KiasPortType type) => Describe(id, type, false);
        KiasGraphPort O(string id, KiasPortType type) => Describe(id, type, true);
        var signal = KiasPortType.Signal;
        var boolean = KiasPortType.Bool;
        var number = KiasPortType.Number;
        switch (kind)
        {
            case KiasNodeKind.OnStart: return new() { O("Started", signal) };
            case KiasNodeKind.BoolConstant: return new() { O("Value", boolean) };
            case KiasNodeKind.NumberConstant: return new() { O("Value", number) };
            case KiasNodeKind.StringConstant: return new() { O("Value", KiasPortType.String) };
            case KiasNodeKind.EnumConstant: return new() { O("Value", KiasPortType.Enum) };
            case KiasNodeKind.Not: return new() { I("A", boolean), O("Value", boolean) };
            case KiasNodeKind.And: case KiasNodeKind.Or: case KiasNodeKind.Xor:
            case KiasNodeKind.Nand: case KiasNodeKind.Nor: case KiasNodeKind.Xnor:
                return new() { I("A", boolean), I("B", boolean), O("Value", boolean) };
            case KiasNodeKind.If:
                return new() { I("Trigger", signal), I("Condition", boolean), O("True", signal), O("False", signal) };
            case KiasNodeKind.Timer:
                return new() { I("Trigger", signal), I("Cancel", signal), O("Elapsed", signal) };
            case KiasNodeKind.Cooldown:
                return new() { I("Trigger", signal), I("Reset", signal), I("Key", KiasPortType.String), O("Ready", signal) };
            case KiasNodeKind.Clock:
                return new() { I("Enabled", boolean), I("Reset", signal), O("Tick", signal) };
            case KiasNodeKind.Latch:
                return new() { I("Set", signal), I("Reset", signal), O("Value", boolean) };
            case KiasNodeKind.StringLatch:
                return new() { I("Value", KiasPortType.String), I("Store", signal), I("Reset", signal), O("Stored", KiasPortType.String), O("Saved", signal) };
            case KiasNodeKind.Toggle:
                return new() { I("Trigger", signal), I("Reset", signal), O("Value", boolean) };
            case KiasNodeKind.Counter:
                return new() { I("Increment", signal), I("Decrement", signal), I("Reset", signal), O("Value", number) };
            case KiasNodeKind.Edge:
                return new() { I("Value", boolean), O("Rising", signal), O("Falling", signal) };
            case KiasNodeKind.NumberCompare: case KiasNodeKind.BoolCompare:
            case KiasNodeKind.StringCompare: case KiasNodeKind.EnumCompare:
                var type = kind switch
                {
                    KiasNodeKind.NumberCompare => number, KiasNodeKind.BoolCompare => boolean,
                    KiasNodeKind.StringCompare => KiasPortType.String, _ => KiasPortType.Enum
                };
                return new() { I("A", type), I("B", type), O("Value", boolean) };
            default: return new();
        }
    }

    public static List<KiasGraphPort> DevicePorts(IEnumerable<KiasGraphPort> profile)
    {
        var result = profile.Select(port => port.Copy()).ToList();
        foreach (var port in new[] { Port("$MatchedCount", KiasPortType.Number, true),
                     Port("$OnlineCount", KiasPortType.Number, true), Port("$HasAny", KiasPortType.Bool, true),
                     Port("$Source", KiasPortType.Entity, true) })
            result.Add(port);
        return result;
    }
}
