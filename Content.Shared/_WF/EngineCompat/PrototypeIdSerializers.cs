using System.Collections.Immutable;
using System.Linq;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Generic;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.Shared._WF.EngineCompat;

// Engine v289 removed these in favour of ProtoId<T> fields. They keep validating the string prototype ID fields
// upstream content still declares, so those fields keep their type until upstream converts them.

/// <summary>
/// Validates a string field as an ID of <typeparamref name="TPrototype"/>.
/// </summary>
public sealed class PrototypeIdSerializer<TPrototype> : ITypeValidator<string, ValueDataNode>
    where TPrototype : class, IPrototype
{
    public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
        IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        return ProtoIdSerializer<TPrototype>.Validate(dependencies, node);
    }
}

/// <summary>
/// Validates a list of string IDs of <typeparamref name="TPrototype"/>.
/// </summary>
public sealed class PrototypeIdListSerializer<TPrototype> :
    ITypeValidator<List<string>, SequenceDataNode>,
    ITypeValidator<ImmutableList<string>, SequenceDataNode>,
    ITypeValidator<IReadOnlyList<string>, SequenceDataNode>,
    ITypeValidator<IReadOnlyCollection<string>, SequenceDataNode>
    where TPrototype : class, IPrototype
{
    public ValidationNode Validate(ISerializationManager serializationManager, SequenceDataNode node,
        IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        return PrototypeIdValidation.Sequence<TPrototype>(node, dependencies);
    }
}

/// <summary>
/// Validates a set of string IDs of <typeparamref name="TPrototype"/>.
/// </summary>
public sealed class PrototypeIdHashSetSerializer<TPrototype> :
    ITypeValidator<HashSet<string>, SequenceDataNode>,
    ITypeValidator<ImmutableHashSet<string>, SequenceDataNode>,
    ITypeValidator<ISet<string>, SequenceDataNode>,
    ITypeValidator<IReadOnlySet<string>, SequenceDataNode>
    where TPrototype : class, IPrototype
{
    public ValidationNode Validate(ISerializationManager serializationManager, SequenceDataNode node,
        IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        return PrototypeIdValidation.Sequence<TPrototype>(node, dependencies);
    }
}

/// <summary>
/// Validates a string array of IDs of <typeparamref name="TPrototype"/>, or a single ID.
/// </summary>
public sealed class PrototypeIdArraySerializer<TPrototype> :
    ITypeValidator<string[], SequenceDataNode>,
    ITypeValidator<string[], ValueDataNode>
    where TPrototype : class, IPrototype
{
    public ValidationNode Validate(ISerializationManager serializationManager, SequenceDataNode node,
        IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        return PrototypeIdValidation.Sequence<TPrototype>(node, dependencies);
    }

    public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
        IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        return ProtoIdSerializer<TPrototype>.Validate(dependencies, node);
    }
}

/// <summary>
/// Validates a dictionary whose string keys are IDs of <typeparamref name="TPrototype"/>.
/// </summary>
public sealed class PrototypeIdDictionarySerializer<TValue, TPrototype> :
    ITypeValidator<Dictionary<string, TValue>, MappingDataNode>,
    ITypeValidator<SortedDictionary<string, TValue>, MappingDataNode>,
    ITypeValidator<IReadOnlyDictionary<string, TValue>, MappingDataNode>
    where TPrototype : class, IPrototype
{
    public ValidationNode Validate(ISerializationManager serializationManager, MappingDataNode node,
        IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        var mapping = new Dictionary<ValidationNode, ValidationNode>();
        foreach (var (key, val) in node.Children)
        {
            mapping.Add(ProtoIdSerializer<TPrototype>.Validate(dependencies, new ValueDataNode(key)),
                serializationManager.ValidateNode<TValue>(val, context));
        }

        return new ValidatedMappingNode(mapping);
    }
}

/// <summary>
/// Validates a dictionary whose string values are IDs of <typeparamref name="TPrototype"/>.
/// </summary>
public sealed class PrototypeIdValueDictionarySerializer<TKey, TPrototype> :
    ITypeValidator<Dictionary<TKey, string>, MappingDataNode>,
    ITypeValidator<SortedDictionary<TKey, string>, MappingDataNode>,
    ITypeValidator<IReadOnlyDictionary<TKey, string>, MappingDataNode>
    where TPrototype : class, IPrototype
    where TKey : notnull
{
    public ValidationNode Validate(ISerializationManager serializationManager, MappingDataNode node,
        IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        var mapping = new Dictionary<ValidationNode, ValidationNode>();
        foreach (var (k, val) in node.Children)
        {
            var key = node.GetKeyNode(k);
            var value = val is ValueDataNode valueNode
                ? ProtoIdSerializer<TPrototype>.Validate(dependencies, valueNode)
                : new ErrorNode(val, $"Cannot cast node {val} to ValueDataNode.");
            mapping.Add(value, serializationManager.ValidateNode<TKey>(key, context));
        }

        return new ValidatedMappingNode(mapping);
    }
}

internal static class PrototypeIdValidation
{
    public static ValidationNode Sequence<TPrototype>(SequenceDataNode node, IDependencyCollection dependencies)
        where TPrototype : class, IPrototype
    {
        return new ValidatedSequenceNode(node.Sequence
            .Select(x => x is ValueDataNode value
                ? ProtoIdSerializer<TPrototype>.Validate(dependencies, value)
                : new ErrorNode(x, $"Cannot cast node {x} to ValueDataNode."))
            .ToList());
    }
}
