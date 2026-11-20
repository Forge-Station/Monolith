using Content.Server.NodeContainer.Nodes;
using Content.Shared._WF.Tether;
using Content.Shared._WF.Tether.PowerCord;
using Content.Shared.NodeContainer;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Tether.PowerCord;

/// <summary>
/// A clamp's tap into a power net. It reaches everything of its own voltage anchored on its tile
/// — bare cable, a generator's output, an SMES or substation terminal, an APC — and, while a cord
/// is tied and the far clamp names it back, that clamp's node as well.
/// </summary>
/// <remarks>
/// Stock cable nodes never return this node, but NodeGroupSystem makes every edge undirected at
/// flood time, so one-sided reachability is enough as long as this node is the one being
/// reflooded. <c>PowerCordSystem</c> queues that reflood on every link change.
/// </remarks>
[DataDefinition]
public sealed partial class PowerCordNode : Node
{
    public override IEnumerable<Node> GetReachableNodes(
        Entity<TransformComponent> xform,
        EntityQuery<NodeContainerComponent> nodeQuery,
        EntityQuery<TransformComponent> xformQuery,
        Entity<MapGridComponent>? grid,
        IEntityManager entMan)
    {
        if (!xform.Comp.Anchored || grid is not { } gridEnt)
            yield break;

        var maps = entMan.System<SharedMapSystem>();
        var tile = maps.TileIndicesFor(gridEnt, xform.Comp.Coordinates);
        foreach (var node in NodeHelpers.GetNodesInTile(nodeQuery, gridEnt, tile, maps))
        {
            // The voltage filter is applied by GetCompatibleNodes, which drops mismatched groups.
            if (node != this)
                yield return node;
        }

        if (TryGetPartnerNode(xform, nodeQuery, xformQuery, entMan, out var partner))
            yield return partner;
    }

    /// <summary>
    /// The far clamp's node, but only while the cord still exists, both clamps are anchored on the
    /// same map and the far clamp names this one back. Anything less is not conductive.
    /// </summary>
    private bool TryGetPartnerNode(
        Entity<TransformComponent> xform,
        EntityQuery<NodeContainerComponent> nodeQuery,
        EntityQuery<TransformComponent> xformQuery,
        IEntityManager entMan,
        out Node partner)
    {
        partner = default!;
        if (!entMan.TryGetComponent(Owner, out PowerCordClampComponent? clamp) ||
            clamp.Partner is not { } partnerNet || clamp.Cord is not { } cordNet)
            return false;

        if (!entMan.TryGetEntity(cordNet, out var cord) || !entMan.HasComponent<RopeComponent>(cord))
            return false;

        if (!entMan.TryGetEntity(partnerNet, out var other) || other == Owner ||
            !entMan.TryGetComponent(other, out PowerCordClampComponent? otherClamp) ||
            otherClamp.Voltage != clamp.Voltage ||
            otherClamp.Partner != entMan.GetNetEntity(Owner))
            return false;

        // A cord never bridges two maps: the far hull may have jumped away this tick.
        if (!xformQuery.TryGetComponent(other, out var otherXform) || !otherXform.Anchored ||
            otherXform.MapID == MapId.Nullspace || otherXform.MapID != xform.Comp.MapID)
            return false;

        if (!nodeQuery.TryGetComponent(other, out var container) ||
            !container.Nodes.TryGetValue(otherClamp.NodeName, out var node) ||
            node is not PowerCordNode found || found == this)
            return false;

        partner = found;
        return true;
    }
}
