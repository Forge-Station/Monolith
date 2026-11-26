using Content.Server.Construction.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Construction.Steps;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

public sealed partial class WFDrydockSystem
{
    [Dependency] private TagSystem _tag = default!;

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";

    /// <summary>Construction materials by entity prototype, in material units.</summary>
    private readonly Dictionary<string, Dictionary<string, int>> _constructionMaterials = new();

    /// <summary>
    /// Materials to build an entity by hand: every material step on its construction graph from the start node to its
    /// node, plus a machine's board sheets. Empty without a graph.
    /// </summary>
    private Dictionary<string, int> ConstructionMaterials(EntityPrototype proto)
    {
        if (_constructionMaterials.TryGetValue(proto.ID, out var cached))
            return cached;

        var materials = new Dictionary<string, int>();
        _constructionMaterials[proto.ID] = materials;
        if (!proto.TryComp<ConstructionComponent>(out var construction, Factory) ||
            !_prototype.TryIndex<ConstructionGraphPrototype>(construction.Graph, out var graph) ||
            graph.Start is not { } start ||
            !graph.TryPath(start, construction.Node, out var path))
        {
            return materials;
        }

        var current = graph.Nodes[start];
        foreach (var node in path)
        {
            var edge = current.GetEdge(node.Name);
            current = node;
            if (edge == null)
                continue;

            foreach (var step in edge.Steps)
            {
                if (step is MaterialConstructionGraphStep materialStep)
                    AddSheets(materials, materialStep.MaterialPrototypeId, materialStep.Amount);
            }
        }

        if (proto.TryComp<MachineComponent>(out var machine, Factory) && machine.Board is { } board &&
            _prototype.Index(board).TryComp<MachineBoardComponent>(out var boardComp, Factory))
        {
            // Cable comes off whole with the parts, so only sheets count.
            foreach (var (stack, amount) in boardComp.StackRequirements)
            {
                if (_prototype.TryIndex(stack, out var stackProto) &&
                    _prototype.Index(stackProto.Spawn).TryComp<MaterialComponent>(out _, Factory))
                    AddSheets(materials, stack.Id, amount);
            }
        }

        return materials;
    }

    /// <summary>Adds the materials of a number of sheets of a stack.</summary>
    private void AddSheets(Dictionary<string, int> materials, string stackId, int count)
    {
        if (!_prototype.TryIndex<StackPrototype>(stackId, out var stack) ||
            !_prototype.TryIndex(stack.Spawn, out var sheet) ||
            !sheet.TryComp<PhysicalCompositionComponent>(out var composition, Factory))
        {
            return;
        }

        foreach (var (material, amount) in composition.MaterialComposition)
        {
            materials[material] = materials.GetValueOrDefault(material) + amount * count;
        }
    }

    /// <summary>Whether a prototype is a wall.</summary>
    private bool IsWall(EntityPrototype proto)
    {
        return proto.TryComp<TagComponent>(out var tags, Factory) && _tag.HasTag(tags, WallTag);
    }

    /// <summary>Market value of a set of materials, from each material's price per unit.</summary>
    private double MaterialValue(Dictionary<string, int> materials)
    {
        var value = 0d;
        foreach (var (id, amount) in materials)
        {
            if (_prototype.TryIndex<MaterialPrototype>(id, out var material))
                value += material.Price * amount;
        }

        return value;
    }
}
