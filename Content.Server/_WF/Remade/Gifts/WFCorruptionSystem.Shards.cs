using System.Linq;
using Content.Server._WF.Remade.Counterplay;
using Content.Server._WF.Remade.Gifts;
using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Remade.Structures;
using Content.Shared._WF.Remade;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Remade.Corruption;

/// <summary>Shards as small nodes: a rooted shard feeds a handful of tiles for its Scion, however far from it the shard is carried.</summary>
public sealed partial class WFCorruptionSystem
{
    /// <summary>Whether a node is a rooted shard whose Scion has reached Growth.</summary>
    private bool ShardFeeds(EntityUid node)
    {
        return TryComp<WFMonolithShardComponent>(node, out var shard)
               && shard.Deployed
               && TryComp<WFMonolithComponent>(shard.Parent, out var parent)
               && parent.Stage >= WFMonolithStage.Growth;
    }

    /// <summary>Roots a shard: hands its node to the Scion and puts the first tile under it. False on a sanctuary or off a grid.</summary>
    public bool SeedShard(EntityUid shard, Entity<WFCorruptionNodeComponent> node, Entity<WFMonolithComponent> parent)
    {
        var xform = Transform(shard);
        if (xform.GridUid is not { } gridUid || IsSanctuary(gridUid) || GridFull(gridUid)
            || !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return false;
        }

        AttachNode(node, parent);
        var tile = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var attempt = new WFCorruptionGrowAttemptEvent(tile);
        RaiseLocalEvent(gridUid, ref attempt);
        if (!attempt.Cancelled && !IsCorrupted((gridUid, grid), tile))
            Grow(parent, _map.GridTileToLocal(gridUid, grid, tile), node);

        return true;
    }

    /// <summary>Takes a shard's node away from its Scion. The tiles it was feeding starve.</summary>
    public void DetachNode(Entity<WFCorruptionNodeComponent> node)
    {
        if (node.Comp.Source is { } source && _nodes.TryGetValue(source, out var set))
        {
            set.Remove(node);
            if (set.Count == 0)
                _nodes.Remove(source);
        }

        node.Comp.Source = null;
        var now = _timing.CurTime;
        foreach (var uid in node.Comp.Tiles.ToList())
        {
            if (TryComp<WFCorruptionComponent>(uid, out var tile) && tile.DecayAt == null)
                SetDecay(uid, tile, now + TimeSpan.FromSeconds(_random.NextFloat(0f, NodeStarveSeconds)));
        }
    }
}
