using System.Numerics;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.NPC;
using Robust.Shared.Configuration;

namespace Content.Server.NPC.Pathfinding;

/// <summary>
/// Builds a planet layer's navmesh only where someone is: round players and awake NPCs, and the way of a path asked
/// for, a slice an update, nearest first. A preloaded world dirties every chunk it has, and upstream built them all in one tick and kept them all.
/// </summary>
// What isn't built stays dirty and is built when someone comes near, or a path is asked for across it (WFPlanetPathWake).
public sealed partial class PathfindingSystem
{
    [Dependency] private IConfigurationManager _wfCfg = default!;

    /// <summary>Seconds before an unbuilt planet chunk with nobody near is looked at again.</summary>
    private static readonly TimeSpan WfIdleRecheck = TimeSpan.FromSeconds(1);

    private readonly List<Vector2> _wfInterest = new();
    private readonly List<(float Distance, Vector2i Origin)> _wfNear = new();
    private readonly List<Vector2i> _wfBuild = new();
    private readonly HashSet<Vector2i> _wfPicked = new();

    /// <summary>
    /// The dirty chunks of a grid to build this update: all of them, except on or over a planet layer, where only those
    /// near someone go, at most <see cref="PlanetCVars.PathfindingChunkBudget"/> of them.
    /// </summary>
    private List<Vector2i> WfChunksToBuild(EntityUid grid, GridPathfindingComponent comp)
    {
        _wfBuild.Clear();

        if (!WfOnPlanet(grid) || !_wfCfg.GetCVar(PlanetCVars.PathfindingLazy))
        {
            _wfBuild.AddRange(comp.DirtyChunks);
            return _wfBuild;
        }

        WfInterest(grid);
        var range = _wfCfg.GetCVar(PlanetCVars.PathfindingRange);
        var rangeSq = range * range;
        _wfNear.Clear();
        _wfPicked.Clear();

        if (_wfInterest.Count > 0)
        {
            var half = new Vector2(ChunkSize / 2f);
            foreach (var origin in comp.DirtyChunks)
            {
                var centre = origin * ChunkSize + half;
                var nearest = float.MaxValue;
                foreach (var pos in _wfInterest)
                {
                    nearest = MathF.Min(nearest, Vector2.DistanceSquared(centre, pos));
                }

                if (nearest <= rangeSq)
                {
                    _wfNear.Add((nearest, origin));
                    _wfPicked.Add(origin);
                }
            }
        }

        WfAddWanted(grid, comp);

        if (_wfNear.Count == 0)
        {
            comp.NextUpdate = _timing.CurTime + WfIdleRecheck;
            return _wfBuild;
        }

        _wfNear.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        var budget = Math.Max(1, _wfCfg.GetCVar(PlanetCVars.PathfindingChunkBudget));
        for (var i = 0; i < _wfNear.Count && i < budget; i++)
        {
            _wfBuild.Add(_wfNear[i].Origin);
        }

        return _wfBuild;
    }

    /// <summary>Drops what was built from the dirty set; the rest waits, and is looked at again next update.</summary>
    private void WfClearBuilt(GridPathfindingComponent comp, List<Vector2i> built)
    {
        if (built.Count == comp.DirtyChunks.Count)
        {
            comp.DirtyChunks.Clear();
            return;
        }

        foreach (var origin in built)
        {
            comp.DirtyChunks.Remove(origin);
        }
    }

    /// <summary>Whether a grid is a planet layer's ground or stands on one, such as a city's upper level or a wreck.</summary>
    private bool WfOnPlanet(EntityUid grid)
    {
        return HasComp<WFPlanetLayerComponent>(grid)
            || TryComp(grid, out TransformComponent? xform) && HasComp<WFPlanetLayerComponent>(xform.MapUid);
    }

    /// <summary>Where on a grid's map players and awake NPCs stand, in the grid's own frame.</summary>
    private void WfInterest(EntityUid grid)
    {
        _wfInterest.Clear();
        if (!TryComp(grid, out TransformComponent? gridXform) || gridXform.MapUid is not { } map)
            return;

        var invMatrix = _transform.GetInvWorldMatrix(gridXform);
        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is { } body
                && TryComp(body, out TransformComponent? xform)
                && xform.MapUid == map)
                _wfInterest.Add(Vector2.Transform(_transform.GetWorldPosition(xform), invMatrix));
        }

        var npcs = EntityQueryEnumerator<ActiveNPCComponent, TransformComponent>();
        while (npcs.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapUid == map)
                _wfInterest.Add(Vector2.Transform(_transform.GetWorldPosition(xform), invMatrix));
        }
    }
}
