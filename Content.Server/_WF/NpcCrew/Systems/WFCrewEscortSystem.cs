using System.Linq;
using Content.Server._WF.NpcCrew.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Maintains escort alliances independently of temporary evasive flight orders.</summary>
public sealed partial class WFCrewEscortSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private WFCaptainSystem _captains = default!;
    [Dependency] private IGameTiming _timing = default!;
    private readonly Dictionary<(EntityUid Grid, string Group), EntityUid> _escorts = new();
    private readonly HashSet<(EntityUid Ship, EntityUid Ally)> _allies = new();

    // Rebuilt at most once per tick; a rebuild allocates new sets, so earlier results stay valid snapshots.
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _formations = new();
    private readonly HashSet<EntityUid> _involved = new();
    private GameTick _builtTick;
    private bool _stale = true;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPilotOrdersChangedEvent>(OnOrdersChanged);
        SubscribeLocalEvent<WFCrewComponent, ComponentShutdown>(OnCrewShutdown);
    }

    /// <summary>Assigns an escort without requiring its captain or pilot to survive the engagement.</summary>
    public void SetEscort(EntityUid grid, string group, EntityUid target)
    {
        if (grid == target || !HasComp<MapGridComponent>(grid) || !HasComp<MapGridComponent>(target)
            || Transform(grid).MapID != Transform(target).MapID)
            return;
        _escorts[(grid, group)] = target;
        Invalidate();
    }

    /// <summary>Records a pilot's newly assigned formation.</summary>
    public void SetEscort(EntityUid pilot, EntityUid target)
    {
        if (TryComp<WFCrewComponent>(pilot, out var crew) && Transform(pilot).GridUid is { } grid)
            SetEscort(grid, crew.Group, target);
    }

    /// <summary>Ends an assignment when its mission is replaced, paused, completed or cancelled.</summary>
    public void Clear(EntityUid grid, string group)
    {
        if (_escorts.Remove((grid, group)))
            Invalidate();
    }

    /// <summary>Makes any ship an ally of a crewed ship, as if in its formation, until removed.</summary>
    public void AddAlly(EntityUid ship, EntityUid ally)
    {
        if (ship != ally && _allies.Add((ship, ally)))
            Invalidate();
    }

    public void RemoveAlly(EntityUid ship, EntityUid ally)
    {
        if (_allies.Remove((ship, ally)))
            Invalidate();
    }

    /// <summary>Drops this tick's formation snapshot after crew, escorts or battlegroups change.</summary>
    public void Invalidate() => _stale = true;

    private void OnOrdersChanged(ref WFPilotOrdersChangedEvent args)
    {
        if (args.Continuation || _captains.IsChangingOrders
            || !TryComp<WFCrewComponent>(args.Mob, out var crew) || Transform(args.Mob).GridUid is not { } grid)
            return;
        Clear(grid, crew.Group);
    }

    private void OnCrewShutdown(Entity<WFCrewComponent> ent, ref ComponentShutdown args) => Invalidate();

    /// <summary>
    /// Snapshots connected escorts and their protected vessels before alert handlers change orders. The set is shared
    /// by every caller this tick; don't modify it.
    /// </summary>
    public HashSet<EntityUid> GetFormation(EntityUid grid)
    {
        if (TerminatingOrDeleted(grid) || !HasComp<MapGridComponent>(grid))
            return new HashSet<EntityUid> { grid };
        Refresh();
        if (!_formations.TryGetValue(grid, out var members))
            _formations[grid] = members = new HashSet<EntityUid> { grid };
        return members;
    }

    /// <summary>Formation members remain allies even when their company or local crew labels differ.</summary>
    public bool AreInFormation(EntityUid first, EntityUid second) => GetFormation(first).Contains(second);

    /// <summary>Whether a grid is a living crew's home or part of an escort or battlegroup.</summary>
    public bool IsInvolved(EntityUid grid)
    {
        Refresh();
        return _involved.Contains(grid);
    }

    private void Refresh()
    {
        if (!_stale && _builtTick == _timing.CurTick)
            return;
        _stale = false;
        _builtTick = _timing.CurTick;
        _formations.Clear();
        _involved.Clear();

        var crews = new HashSet<(EntityUid Grid, string Group)>();
        var battlegroups = new Dictionary<(MapId Map, string Name), List<EntityUid>>();
        var query = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var transform))
        {
            if (!_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid)
                || (crew.Post?.EntityId ?? transform.GridUid) is not { } home || !HasComp<MapGridComponent>(home))
                continue;
            crews.Add((home, crew.Group));
            _involved.Add(home);
            if (crew.Battlegroup.Length == 0)
                continue;
            var key = (Transform(home).MapID, crew.Battlegroup);
            if (!battlegroups.TryGetValue(key, out var ships))
                battlegroups[key] = ships = new List<EntityUid>();
            if (!ships.Contains(home))
                ships.Add(home);
        }

        var neighbors = new Dictionary<EntityUid, HashSet<EntityUid>>();
        // Every ship of a battlegroup is linked through its first ship.
        foreach (var ships in battlegroups.Values)
        {
            for (var i = 1; i < ships.Count; i++)
                Link(neighbors, ships[0], ships[i]);
        }
        foreach (var (key, target) in _escorts.ToArray())
        {
            if (!crews.Contains(key) || TerminatingOrDeleted(key.Grid) || TerminatingOrDeleted(target)
                || !HasComp<MapGridComponent>(key.Grid) || !HasComp<MapGridComponent>(target))
            {
                _escorts.Remove(key);
                continue;
            }
            // FTL temporarily separates maps; keep the assignment for their reunion.
            if (Transform(key.Grid).MapID != Transform(target).MapID)
                continue;
            Link(neighbors, key.Grid, target);
        }

        _allies.RemoveWhere(pair => TerminatingOrDeleted(pair.Ship) || TerminatingOrDeleted(pair.Ally));
        foreach (var (ship, ally) in _allies)
        {
            if (Transform(ship).MapID == Transform(ally).MapID)
                Link(neighbors, ship, ally);
        }

        var pending = new Queue<EntityUid>();
        foreach (var start in neighbors.Keys)
        {
            if (_formations.ContainsKey(start))
                continue;
            var members = new HashSet<EntityUid> { start };
            pending.Enqueue(start);
            while (pending.TryDequeue(out var current))
            {
                foreach (var member in neighbors[current])
                {
                    if (members.Add(member))
                        pending.Enqueue(member);
                }
            }
            foreach (var member in members)
                _formations[member] = members;
            _involved.UnionWith(members);
        }
    }

    private static void Link(Dictionary<EntityUid, HashSet<EntityUid>> neighbors, EntityUid first, EntityUid second)
    {
        if (!neighbors.TryGetValue(first, out var outgoing))
            neighbors[first] = outgoing = new HashSet<EntityUid>();
        outgoing.Add(second);
        if (!neighbors.TryGetValue(second, out var incoming))
            neighbors[second] = incoming = new HashSet<EntityUid>();
        incoming.Add(first);
    }
}
