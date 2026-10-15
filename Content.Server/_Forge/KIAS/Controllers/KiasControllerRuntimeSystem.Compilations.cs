using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Content.Shared._Forge.KIAS.Controllers;

namespace Content.Server._Forge.KIAS.Controllers;

public sealed partial class KiasControllerRuntimeSystem
{
    private readonly Dictionary<string, KiasCompiledGraph> _compiled = new();
    private readonly Queue<string> _compiledOrder = new();

    private KiasGraphCompilation Compile(KiasControllerProgram program)
    {
        var key = Fingerprint(program);
        if (key != null && _compiled.TryGetValue(key, out var graph)) return new() { Graph = graph };
        var result = KiasGraphCompiler.Compile(program, _io.Schema, _io.SnapshotSchema);
        if (key == null || !result.Success) return result;
        if (_compiled.Count >= 128) _compiled.Remove(_compiledOrder.Dequeue());
        // Схема общая и неизменяемая; память и таймеры принадлежат отдельной машине карточки.
        _compiled[key] = result.Graph!;
        _compiledOrder.Enqueue(key);
        return result;
    }

    private string? Fingerprint(KiasControllerProgram program)
    {
        if (program.Nodes.Count > 128 || program.Wires.Count > 256 || program.Name.Length > 64
            || program.Nodes.Any(node => node.Config.Text.Length > 256 || node.Profile.Length > 64
                || node.Room.Length > 64 || node.Group.Length > 32 || node.DeviceName.Length > 256 || node.PortSnapshot.Count > 64)
            || program.Wires.Any(wire => wire.FromPort.Length > 64 || wire.ToPort.Length > 64)) return null;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        void Ports(IReadOnlyList<KiasGraphPort> ports)
        {
            writer.Write(ports.Count);
            foreach (var port in ports)
            {
                writer.Write(port.Id); writer.Write((byte) port.Direction); writer.Write((byte) port.Type);
                writer.Write(port.Name); writer.Write(port.Description);
                writer.Write((byte) port.EnumDomain);
            }
        }
        writer.Write(program.Version); writer.Write(program.Name); writer.Write(program.Nodes.Count);
        foreach (var node in program.Nodes)
        {
            writer.Write(node.Id); writer.Write((byte) node.Kind); writer.Write(node.X); writer.Write(node.Y);
            writer.Write(node.Profile); writer.Write(node.Binding.HasValue); writer.Write(node.Binding?.Id ?? 0);
            writer.Write(node.DeviceName); writer.Write(node.Room); writer.Write(node.Group);
            writer.Write(node.Config.Bool); writer.Write(node.Config.Number); writer.Write(node.Config.Text);
            writer.Write(node.Config.Enum); writer.Write(node.Config.Seconds); writer.Write((byte) node.Config.Comparison);
            writer.Write((byte) node.Config.EnumDomain);
            Ports(node.PortSnapshot);
            if (KiasGraphCatalog.External(node.Kind))
            {
                var schema = _io.Schema(node.Profile) ?? _io.SnapshotSchema(node);
                writer.Write(schema != null);
                Ports(schema ?? Array.Empty<KiasGraphPort>());
            }
        }
        writer.Write(program.Wires.Count);
        foreach (var wire in program.Wires)
        {
            writer.Write(wire.FromNode); writer.Write(wire.FromPort); writer.Write(wire.ToNode); writer.Write(wire.ToPort);
        }
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, (int) stream.Length)));
    }
}
