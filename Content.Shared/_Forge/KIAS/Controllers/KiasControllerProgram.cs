using Robust.Shared.Serialization;

namespace Content.Shared._Forge.KIAS.Controllers;

public enum KiasPortType : byte { Signal, Bool, Number, String, Entity, Enum }
public enum KiasPortDirection : byte { Input, Output }
public enum KiasEnumDomain : byte { Unspecified, AudioChannel, ContactDisposition, Alert, PowerChannel }
public enum KiasNodeKind : byte
{
    OnStart, BoolConstant, NumberConstant, StringConstant, EnumConstant,
    And, Or, Xor, Not, Nand, Nor, Xnor, If, Timer, Clock, Latch, Toggle, Counter, Edge,
    NumberCompare, BoolCompare, StringCompare, EnumCompare, Cooldown,
    Specific, Any, All, StringLatch
}
public enum KiasComparison : byte { Equal, NotEqual, Less, LessEqual, Greater, GreaterEqual }

[DataDefinition]
public sealed partial class KiasControllerProgram
{
    public const int CurrentVersion = 1;
    [DataField] public int Version = CurrentVersion;
    [DataField] public string Name = "Controller";
    [DataField] public List<KiasControllerNode> Nodes = new();
    [DataField] public List<KiasControllerWire> Wires = new();

    public KiasControllerProgram Copy() => new()
    {
        Version = Version, Name = Name,
        Nodes = Nodes.ConvertAll(node => node.Copy()),
        Wires = Wires.ConvertAll(wire => wire.Copy())
    };
}

[DataDefinition]
public sealed partial class KiasControllerNode
{
    [DataField] public int Id;
    [DataField] public KiasNodeKind Kind;
    [DataField] public float X;
    [DataField] public float Y;
    [DataField] public string Profile = string.Empty;
    [DataField] public EntityUid? Binding;
    [DataField] public string DeviceName = string.Empty;
    [DataField] public string Room = string.Empty;
    [DataField] public string Group = string.Empty;
    [DataField] public KiasNodeConfig Config = new();
    [DataField] public List<KiasGraphPort> PortSnapshot = new();

    public KiasControllerNode Copy() => new()
    {
        Id = Id, Kind = Kind, X = X, Y = Y, Profile = Profile, Binding = Binding,
        DeviceName = DeviceName, Room = Room, Group = Group, Config = Config.Copy(),
        PortSnapshot = PortSnapshot.ConvertAll(port => port.Copy())
    };
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class KiasNodeConfig
{
    [DataField] public bool Bool;
    [DataField] public double Number;
    [DataField] public string Text = string.Empty;
    [DataField] public int Enum;
    [DataField] public KiasEnumDomain EnumDomain;
    [DataField] public double Seconds = 1;
    [DataField] public KiasComparison Comparison;
    public KiasNodeConfig Copy() => new()
    {
        Bool = Bool, Number = Number, Text = Text, Enum = Enum, EnumDomain = EnumDomain, Seconds = Seconds, Comparison = Comparison
    };
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class KiasControllerWire
{
    [DataField] public int FromNode;
    [DataField] public string FromPort = string.Empty;
    [DataField] public int ToNode;
    [DataField] public string ToPort = string.Empty;
    public KiasControllerWire Copy() => new()
    {
        FromNode = FromNode, FromPort = FromPort, ToNode = ToNode, ToPort = ToPort
    };
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class KiasGraphPort
{
    [DataField] public string Id = string.Empty;
    [DataField] public KiasPortDirection Direction;
    [DataField] public KiasPortType Type;
    [DataField] public string Name = string.Empty;
    [DataField] public string Description = string.Empty;
    [DataField] public KiasEnumDomain EnumDomain;
    public KiasGraphPort Copy() => new()
    {
        Id = Id, Direction = Direction, Type = Type, Name = Name, Description = Description, EnumDomain = EnumDomain
    };
}

public readonly record struct KiasGraphEndpoint(int Node, string Port);

public readonly record struct KiasGraphValue(KiasPortType Type, bool Bool = false, double Number = 0,
    string Text = "", EntityUid? Entity = null, int Enum = 0)
{
    public static KiasGraphValue Pulse => new(KiasPortType.Signal);
    public static KiasGraphValue Boolean(bool value) => new(KiasPortType.Bool, Bool: value);
    public static KiasGraphValue Numeric(double value) => new(KiasPortType.Number, Number: value);
    public static KiasGraphValue String(string value) => new(KiasPortType.String, Text: value);
    public static KiasGraphValue Reference(EntityUid? value) => new(KiasPortType.Entity, Entity: value);
    public static KiasGraphValue Enumeration(int value) => new(KiasPortType.Enum, Enum: value);
}
