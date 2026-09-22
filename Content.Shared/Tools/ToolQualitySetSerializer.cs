using System.Collections.Generic;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Generic;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;
using static Robust.Shared.Serialization.Manager.ISerializationManager;

namespace Content.Shared.Tools;

/// <summary>
///     Reads <see cref="HashSet{T}"/> of <see cref="ProtoId{T}"/> from either a scalar or a YAML sequence.
///     Legacy prototypes use <c>qualities: Welding</c> while newer ones use <c>qualities: [ Welding ]</c>.
/// </summary>
[TypeSerializer]
public sealed class ToolQualitySetSerializer :
    ITypeSerializer<HashSet<ProtoId<ToolQualityPrototype>>, ValueDataNode>,
    ITypeReader<HashSet<ProtoId<ToolQualityPrototype>>, SequenceDataNode>,
    ITypeCopyCreator<HashSet<ProtoId<ToolQualityPrototype>>>
{
    public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
        IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        return ProtoIdSerializer<ToolQualityPrototype>.Validate(dependencies, node);
    }

    public HashSet<ProtoId<ToolQualityPrototype>> Read(ISerializationManager serializationManager, ValueDataNode node,
        IDependencyCollection dependencies, SerializationHookContext hookCtx, ISerializationContext? context = null,
        InstantiationDelegate<HashSet<ProtoId<ToolQualityPrototype>>>? instanceProvider = null)
    {
        var set = instanceProvider?.Invoke() ?? new HashSet<ProtoId<ToolQualityPrototype>>();
        set.Add(serializationManager.Read<ProtoId<ToolQualityPrototype>, ValueDataNode, ProtoIdSerializer<ToolQualityPrototype>>(
            node, hookCtx, context));
        return set;
    }

    public ValidationNode Validate(ISerializationManager serializationManager, SequenceDataNode node,
        IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        var list = new List<ValidationNode>();
        foreach (var elem in node.Sequence)
        {
            list.Add(serializationManager.ValidateNode<ProtoId<ToolQualityPrototype>>(elem, context));
        }

        return new ValidatedSequenceNode(list);
    }

    public HashSet<ProtoId<ToolQualityPrototype>> Read(ISerializationManager serializationManager, SequenceDataNode node,
        IDependencyCollection dependencies, SerializationHookContext hookCtx, ISerializationContext? context = null,
        InstantiationDelegate<HashSet<ProtoId<ToolQualityPrototype>>>? instanceProvider = null)
    {
        var set = instanceProvider?.Invoke() ?? new HashSet<ProtoId<ToolQualityPrototype>>(node.Sequence.Count);

        foreach (var dataNode in node.Sequence)
        {
            set.Add(serializationManager.Read<ProtoId<ToolQualityPrototype>>(dataNode, hookCtx, context));
        }

        return set;
    }

    public DataNode Write(ISerializationManager serializationManager, HashSet<ProtoId<ToolQualityPrototype>> value,
        IDependencyCollection dependencies, bool alwaysWrite = false, ISerializationContext? context = null)
    {
        var sequence = new SequenceDataNode(value.Count);

        foreach (var elem in value)
        {
            sequence.Add(serializationManager.WriteValue(elem, alwaysWrite, context));
        }

        return sequence;
    }

    public HashSet<ProtoId<ToolQualityPrototype>> CreateCopy(ISerializationManager serializationManager,
        HashSet<ProtoId<ToolQualityPrototype>> source, IDependencyCollection dependencies, SerializationHookContext hookCtx,
        ISerializationContext? context = null)
    {
        return new HashSet<ProtoId<ToolQualityPrototype>>(source);
    }
}
