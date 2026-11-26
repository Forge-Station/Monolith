using System.Linq;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Lathe;

public abstract partial class SharedFabricationSiloSystem
{
    /// <summary>
    /// Whether an item counts as a recipe part: the part itself, a recolour or child of it, or a stack of the same kind.
    /// </summary>
    public bool IsRecipePart(EntityUid item, EntProtoId part)
    {
        if (MetaData(item).EntityPrototype is not { } proto)
            return false;

        if (proto.ID == part.Id)
            return true;

        if (!_prototype.TryIndex(part, out var partProto))
            return false;

        // A single screw and a full stack are different prototypes of one stack type.
        if (_stackQuery.TryComp(item, out var stack) &&
            partProto.TryComp<StackComponent>(out var partStack, _factory))
            return stack.StackTypeId == partStack.StackTypeId;

        // A recolour, such as the lathe's green crowbar, shares the part's parents and name.
        if (proto.Name == partProto.Name && proto.Parents is { } parents && partProto.Parents is { } partParents &&
            parents.SequenceEqual(partParents))
            return true;

        foreach (var parent in _prototype.EnumerateParents<EntityPrototype>(proto.ID))
        {
            if (parent.ID == part.Id)
                return true;
        }

        return false;
    }
}
