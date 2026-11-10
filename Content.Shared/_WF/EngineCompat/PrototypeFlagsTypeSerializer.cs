#pragma warning disable CS0618 // Serializes the engine's obsolete type, which still has fields to load.
using System.Linq;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Generic;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;
using Robust.Shared.Utility;

namespace Content.Shared._WF.EngineCompat;

/// <summary>
/// Reads <see cref="PrototypeFlags{T}"/> from a list or a single ID. Engine v289 kept the type but removed its serializer.
/// </summary>
[TypeSerializer]
public sealed class PrototypeFlagsTypeSerializer<T> :
    ITypeSerializer<PrototypeFlags<T>, SequenceDataNode>,
    ITypeSerializer<PrototypeFlags<T>, ValueDataNode>,
    ITypeCopyCreator<PrototypeFlags<T>>,
    ITypeCopier<PrototypeFlags<T>>
    where T : class, IPrototype
{
    public ValidationNode Validate(ISerializationManager serializationManager, SequenceDataNode node,
        IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        return PrototypeIdValidation.Sequence<T>(node, dependencies);
    }

    public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
        IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        return ProtoIdSerializer<T>.Validate(dependencies, node);
    }

    public PrototypeFlags<T> Read(ISerializationManager serializationManager, SequenceDataNode node,
        IDependencyCollection dependencies, SerializationHookContext hookCtx, ISerializationContext? context = null,
        ISerializationManager.InstantiationDelegate<PrototypeFlags<T>>? instanceProvider = null)
    {
        return new PrototypeFlags<T>(node.Sequence.OfType<ValueDataNode>().Select(value => value.Value));
    }

    public PrototypeFlags<T> Read(ISerializationManager serializationManager, ValueDataNode node,
        IDependencyCollection dependencies, SerializationHookContext hookCtx, ISerializationContext? context = null,
        ISerializationManager.InstantiationDelegate<PrototypeFlags<T>>? instanceProvider = null)
    {
        return new PrototypeFlags<T>(node.Value);
    }

    public DataNode Write(ISerializationManager serializationManager, PrototypeFlags<T> value,
        IDependencyCollection dependencies, bool alwaysWrite = false, ISerializationContext? context = null)
    {
        return new SequenceDataNode(value.ToArray());
    }

    public PrototypeFlags<T> CreateCopy(ISerializationManager serializationManager, PrototypeFlags<T> source,
        IDependencyCollection dependencies, SerializationHookContext hookCtx, ISerializationContext? context = null)
    {
        return new PrototypeFlags<T>(source);
    }

    public void CopyTo(ISerializationManager serializationManager, PrototypeFlags<T> source, ref PrototypeFlags<T> target,
        IDependencyCollection dependencies, SerializationHookContext hookCtx, ISerializationContext? context = null)
    {
        target = new PrototypeFlags<T>(source);
    }
}
