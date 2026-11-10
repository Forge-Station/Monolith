using System.Linq;
using Content.Server._WF.Remade.Gifts;
using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Remade.Structures;
using Content.Shared._WF.Remade;
using Robust.Shared.Map;

namespace Content.Server._WF.Remade.Corruption;

/// <summary>
/// Nodes. A node feeds corruption within its reach up to its capacity, on top of what the Monolith's stage allows, and a hull
/// never carries more tiles than it has floor. When a node dies the tiles it was feeding starve.
/// </summary>
public sealed partial class WFCorruptionSystem
{
    [Dependency] private SharedTransformSystem _transform = default!;

    private const float NodeStarveSeconds = 30f;

    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _nodes = new();
    private readonly Dictionary<EntityUid, int> _gridTiles = new();

    private void InitializeNodes()
    {
        SubscribeLocalEvent<WFCorruptionNodeComponent, EntityTerminatingEvent>(OnNodeTerminating);
    }

    /// <summary>Tiles a Monolith may hold at a stage on a hull of this size with its nodes' capacity added, never past the hull cap.</summary>
    public static int Cap(WFMonolithStage stage, int hullTiles, int nodeCapacity)
    {
        return Math.Min(HullCap, Limit(stage, hullTiles) + nodeCapacity);
    }

    /// <summary>Hands a node to a Monolith to feed corruption for it.</summary>
    public void AttachNode(Entity<WFCorruptionNodeComponent> node, EntityUid monolith)
    {
        node.Comp.Source = monolith;
        if (!_nodes.TryGetValue(monolith, out var set))
            _nodes[monolith] = set = new HashSet<EntityUid>();

        set.Add(node);
    }

    /// <summary>Tiles on a hull.</summary>
    public int GridTiles(EntityUid grid)
    {
        return _gridTiles.GetValueOrDefault(grid);
    }

    /// <summary>Tiles a Monolith's own stage pays for: all of its corruption bar what nodes are feeding.</summary>
    public int OwnTiles(Entity<WFMonolithComponent> monolith)
    {
        var count = monolith.Comp.Corruption.Count;
        if (_nodes.TryGetValue(monolith, out var set))
        {
            foreach (var node in set)
            {
                if (TryComp<WFCorruptionNodeComponent>(node, out var comp))
                    count -= comp.Tiles.Count;
            }
        }

        return count;
    }

    /// <summary>Tiles all of a Monolith's live nodes can feed.</summary>
    public int NodeCapacity(EntityUid monolith)
    {
        var total = 0;
        if (_nodes.TryGetValue(monolith, out var set))
        {
            foreach (var node in set)
            {
                if (TryComp<WFCorruptionNodeComponent>(node, out var comp) && IsFeeding(node))
                    total += comp.Capacity;
            }
        }

        return total;
    }

    private bool IsFeeding(EntityUid node)
    {
        return TryComp<WFGrowthComponent>(node, out var growth) && growth.Active || ShardFeeds(node);
    }

    /// <summary>Works out who pays for a new tile: a node in reach with room, else the Monolith if its stage allows.</summary>
    private bool TryPay(Entity<WFMonolithComponent> monolith, EntityCoordinates at, out Entity<WFCorruptionNodeComponent>? payer, EntityUid? from = null)
    {
        payer = null;
        if (_nodes.TryGetValue(monolith, out var set) && set.Count > 0)
        {
            var map = _transform.ToMapCoordinates(at);
            var best = float.MaxValue;
            foreach (var node in set)
            {
                if (!TryComp<WFCorruptionNodeComponent>(node, out var comp)
                    || comp.Tiles.Count >= comp.Capacity
                    || !IsFeeding(node))
                {
                    continue;
                }

                var nodeMap = _transform.GetMapCoordinates(node);
                if (nodeMap.MapId != map.MapId)
                    continue;

                var distance = (nodeMap.Position - map.Position).Length();
                if (distance <= comp.Reach && distance < best)
                {
                    best = distance;
                    payer = new Entity<WFCorruptionNodeComponent>(node, comp);
                }
            }

            if (payer != null)
                return true;
        }

        // Growth spreading from a shard stays inside the shard's own small allowance.
        if (from != null && HasComp<WFMonolithShardComponent>(from))
            return false;

        return OwnTiles(monolith) < MonolithLimit(monolith);
    }

    private void OnNodeTerminating(Entity<WFCorruptionNodeComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.Source is { } source && _nodes.TryGetValue(source, out var set))
        {
            set.Remove(ent);
            if (set.Count == 0)
                _nodes.Remove(source);
        }

        var now = _timing.CurTime;
        foreach (var uid in ent.Comp.Tiles.ToList())
        {
            if (TryComp<WFCorruptionComponent>(uid, out var tile) && tile.DecayAt == null)
                SetDecay(uid, tile, now + TimeSpan.FromSeconds(_random.NextFloat(0f, NodeStarveSeconds)));
        }
    }
}
