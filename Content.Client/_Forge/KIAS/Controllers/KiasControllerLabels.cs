using System.Linq;
using Content.Shared._Forge.KIAS.Controllers;
using System.Globalization;

namespace Content.Client._Forge.KIAS.Controllers;

public static class KiasControllerLabels
{
    public static string Type(KiasGraphPort port) => port.Type == KiasPortType.Enum && port.EnumDomain != KiasEnumDomain.Unspecified
        ? Loc.GetString($"kias-controller-domain-{port.EnumDomain.ToString().ToLowerInvariant()}")
        : Loc.GetString($"kias-controller-type-{port.Type.ToString().ToLowerInvariant()}");
    public static string Description(KiasGraphPort port) => Loc.TryGetString(port.Description, out var help) ? help : port.Description;
    public static string PortTitle(KiasGraphPort port) => $"{Loc.GetString($"kias-controller-direction-{port.Direction.ToString().ToLowerInvariant()}")} · {Type(port)} — {Port(port)}";
    public static string PortHelp(KiasGraphPort port) => PortTitle(port) + "\n" + Description(port);
    public static string EnumValue(KiasEnumDomain domain, int value) => Loc.TryGetString($"kias-controller-enum-{domain.ToString().ToLowerInvariant()}-{value}", out var label)
        ? label : Loc.GetString("kias-controller-enum-unselected");
    public static string Summary(KiasGraphNodeView node)
    {
        var config = node.Config;
        return node.Kind switch
        {
            KiasNodeKind.StringConstant or KiasNodeKind.StringLatch => $"“{config.Text.Replace('\n', ' ')}”",
            KiasNodeKind.NumberConstant or KiasNodeKind.Counter => config.Number.ToString("G", CultureInfo.InvariantCulture),
            KiasNodeKind.BoolConstant or KiasNodeKind.Latch or KiasNodeKind.Toggle => Loc.GetString(config.Bool ? "kias-controller-yes" : "kias-controller-no"),
            KiasNodeKind.Timer or KiasNodeKind.Clock or KiasNodeKind.Cooldown => Loc.GetString("kias-controller-duration", ("seconds", config.Seconds)),
            KiasNodeKind.EnumConstant => EnumValue(node.Ports.FirstOrDefault()?.EnumDomain ?? config.EnumDomain, config.Enum),
            KiasNodeKind.NumberCompare or KiasNodeKind.BoolCompare or KiasNodeKind.StringCompare or KiasNodeKind.EnumCompare => config.Comparison switch
            {
                KiasComparison.Equal => "=", KiasComparison.NotEqual => "≠", KiasComparison.Less => "<",
                KiasComparison.LessEqual => "≤", KiasComparison.Greater => ">", _ => "≥"
            },
            KiasNodeKind.Specific => node.DeviceName,
            KiasNodeKind.Any or KiasNodeKind.All => Loc.GetString("kias-controller-match-summary", ("count", node.Matched))
                + (node.Room.Length > 0 ? " · " + Loc.GetString("kias-controller-filter-room-value", ("value", node.Room)) : "")
                + (node.Group.Length > 0 ? " · " + Loc.GetString("kias-controller-filter-group-value", ("value", node.Group)) : ""),
            _ => $"#{node.Id}"
        };
    }
    public static string? Incompatibility(KiasGraphPort first, KiasGraphPort second)
    {
        if (first.Direction == second.Direction) return Loc.GetString("kias-controller-wire-direction");
        if (first.Type != second.Type)
            return Loc.GetString("kias-controller-wire-type", ("first", Type(first)), ("second", Type(second)))
                + (first.Type is KiasPortType.Signal or KiasPortType.Bool && second.Type is KiasPortType.Signal or KiasPortType.Bool
                    ? " " + Loc.GetString("kias-controller-wire-conversion") : "");
        if (first.Type == KiasPortType.Enum && first.EnumDomain != KiasEnumDomain.Unspecified
            && second.EnumDomain != KiasEnumDomain.Unspecified && first.EnumDomain != second.EnumDomain)
            return Loc.GetString("kias-controller-error-enum-domain");
        return null;
    }
    public static string Port(KiasGraphPort port) => Loc.TryGetString(port.Name, out var name) ? name : port.Id;
    public static string Profile(string id, IEnumerable<KiasGraphPort> ports)
    {
        if (Loc.TryGetString($"kias-controller-profile-{id.ToLowerInvariant()}", out var name)) return name;
        return Loc.GetString("kias-controller-native-profile") + ": "
            + string.Join(" / ", ports.Where(port => !port.Id.StartsWith('$')).Take(2).Select(Port));
    }
    public static string Error(string error)
    {
        var parts = error.Split(':', 2);
        if (!Loc.TryGetString($"kias-controller-error-{parts[0]}", out var description)) return error;
        return parts.Length > 1 ? $"{description} ({parts[1]})" : description;
    }
}
