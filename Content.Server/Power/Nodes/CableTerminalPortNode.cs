using Content.Server.NodeContainer;
using Content.Server.NodeContainer.Nodes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.Power.Nodes
{
    [DataDefinition]
    public sealed partial class CableTerminalPortNode : Node
    {
        public override IEnumerable<Node> GetReachableNodes(TransformComponent xform,
            EntityQuery<NodeContainerComponent> nodeQuery,
            EntityQuery<TransformComponent> xformQuery,
            MapGridComponent? grid,
            IEntityManager entMan)
        {
            if (!xform.Anchored || grid == null || !xform.GridUid.HasValue)
                yield break;

            var gridUid = xform.GridUid.Value;
            var map = entMan.System<SharedMapSystem>();
            var gridIndex = map.TileIndicesFor(gridUid, grid, xform.Coordinates);

            var nodes = NodeHelpers.GetCardinalNeighborNodes(nodeQuery, gridUid, grid, gridIndex, map, includeSameTile: false);
            foreach (var (_, node) in nodes)
            {
                if (node is CableTerminalNode)
                    yield return node;
            }
        }
    }
}
