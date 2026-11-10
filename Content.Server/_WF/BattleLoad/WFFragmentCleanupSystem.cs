using Content.Shared._WF.CCVar;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Robust.Server.Physics;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.BattleLoad;

/// <summary>Deletes the tiny grids a hull sheds as it breaks up, unless a player or a mob is aboard.</summary>
public sealed partial class WFFragmentCleanupSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedMapSystem _map = default!;

    private readonly List<EntityUid> _pending = new();
    private int _maxTiles;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GridSplitEvent>(OnSplit);
        Subs.CVar(_cfg, BattleLoadCVars.FragmentMaxTiles, value => _maxTiles = value, true);
    }

    private void OnSplit(ref GridSplitEvent args)
    {
        if (_maxTiles <= 0)
            return;

        // Judged next tick, once every system has had its say on the split.
        _pending.AddRange(args.NewGrids);
    }

    public override void Update(float frameTime)
    {
        if (_pending.Count == 0)
            return;

        foreach (var uid in _pending)
        {
            if (TerminatingOrDeleted(uid) || !TryComp<MapGridComponent>(uid, out var grid))
                continue;

            if (!IsSmall((uid, grid)) || HasSomeoneAboard(uid))
                continue;

            QueueDel(uid);
        }

        _pending.Clear();
    }

    /// <summary>Whether a grid has at most the configured number of tiles.</summary>
    public bool IsSmall(Entity<MapGridComponent> grid)
    {
        var count = 0;
        var tiles = _map.GetAllTiles(grid, grid.Comp);
        while (tiles.MoveNext(out _))
        {
            if (++count > _maxTiles)
                return false;
        }

        return true;
    }

    private bool HasSomeoneAboard(EntityUid uid)
    {
        // Any mob counts: NPC crew have no mind, and a corpse may yet be revived.
        if (HasComp<ActorComponent>(uid)
            || HasComp<MobStateComponent>(uid)
            || TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind)
            return true;

        var children = Transform(uid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (HasSomeoneAboard(child))
                return true;
        }

        return false;
    }
}
