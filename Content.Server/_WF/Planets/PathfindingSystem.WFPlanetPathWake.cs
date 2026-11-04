using System.Linq;
using System.Numerics;
using Content.Shared._WF.CCVar;

namespace Content.Server.NPC.Pathfinding;

/// <summary>
/// A path asked for across planet ground nobody has been near: the unbuilt chunks along the way are built first, nearest
/// the start first, and the request waits for them instead of finding no way.
/// </summary>
public sealed partial class PathfindingSystem
{
    /// <summary>How long a request waits for its ground before it runs on whatever is built.</summary>
    private static readonly TimeSpan WfGroundWait = TimeSpan.FromSeconds(10);

    /// <summary>How long a chunk stays wanted after the request that wanted it was answered.</summary>
    private static readonly TimeSpan WfWantedFor = TimeSpan.FromSeconds(15);

    /// <summary>Chunks either side of the straight line to the target that are built for a request.</summary>
    private const int WfCorridor = 2;

    /// <summary>How far along the line, in metres, a request has the ground built.</summary>
    private const float WfCorridorLength = 640f;

    /// <summary>Chunks wanted by path requests, per grid, with the squared distance from the request's start.</summary>
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, (float Distance, TimeSpan Until)>> _wfWanted = new();

    /// <summary>Requests waiting on unbuilt ground: the grid, the chunks along the way, and when they give up waiting.</summary>
    private readonly Dictionary<PathRequest, (EntityUid Grid, List<Vector2i> Chunks, TimeSpan Until)> _wfWaiting = new();

    /// <summary>Requests held back this update; read from the parallel path loop.</summary>
    private readonly HashSet<PathRequest> _wfHeld = new();

    private readonly HashSet<Vector2i> _wfCorridor = new();

    /// <summary>
    /// Holds back the requests about to run whose way crosses unbuilt planet ground, and has that ground built. Called
    /// on the main thread, before the parallel loop.
    /// </summary>
    private void WfHoldForGround(int amount)
    {
        _wfHeld.Clear();
        if (!_wfCfg.GetCVar(PlanetCVars.PathfindingLazy))
        {
            _wfWaiting.Clear();
            return;
        }

        var now = _timing.CurTime;
        for (var i = 0; i < amount; i++)
        {
            if (_pathRequests[i] is not AStarPathRequest request || request.Started || request.Task.IsCompleted)
                continue;

            if (!_wfWaiting.TryGetValue(request, out var waiting))
            {
                if (!WfWantGround(request, now, out waiting))
                    continue;
                _wfWaiting[request] = waiting;
            }

            if (now < waiting.Until && TryComp<GridPathfindingComponent>(waiting.Grid, out var comp) && WfAnyDirty(comp, waiting.Chunks))
            {
                _wfHeld.Add(request);
                continue;
            }

            _wfWaiting.Remove(request);
        }

        // Answered or cancelled elsewhere.
        if (_wfWaiting.Count > _wfHeld.Count)
        {
            foreach (var request in _wfWaiting.Keys.ToList())
            {
                if (request.Task.IsCompleted)
                    _wfWaiting.Remove(request);
            }
        }
    }

    /// <summary>Whether the parallel loop leaves this request for a later update.</summary>
    private bool WfHeld(PathRequest request)
    {
        return _wfHeld.Count > 0 && _wfHeld.Contains(request);
    }

    private static bool WfAnyDirty(GridPathfindingComponent comp, List<Vector2i> chunks)
    {
        foreach (var origin in chunks)
        {
            if (comp.DirtyChunks.Contains(origin))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Marks the unbuilt chunks along a request's straight line as wanted; false when it is not on a lazily built
    /// planet layer or nothing on its way is unbuilt.
    /// </summary>
    private bool WfWantGround(AStarPathRequest request, TimeSpan now, out (EntityUid Grid, List<Vector2i> Chunks, TimeSpan Until) waiting)
    {
        waiting = default;
        if (_transform.GetGrid(request.Start) is not { } grid
            || _transform.GetGrid(request.End) != grid
            || !WfOnPlanet(grid)
            || !TryComp<GridPathfindingComponent>(grid, out var comp)
            || comp.DirtyChunks.Count == 0)
            return false;

        var invMatrix = _transform.GetInvWorldMatrix(grid);
        var start = Vector2.Transform(_transform.ToMapCoordinates(request.Start).Position, invMatrix);
        var end = Vector2.Transform(_transform.ToMapCoordinates(request.End).Position, invMatrix);
        var line = end - start;
        var length = MathF.Min(line.Length(), WfCorridorLength);
        var direction = line.LengthSquared() > 0.0001f ? Vector2.Normalize(line) : Vector2.Zero;

        if (!_wfWanted.TryGetValue(grid, out var wanted))
            _wfWanted[grid] = wanted = new Dictionary<Vector2i, (float, TimeSpan)>();

        _wfCorridor.Clear();
        var chunks = new List<Vector2i>();
        var until = now + WfGroundWait + WfWantedFor;
        for (var along = 0f; ; along += ChunkSize / 2f)
        {
            along = MathF.Min(along, length);
            var point = start + direction * along;
            var centre = new Vector2i((int) MathF.Floor(point.X / ChunkSize), (int) MathF.Floor(point.Y / ChunkSize));
            for (var x = -WfCorridor; x <= WfCorridor; x++)
            for (var y = -WfCorridor; y <= WfCorridor; y++)
            {
                var origin = centre + new Vector2i(x, y);
                if (!_wfCorridor.Add(origin) || !comp.DirtyChunks.Contains(origin))
                    continue;

                chunks.Add(origin);
                var distance = along * along;
                if (!wanted.TryGetValue(origin, out var known) || known.Distance > distance || known.Until < until)
                    wanted[origin] = (MathF.Min(distance, known.Until > now ? known.Distance : distance), until);
            }

            if (along >= length)
                break;
        }

        if (chunks.Count == 0)
            return false;

        comp.NextUpdate = now;
        waiting = (grid, chunks, now + WfGroundWait);
        return true;
    }

    /// <summary>Adds the still-unbuilt chunks path requests want to the candidates; forgets the stale ones.</summary>
    private void WfAddWanted(EntityUid grid, GridPathfindingComponent comp)
    {
        if (!_wfWanted.TryGetValue(grid, out var wanted))
            return;

        var now = _timing.CurTime;
        foreach (var (origin, (distance, until)) in wanted)
        {
            if (until < now || !comp.DirtyChunks.Contains(origin))
            {
                wanted.Remove(origin);
                continue;
            }

            if (_wfPicked.Add(origin))
                _wfNear.Add((distance, origin));
        }

        if (wanted.Count == 0)
            _wfWanted.Remove(grid);
    }
}
