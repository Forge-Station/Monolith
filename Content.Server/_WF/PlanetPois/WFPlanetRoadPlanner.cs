using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._WF.PlanetPois;
using Robust.Shared.Maths;

namespace Content.Server._WF.PlanetPois;

/// <summary>
/// A reserved site and its absolute road entrances. A priority site, such as a cargo hub or a city, is routed first
/// and with a larger search.
/// </summary>
public sealed record WFPlanetRoadSite(Box2i Bounds, WFPlanetPoiConnection Connection, IReadOnlyList<WFPlanetRoadSocket> Sockets,
    bool Priority = false);

/// <summary>An absolute entrance tile with an outward direction.</summary>
public sealed record WFPlanetRoadSocket(Vector2i Position, WFPlanetPoiDirection Direction);

/// <summary>
/// What a world's roads reach besides its site entrances: how many leave the landing zone, how many run out toward
/// the edge, how many extra links close a long detour into a loop, and how near they come to an off-road site.
/// </summary>
public sealed record WFPlanetRoadShape(int Gates = 2, int Exits = 1, int Loops = 1, int Reach = 64)
{
    public static readonly WFPlanetRoadShape Default = new();

    /// <summary>Just the site entrances, joined to one another.</summary>
    public static readonly WFPlanetRoadShape SitesOnly = new(0, 0, 0, 0);
}

/// <summary>A connected road network, or an explicit routing failure with no paintable tiles.</summary>
public sealed record WFPlanetRoadPlan(bool Success, string? Error, HashSet<Vector2i> RoadTiles, HashSet<Vector2i> ShoulderTiles)
{
    /// <summary>The tiles down the middle of the routed roads; the short approaches into the sites are not among them.</summary>
    public HashSet<Vector2i> Centreline { get; init; } = new();

    /// <summary>On a failure that is one entrance's alone, that entrance: the rest may route without its site.</summary>
    public Vector2i? FailedEntrance { get; init; }

    /// <summary>
    /// The centreline of the roads that serve the world rather than one site: those out of the landing zone, toward
    /// the edge and the extra links; empty when it has none.
    /// </summary>
    public HashSet<Vector2i> Backbone { get; init; } = new();
}

/// <summary>
/// Routes a world's roads without crossing reserved buildings, pinned terrain or water. The roads join places: the
/// landing zone, by a road or two out of its edge, every site entrance and, within reach, the off-road sites a
/// footpath will join; each is joined to the nearest road already laid, nearest first, so they branch like a tree and
/// meet at T and Y junctions. A road or two may run on toward the world's edge, and a link may close a long detour
/// into a loop. Roads run straight and bend 45° at a time.
/// </summary>
public static class WFPlanetRoadPlanner
{
    /// <summary>A* expansions one route may use.</summary>
    private const int SearchLimit = 200000;

    /// <summary>A* expansions the whole plan may use.</summary>
    private const int TotalSearchLimit = 2500000;

    /// <summary>How many times more a priority site's route may search, from a budget of its own.</summary>
    private const int PrioritySearchScale = 4;

    /// <summary>What a straight step costs.</summary>
    private const int StepCost = 10;

    /// <summary>What a diagonal step costs: two tiles of staircase, about √2 of a straight step.</summary>
    private const int DiagonalCost = 14;

    /// <summary>What a 45° bend costs.</summary>
    private const int BendCost = 25;

    /// <summary>What a square turn costs, so roads bend gently and turn square rarely.</summary>
    private const int TurnCost = 70;

    /// <summary>What a step costs beside a road the route hasn't joined, so roads meet square, not alongside.</summary>
    private const int HugCost = 40;

    /// <summary>Tiles past a road's shoulder that count as beside it.</summary>
    private const int HugGap = 3;

    /// <summary>
    /// How far, in tiles, a through site's second way in runs before it may fork off the road from its first, so the
    /// two never run side by side.
    /// </summary>
    private const int ThroughFork = 32;

    /// <summary>Tiles between the landing zone's edge and a gate road's end.</summary>
    private const int GateGap = 3;

    /// <summary>
    /// How far out round the landing zone a step costs <see cref="PadCost"/> more, so roads come to it only by the
    /// gates and never run round its edge; the gate roads run straight out past it.
    /// </summary>
    private const int PadMargin = 10;

    /// <summary>What a step costs within <see cref="PadMargin"/> of the landing zone, off the gate roads.</summary>
    private const int PadCost = 30;

    /// <summary>Tiles kept between an exit road's end and the world's edge.</summary>
    private const int EdgeGap = 12;

    /// <summary>The least room between the landing zone and the edge for gates and exits; a smaller world gets neither.</summary>
    private const int MinRoom = 48;

    /// <summary>The narrowest gap round the landing zone, in degrees, an exit road runs out into.</summary>
    private const float MinExitGap = 60f;

    /// <summary>The least angle, in degrees, between two gates out of the landing zone.</summary>
    private const float MinGateSpread = 120f;

    /// <summary>A link closes a detour at least this many times longer by road than as the crow flies.</summary>
    private const float DetourRatio = 2.5f;

    /// <summary>A link is at least this long, so it never doubles a road a few tiles off.</summary>
    private const int MinLink = 32;

    /// <summary>A road out to an off-road site stops at least this far short of its door, for the footpath.</summary>
    private const int NearbyGap = 16;

    /// <summary>The most a gate turns, in degrees each way, from the site it faces to find open ground.</summary>
    private const int GateSwing = 60;

    /// <summary>Headings: north, east, south, west, then the diagonals north-east, south-east, south-west, north-west.</summary>
    private static readonly Vector2i[] Steps =
    {
        new(0, 1), new(1, 0), new(0, -1), new(-1, 0), new(1, 1), new(1, -1), new(-1, -1), new(-1, 1),
    };

    /// <summary>Each heading's compass point in eighths of a turn, clockwise from north.</summary>
    private static readonly int[] Octant = { 0, 2, 4, 6, 1, 3, 5, 7 };

    private static readonly Vector2i[] Cardinals = { new(0, 1), new(1, 0), new(0, -1), new(-1, 0) };

    /// <summary>
    /// A place a road must reach: a site entrance's road end, or with <see cref="Near"/> an off-road site's door the
    /// road need only come within reach of.
    /// </summary>
    private sealed record Node(Vector2i Start, int Heading, int Site, Vector2i? Socket, bool Near = false, bool Priority = false);

    /// <summary>The owner of the roads out to off-road sites.</summary>
    private const int NearbyOwner = -2;

    /// <summary>Raised inside the planner when the trees out of two gates can't be joined.</summary>
    private sealed class JoinFailed : Exception
    {
    }

    /// <summary>
    /// Plans all entrances atomically; a failed entrance rejects the complete network. <paramref name="impassable"/>
    /// marks more ground no road may cross, such as water, <paramref name="shape"/> sets the roads beyond the sites and
    /// <paramref name="nearby"/> are the doors of off-road sites the roads come within reach of, for their footpaths.
    /// </summary>
    public static WFPlanetRoadPlan Plan(Vector2 centre, float radius, float centreClear,
        IReadOnlyList<WFPlanetRoadSite> sites, IReadOnlySet<Vector2i> blocked,
        int width, int shoulderWidth, int seed, Func<Vector2i, bool>? impassable = null, WFPlanetRoadShape? shape = null,
        IReadOnlyList<Vector2i>? nearby = null)
    {
        WFPlanetRoadPlan Fail(string reason, Vector2i? entrance = null) =>
            new(false, reason, new(), new()) { FailedEntrance = entrance };
        if (!float.IsFinite(radius) || !float.IsFinite(centreClear) || !float.IsFinite(centre.X) || !float.IsFinite(centre.Y)
            || width < 1 || width > 15 || width % 2 == 0 || shoulderWidth < 0 || shoulderWidth > 8 || radius <= 0 || radius > 4096 || centreClear < 0)
            return Fail("Invalid road dimensions or world bounds.");
        shape ??= WFPlanetRoadShape.Default;

        var low = -(width / 2);
        var high = low + width - 1;
        var clearance = Math.Max(-low, high) + shoulderWidth;
        var entrances = new List<(WFPlanetRoadSocket Socket, int Site)>();
        var approaches = new Dictionary<WFPlanetRoadSite, HashSet<Vector2i>>();
        for (var index = 0; index < sites.Count; index++)
        {
            var site = sites[index];
            if (site.Bounds.Right <= site.Bounds.Left || site.Bounds.Top <= site.Bounds.Bottom || !Enum.IsDefined(site.Connection))
                return Fail("A site has invalid reserved bounds or connection mode.");
            if (site.Connection == WFPlanetPoiConnection.Isolated)
                continue;
            IReadOnlyList<WFPlanetRoadSocket> sockets = site.Connection == WFPlanetPoiConnection.Through ? site.Sockets : site.Sockets.Take(1).ToArray();
            if (sockets.Count == 0 || site.Connection == WFPlanetPoiConnection.Through && (sockets.Select(s => s.Position).Distinct().Count() < 2 || sockets.Select(s => s.Direction).Distinct().Count() < 2))
                return Fail("A connected site has insufficient distinct entrances.");
            var siteApproaches = new HashSet<Vector2i>();
            if (!approaches.TryAdd(site, siteApproaches))
                return Fail("A connected site is listed more than once.");
            foreach (var socket in sockets)
            {
                if (!Enum.IsDefined(socket.Direction))
                    return Fail("An entrance has an invalid outward direction.");
                var direction = Direction(socket.Direction);
                // A socket may start on the outermost reserved tile, never deep inside a building.
                if (!Inside(socket.Position, site.Bounds) || Inside(socket.Position + direction, site.Bounds))
                    return Fail("An entrance does not face out of its reserved footprint.");
                entrances.Add((socket, index));
                for (var step = 0; step <= clearance + 1; step++)
                {
                    var point = socket.Position + direction * step;
                    for (var side = -clearance; side <= clearance; side++)
                        siteApproaches.Add(point + new Vector2i(-direction.Y, direction.X) * side);
                }
            }
        }

        var tileCache = new Dictionary<Vector2i, bool>();
        bool TileAllowed(Vector2i tile)
        {
            if (tileCache.TryGetValue(tile, out var known))
                return known;
            var position = new Vector2(tile.X + 0.5f, tile.Y + 0.5f);
            var distance = Vector2.Distance(position, centre);
            // Include the complete tile square inside the world circle.
            var allowed = !(distance + 0.707107f > radius || centreClear > 0 && distance - 0.707107f < centreClear || blocked.Contains(tile))
                          && sites.All(site => !Inside(tile, site.Bounds) || approaches.TryGetValue(site, out var near) && near.Contains(tile))
                          && impassable?.Invoke(tile) != true;
            return tileCache[tile] = allowed;
        }
        var clearanceCache = new Dictionary<Vector2i, bool>();
        bool Allowed(Vector2i point)
        {
            if (clearanceCache.TryGetValue(point, out var cached))
                return cached;
            for (var x = low - shoulderWidth; x <= high + shoulderWidth; x++)
            for (var y = low - shoulderWidth; y <= high + shoulderWidth; y++)
                if (!TileAllowed(point + new Vector2i(x, y)))
                    return clearanceCache[point] = false;
            return clearanceCache[point] = true;
        }

        var nodes = new List<Node>();
        foreach (var (socket, site) in entrances)
        {
            // Keep the complete width outside the building after the approach.
            var direction = Direction(socket.Direction);
            var start = socket.Position + direction * (clearance + 1);
            for (var step = 0; step <= clearance + 1; step++)
            {
                var point = socket.Position + direction * step;
                // At the entrance, width runs across the socket; it must not paint back into the building.
                for (var side = low - shoulderWidth; side <= high + shoulderWidth; side++)
                {
                    var lane = point + new Vector2i(-direction.Y, direction.X) * side;
                    if (!TileAllowed(lane))
                        return Fail($"The approach from the entrance at {socket.Position} is blocked or outside the world at {lane}.", socket.Position);
                }
            }
            if (!Allowed(start))
                return Fail($"The entrance at {socket.Position} lacks road clearance at {start}.", socket.Position);
            nodes.Add(new Node(start, (int) socket.Direction, site, socket.Position, Priority: sites[site].Priority));
        }
        if (shape.Reach > 0 && nodes.Count > 0 && nearby != null)
        {
            foreach (var door in nearby.Distinct().OrderBy(door => door.X).ThenBy(door => door.Y))
                nodes.Add(new Node(door, -1, NearbyOwner, null, true));
        }

        WFPlanetRoadPlan? network = null;
        // Two gates whose roads can't be joined fall back to one, then none.
        for (var gates = Math.Max(0, shape.Gates); network == null; gates = gates > 1 ? 1 : gates - 1)
        {
            try
            {
                network = Network(centre, radius, centreClear, clearance, sites, nodes, shape with { Gates = Math.Max(0, gates) }, seed, Allowed);
            }
            catch (JoinFailed) when (gates > 0)
            {
            }
            catch (JoinFailed)
            {
                return Fail("The roads could not be joined into one network.");
            }
        }
        if (!network.Success)
            return network;

        var roads = new HashSet<Vector2i>();
        var shoulders = new HashSet<Vector2i>();
        foreach (var point in network.Centreline)
        {
            for (var x = low - shoulderWidth; x <= high + shoulderWidth; x++)
            for (var y = low - shoulderWidth; y <= high + shoulderWidth; y++)
            {
                var tile = point + new Vector2i(x, y);
                if (!TileAllowed(tile))
                    return Fail("The completed network violates its clearance.");
                if (x >= low && x <= high && y >= low && y <= high) roads.Add(tile);
                else shoulders.Add(tile);
            }
        }
        foreach (var (socket, _) in entrances)
        {
            var direction = Direction(socket.Direction);
            for (var step = 0; step <= clearance + 1; step++)
            for (var side = low - shoulderWidth; side <= high + shoulderWidth; side++)
            {
                var tile = socket.Position + direction * step + new Vector2i(-direction.Y, direction.X) * side;
                if (side >= low && side <= high) roads.Add(tile);
                else shoulders.Add(tile);
            }
        }
        shoulders.ExceptWith(roads);
        return new(true, null, roads, shoulders) { Centreline = network.Centreline, Backbone = network.Backbone };
    }

    /// <summary>
    /// Lays the centreline: gates out of the landing zone first, then each entrance joined to the nearest road, nearest
    /// first; then the trees out of different gates joined, the exits run out and the loops closed.
    /// </summary>
    private static WFPlanetRoadPlan Network(Vector2 centre, float radius, float centreClear, int clearance,
        IReadOnlyList<WFPlanetRoadSite> sites, List<Node> entrances, WFPlanetRoadShape shape, int seed, Func<Vector2i, bool> allowed)
    {
        var random = new Random(seed);
        var directions = new[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        for (var i = directions.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (directions[i], directions[j]) = (directions[j], directions[i]);
        }
        var budget = TotalSearchLimit;
        var priorityBudget = TotalSearchLimit * PrioritySearchScale;
        var network = new HashSet<Vector2i>();
        var backbone = new HashSet<Vector2i>();
        // Which site's own road a tile was laid for: -1 for the gates, exits and links, -2 for the roads out to off-road sites.
        var owner = new Dictionary<Vector2i, int>();
        // Tiles under a road's width and shoulders, for the cost of running alongside one.
        var beside = new HashSet<Vector2i>();
        void Join(IEnumerable<Vector2i> points, int site)
        {
            foreach (var point in points)
            {
                if (!network.Add(point))
                    continue;
                owner[point] = site;
                if (site == -1)
                    backbone.Add(point);
                for (var x = -clearance - HugGap; x <= clearance + HugGap; x++)
                for (var y = -clearance - HugGap; y <= clearance + HugGap; y++)
                    beside.Add(point + new Vector2i(x, y));
            }
        }
        var gateRadius = centreClear > 0 ? centreClear + clearance * 1.5f + GateGap : 0f;
        var padBand = centreClear > 0 ? gateRadius + PadMargin : 0f;
        // Distances go round the band about the landing zone that roads keep out of.
        var pad = new Pad(centre, MathF.Max(0f, padBand - GateGap), padBand);
        int Hug(Vector2i tile) => network.Contains(tile) ? 0
            : (beside.Contains(tile) ? HugCost : 0) + (Vector2.Distance(Middle(tile), centre) < padBand ? PadCost : 0);
        // A road an entrance may join: any but its own site's, which only once well clear of the site.
        bool Joins(Node node, Vector2i tile) => owner[tile] != node.Site || pad.Distance(tile, node.Start) >= ThroughFork;

        var edgeRadius = radius - clearance * 1.5f - EdgeGap;
        var roomy = centreClear > 0 && edgeRadius - gateRadius >= MinRoom;

        // Gates: the first faces the site that matters most, a big one near, the rest the sites furthest round from it.
        var doors = entrances.Where(node => !node.Near).ToList();
        if (roomy && shape.Gates > 0 && doors.Count > 0)
        {
            var angles = new List<float>();
            float Angle(Vector2i tile) => MathF.Atan2(tile.Y + 0.5f - centre.Y, tile.X + 0.5f - centre.X);
            var first = doors
                .Select(node => (Node: node, Score: MathF.Sqrt(Area(sites[node.Site].Bounds)) / MathF.Max(1f, Vector2.Distance(Middle(node.Start), centre))))
                .OrderByDescending(pair => pair.Node.Priority).ThenByDescending(pair => pair.Score).ThenBy(pair => pair.Node.Start.X).ThenBy(pair => pair.Node.Start.Y).First().Node;
            angles.Add(Angle(first.Start));
            while (angles.Count < shape.Gates)
            {
                var (node, spread) = doors
                    .Select(node => (Node: node, Spread: angles.Min(angle => AngleBetween(angle, Angle(node.Start)))))
                    .OrderByDescending(pair => pair.Spread).ThenBy(pair => pair.Node.Start.X).ThenBy(pair => pair.Node.Start.Y).First();
                if (spread < MinGateSpread * MathF.PI / 180f)
                    break;
                angles.Add(Angle(node.Start));
            }
            foreach (var facing in angles)
            {
                // Straight out from the edge of the landing zone, past the band where roads keep off it; turned aside
                // a little at a time where a site or a lake stands in the way.
                for (var swing = 0; swing <= GateSwing * 2; swing += 10)
                {
                    var angle = facing + (swing % 20 == 0 ? swing / 2 : -(swing + 10) / 2) * MathF.PI / 180f;
                    if (Open(centre, angle, gateRadius, 1, allowed) is not { } gate
                        || Open(centre, angle, padBand + GateGap, 1, allowed) is not { } end)
                        continue;
                    var stub = Route(new[] { (gate, -1) }, tile => tile == end, end, allowed, _ => 0, directions, SearchLimit / 8, ref budget, pad);
                    if (stub == null)
                        continue;
                    Join(stub, -1);
                    break;
                }
            }
        }

        // Each entrance joins the nearest road, nearest first; a through site's second way in joins another road, or
        // forks off its own well clear of the site.
        var pending = new List<Node>(entrances);
        if (network.Count == 0 && pending.Count > 0)
        {
            var first = pending.Where(node => !node.Near).OrderByDescending(node => node.Priority).First();
            Join(new[] { first.Start }, first.Site);
            pending.Remove(first);
        }
        while (pending.Count > 0)
        {
            Node? next = null;
            var nextDistance = float.MaxValue;
            var nextAim = default(Vector2i);
            // Priority entrances go first, then the rest; the off-road sites wait for the entrances, whose roads may
            // pass near enough already.
            var priorityLeft = pending.Any(node => node.Priority);
            var doorsLeft = pending.Any(node => !node.Near);
            foreach (var node in pending)
            {
                if (node.Near && doorsLeft || priorityLeft && !node.Priority)
                    continue;
                var aim = node.Near ? Nearest(network, node.Start, pad, out var distance)
                    : Nearest(network.Where(tile => Joins(node, tile)), node.Start, pad, out distance);
                if (distance == float.MaxValue)
                    aim = Nearest(network, node.Start, pad, out distance);
                if (distance >= nextDistance)
                    continue;
                next = node;
                nextDistance = distance;
                nextAim = aim;
            }
            var chosen = next!;
            pending.Remove(chosen);
            if (chosen.Near)
            {
                // An off-road site the roads already pass near enough is left to its footpath.
                if (nextDistance <= shape.Reach)
                    continue;
                // Otherwise a road comes out to it, stopping short of its door where the ground is open.
                var toward = Vector2.Normalize(Middle(nextAim) - Middle(chosen.Start));
                Vector2i? from = null;
                for (var gap = NearbyGap; gap <= shape.Reach && from == null; gap += 2)
                {
                    var at = Middle(chosen.Start) + toward * gap;
                    var tile = new Vector2i((int) MathF.Floor(at.X), (int) MathF.Floor(at.Y));
                    if (allowed(tile))
                        from = tile;
                }
                if (from is not { } near)
                    continue;
                var drive = network.Contains(near) ? null
                    : Route(new[] { (near, -1) }, network.Contains, nextAim, allowed, Hug, directions, SearchLimit, ref budget, pad);
                if (drive != null)
                    Join(drive, NearbyOwner);
                continue;
            }
            if (network.Contains(chosen.Start))
            {
                Join(new[] { chosen.Start }, chosen.Site);
                continue;
            }
            var limit = chosen.Priority ? SearchLimit * PrioritySearchScale : SearchLimit;
            ref var spend = ref chosen.Priority ? ref priorityBudget : ref budget;
            var spur = Route(new[] { (chosen.Start, chosen.Heading) }, tile => network.Contains(tile) && Joins(chosen, tile),
                nextAim, allowed, Hug, directions, limit, ref spend, pad);
            // Its own road near the site will do when nothing else can be reached.
            if (spur == null && owner.ContainsValue(chosen.Site))
            {
                spur = Route(new[] { (chosen.Start, chosen.Heading) }, network.Contains, Nearest(network, chosen.Start, pad, out _),
                    allowed, Hug, directions, limit, ref spend, pad);
            }
            if (spur == null)
                return new(false, $"The entrance at {chosen.Socket} cannot reach the road network within the routing budget.", new(), new())
                    { FailedEntrance = chosen.Socket };
            Join(spur, chosen.Site);
        }
        if (network.Count == 0)
            return new(false, "No road could be laid.", new(), new());

        // The trees out of different gates are joined where they come nearest.
        while (true)
        {
            var pieces = Components(network).OrderBy(piece => piece.Count).ThenBy(piece => Lowest(piece).X).ThenBy(piece => Lowest(piece).Y).ToList();
            if (pieces.Count <= 1)
                break;
            var piece = pieces[0];
            var starts = piece.OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
            var others = network.Where(tile => !piece.Contains(tile)).ToList();
            var aim = default(Vector2i);
            var best = float.MaxValue;
            foreach (var tile in starts)
            {
                var near = Nearest(others, tile, pad, out var distance);
                if (distance >= best)
                    continue;
                best = distance;
                aim = near;
            }
            var link = Route(starts.Select(tile => (tile, -1)).ToArray(), tile => network.Contains(tile) && !piece.Contains(tile), aim,
                allowed, Hug, directions, SearchLimit, ref budget, pad);
            if (link == null)
                throw new JoinFailed();
            Join(link, -1);
        }

        // Exits run out into the widest empty stretches round the world, toward its edge.
        if (roomy && shape.Exits > 0)
        {
            var taken = network.Where(tile => owner[tile] >= 0 || Vector2.Distance(Middle(tile), centre) > gateRadius + 2)
                .Select(tile => MathF.Atan2(tile.Y + 0.5f - centre.Y, tile.X + 0.5f - centre.X)).ToList();
            for (var exit = 0; exit < shape.Exits; exit++)
            {
                if (Widest(taken) is not { } gap || gap.Width < MinExitGap * MathF.PI / 180f)
                    break;
                taken.Add(gap.Middle);
                if (Open(centre, gap.Middle, edgeRadius, -1, allowed) is not { } end)
                    continue;
                var road = Route(new[] { (end, -1) }, network.Contains, Nearest(network, end, pad, out _), allowed, Hug, directions, SearchLimit, ref budget, pad);
                if (road != null)
                    Join(road, -1);
            }
        }

        // Links close the worst detours: two places near as the crow flies but far apart by road. A link never makes a
        // loop round the landing zone, nor goes round it itself.
        var places = entrances.Where(node => !node.Near).Select(node => (node.Start, node.Site)).Where(place => network.Contains(place.Start)).ToList();
        bool Closes(Vector2i from, Vector2i to, Dictionary<Vector2i, (int Steps, float Sweep)> roads, out float saving)
        {
            saving = 0f;
            if (!roads.TryGetValue(to, out var byRoad))
                return false;
            var direct = Vector2.Distance(Middle(from), Middle(to));
            if (direct < MinLink || direct > radius || byRoad.Steps < direct * DetourRatio
                || pad.Distance(from, to) > direct + 1f
                || MathF.Abs(byRoad.Sweep + Turn(centre, Middle(to), Middle(from))) > MathF.PI)
                return false;
            saving = byRoad.Steps - direct;
            return true;
        }
        for (var loop = 0; loop < shape.Loops && places.Count > 1; loop++)
        {
            var best = (From: default(Vector2i), To: default(Vector2i), Roads: (Dictionary<Vector2i, (int, float)>?) null, Saving: 0f);
            foreach (var from in places)
            {
                var roads = Distances(network, from.Start, centre);
                foreach (var to in places)
                {
                    if (to.Site == from.Site || !Closes(from.Start, to.Start, roads, out var saving) || saving <= best.Saving)
                        continue;
                    best = (from.Start, to.Start, roads, saving);
                }
            }
            if (best.Roads is not { } fromRoads)
                break;
            var origin = best.From;
            // It stops at the first road it meets that was a long way round, so it ends at a T, never across a road.
            var link = Route(new[] { (origin, -1) },
                tile => network.Contains(tile) && Closes(origin, tile, fromRoads, out _),
                best.To, allowed, Hug, directions, SearchLimit, ref budget, pad);
            if (link == null)
                break;
            Join(link, -1);
        }

        return new(true, null, new(), new()) { Centreline = network, Backbone = backbone };
    }

    /// <summary>The first allowed tile on a line out from the centre at <paramref name="angle"/>, from <paramref name="distance"/> on, stepping <paramref name="step"/>.</summary>
    private static Vector2i? Open(Vector2 centre, float angle, float distance, int step, Func<Vector2i, bool> allowed)
    {
        for (var tries = 0; tries < 32; tries++)
        {
            var at = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (distance + tries * step);
            var tile = new Vector2i((int) MathF.Floor(at.X), (int) MathF.Floor(at.Y));
            if (allowed(tile))
                return tile;
        }
        return null;
    }

    /// <summary>The widest gap between a set of angles round a circle; null with none.</summary>
    private static (float Middle, float Width)? Widest(List<float> angles)
    {
        if (angles.Count == 0)
            return null;
        var sorted = angles.Select(Normal).OrderBy(angle => angle).ToList();
        var best = (Middle: 0f, Width: -1f);
        for (var i = 0; i < sorted.Count; i++)
        {
            var from = sorted[i];
            var to = i + 1 < sorted.Count ? sorted[i + 1] : sorted[0] + MathF.Tau;
            if (to - from > best.Width)
                best = (from + (to - from) / 2f, to - from);
        }
        return best;
    }

    private static float Normal(float angle) => (angle % MathF.Tau + MathF.Tau) % MathF.Tau;

    private static float AngleBetween(float a, float b)
    {
        var difference = MathF.Abs(Normal(a) - Normal(b));
        return MathF.Min(difference, MathF.Tau - difference);
    }

    private static Vector2 Middle(Vector2i tile) => new(tile.X + 0.5f, tile.Y + 0.5f);

    private static float Area(Box2i box) => (float) (box.Right - box.Left) * (box.Top - box.Bottom);

    /// <summary>
    /// Steps along the roads from a tile to every tile it reaches, and the angle the way there turns about
    /// <paramref name="centre"/>.
    /// </summary>
    private static Dictionary<Vector2i, (int Steps, float Sweep)> Distances(HashSet<Vector2i> network, Vector2i from, Vector2 centre)
    {
        var distances = new Dictionary<Vector2i, (int Steps, float Sweep)> { [from] = (0, 0f) };
        var frontier = new Queue<Vector2i>();
        frontier.Enqueue(from);
        while (frontier.TryDequeue(out var tile))
        {
            var (steps, sweep) = distances[tile];
            foreach (var step in Cardinals)
            {
                var next = tile + step;
                if (!network.Contains(next) || distances.ContainsKey(next))
                    continue;
                distances[next] = (steps + 1, sweep + Turn(centre, Middle(tile), Middle(next)));
                frontier.Enqueue(next);
            }
        }
        return distances;
    }

    /// <summary>The signed angle from one point to another about a centre, the short way.</summary>
    private static float Turn(Vector2 centre, Vector2 from, Vector2 to)
    {
        var a = from - centre;
        var b = to - centre;
        return MathF.Atan2(a.X * b.Y - a.Y * b.X, Vector2.Dot(a, b));
    }

    /// <summary>
    /// The cheapest route from any start to a goal tile over allowed tiles. Straight steps go one tile, diagonal steps
    /// lay a two-tile staircase; bends and <paramref name="extra"/> cost more than going straight, and nothing turns
    /// sharper than square. A start's heading, or -1 for any, is the way it already faces. Null when none is found
    /// within the budgets.
    /// </summary>
    private static List<Vector2i>? Route(IReadOnlyList<(Vector2i Tile, int Heading)> starts, Func<Vector2i, bool> goal, Vector2i aim,
        Func<Vector2i, bool> allowed, Func<Vector2i, int> extra, int[] directions, int limit, ref int budget, Pad pad)
    {
        // The estimate counts double, and equal totals go to the state nearer the aim: a route a little dearer than the
        // best is found without trying every mix of bends along the way, or every corner of a pocket it must leave.
        var frontier = new PriorityQueue<(Vector2i Tile, int Heading), (int Cost, int Left, int Order)>();
        var costs = new Dictionary<(Vector2i, int), int>();
        var previous = new Dictionary<(Vector2i, int), (Vector2i, int)>();
        var order = 0;
        int Estimate(Vector2i tile)
        {
            var dx = Math.Abs(tile.X - aim.X);
            var dy = Math.Abs(tile.Y - aim.Y);
            var octile = StepCost * Math.Abs(dx - dy) + DiagonalCost * Math.Min(dx, dy);
            // Never less than the way round the landing zone, so the search doesn't flood the ground behind it.
            var around = Math.Max(octile, (int) (pad.Distance(tile, aim) * DiagonalCost / MathF.Sqrt(2f)));
            // An aim in the band about the landing zone costs at least the way in across it.
            around += (int) (MathF.Max(0f, pad.Toll(aim) - pad.Toll(tile)) * StepCost);
            return around * 2;
        }
        foreach (var (start, heading) in starts)
        {
            if (!costs.TryAdd((start, heading), 0))
                continue;
            frontier.Enqueue((start, heading), (Estimate(start), Estimate(start), order++));
        }

        var visited = 0;
        while (frontier.TryDequeue(out var state, out var priority))
        {
            var cost = costs[state];
            if (priority.Cost - Estimate(state.Tile) > cost)
                continue;
            if (goal(state.Tile))
            {
                var path = new List<Vector2i>();
                for (var at = state; ; )
                {
                    path.Add(at.Tile);
                    // A diagonal step came across first, then up or down.
                    if (at.Heading >= 4 && previous.ContainsKey(at))
                        path.Add(at.Tile - new Vector2i(0, Steps[at.Heading].Y));
                    if (!previous.TryGetValue(at, out var back))
                        break;
                    at = back;
                }
                path.Reverse();
                return path;
            }
            if (visited++ >= limit || budget-- <= 0)
                return null;

            foreach (var heading in directions)
            {
                var turn = 0;
                if (state.Heading >= 0)
                {
                    var eighths = Math.Abs(Octant[heading] - Octant[state.Heading]);
                    eighths = Math.Min(eighths, 8 - eighths);
                    if (eighths > 2)
                        continue;
                    turn = eighths == 0 ? 0 : eighths == 1 ? BendCost : TurnCost;
                }
                var step = Steps[heading];
                var next = state.Tile + step;
                var nextCost = cost + turn;
                if (heading >= 4)
                {
                    // Across, then up or down: the staircase stays joined edge to edge.
                    var across = state.Tile + new Vector2i(step.X, 0);
                    if (!allowed(across) || goal(across) || !allowed(next))
                        continue;
                    nextCost += DiagonalCost + extra(across) + extra(next);
                }
                else
                {
                    if (!allowed(next))
                        continue;
                    nextCost += StepCost + extra(next);
                }
                var key = (next, heading);
                if (costs.TryGetValue(key, out var old) && old <= nextCost)
                    continue;
                costs[key] = nextCost;
                previous[key] = state;
                var left = Estimate(next);
                frontier.Enqueue(key, (nextCost + left, left, order++));
            }
        }
        return null;
    }

    /// <summary>
    /// The tile of a set nearest another, the way round the landing zone and counting what it costs to reach one in
    /// the band about it; ties broken the same way wherever the world is. <paramref name="bestDistance"/> is
    /// <see cref="float.MaxValue"/> for an empty set.
    /// </summary>
    private static Vector2i Nearest(IEnumerable<Vector2i> tiles, Vector2i to, Pad pad, out float bestDistance)
    {
        var best = default(Vector2i);
        bestDistance = float.MaxValue;
        foreach (var tile in tiles)
        {
            var distance = pad.Distance(tile, to) + pad.Toll(tile);
            if (distance > bestDistance || distance == bestDistance && (tile.X > best.X || tile.X == best.X && tile.Y >= best.Y))
                continue;
            best = tile;
            bestDistance = distance;
        }
        return best;
    }

    /// <summary>
    /// The landing zone as an obstacle: distances between tiles go round <paramref name="Radius"/>, and a step within
    /// <paramref name="Band"/> of the middle costs <see cref="PadCost"/> more.
    /// </summary>
    private readonly record struct Pad(Vector2 Centre, float Radius, float Band)
    {
        /// <summary>What it costs, in straight steps, to come in from the band's edge to a tile within it.</summary>
        public float Toll(Vector2i tile) => MathF.Max(0f, Band - Vector2.Distance(Middle(tile), Centre)) * PadCost / StepCost;

        /// <summary>
        /// The shortest way between two tiles' middles that keeps out of the circle; a tile inside it counts from the
        /// nearest point of its edge, plus the way out.
        /// </summary>
        public float Distance(Vector2i from, Vector2i to)
        {
            var a = Middle(from);
            var b = Middle(to);
            if (Radius <= 0f)
                return Vector2.Distance(a, b);
            var outside = 0f;
            var da = a - Centre;
            var db = b - Centre;
            var ra = da.Length();
            var rb = db.Length();
            if (ra < Radius && ra > 0f)
            {
                outside += Radius - ra;
                da *= Radius / ra;
                ra = Radius;
                a = Centre + da;
            }
            if (rb < Radius && rb > 0f)
            {
                outside += Radius - rb;
                db *= Radius / rb;
                rb = Radius;
                b = Centre + db;
            }
            var straight = Vector2.Distance(a, b);
            if (straight <= 0f || ra <= 0f || rb <= 0f)
                return outside + straight;
            var t = Math.Clamp(Vector2.Dot(Centre - a, b - a) / (straight * straight), 0f, 1f);
            if (Vector2.Distance(a + (b - a) * t, Centre) >= Radius - 0.01f)
                return outside + straight;
            var between = MathF.Acos(Math.Clamp(Vector2.Dot(da, db) / (ra * rb), -1f, 1f));
            var arc = between - MathF.Acos(MathF.Min(1f, Radius / ra)) - MathF.Acos(MathF.Min(1f, Radius / rb));
            if (arc <= 0f)
                return outside + straight;
            return outside + MathF.Sqrt(MathF.Max(0f, ra * ra - Radius * Radius)) + MathF.Sqrt(MathF.Max(0f, rb * rb - Radius * Radius)) + Radius * arc;
        }
    }

    private static Vector2i Lowest(HashSet<Vector2i> tiles) => tiles.OrderBy(p => p.X).ThenBy(p => p.Y).First();

    /// <summary>The cardinally connected groups of a tile set.</summary>
    public static List<HashSet<Vector2i>> Components(IReadOnlySet<Vector2i> tiles)
    {
        var groups = new List<HashSet<Vector2i>>();
        var unseen = new HashSet<Vector2i>(tiles);
        while (unseen.Count > 0)
        {
            var first = unseen.First();
            unseen.Remove(first);
            var group = new HashSet<Vector2i> { first };
            var pending = new Queue<Vector2i>();
            pending.Enqueue(first);
            while (pending.TryDequeue(out var tile))
            {
                foreach (var step in Cardinals)
                {
                    if (!unseen.Remove(tile + step))
                        continue;
                    group.Add(tile + step);
                    pending.Enqueue(tile + step);
                }
            }
            groups.Add(group);
        }
        return groups;
    }

    private static bool Inside(Vector2i p, Box2i bounds) => p.X >= bounds.Left && p.X < bounds.Right && p.Y >= bounds.Bottom && p.Y < bounds.Top;

    /// <summary>Converts an entrance direction to its unit tile offset.</summary>
    public static Vector2i Direction(WFPlanetPoiDirection direction) => direction switch
    {
        WFPlanetPoiDirection.North => new(0, 1),
        WFPlanetPoiDirection.East => new(1, 0),
        WFPlanetPoiDirection.South => new(0, -1),
        WFPlanetPoiDirection.West => new(-1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };
}
