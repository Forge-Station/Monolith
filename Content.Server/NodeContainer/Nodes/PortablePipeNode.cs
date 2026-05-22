using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.NodeContainer.Nodes
{
    [DataDefinition]
    public sealed partial class PortablePipeNode : PipeNode
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

            foreach (var node in NodeHelpers.GetNodesInTile(nodeQuery, gridUid, grid, gridIndex, map))
            {
                if (node is PortPipeNode)
                    yield return node;
            }

            foreach (var node in base.GetReachableNodes(xform, nodeQuery, xformQuery, grid, entMan))
            {
                yield return node;
            }
        }
    }
}
