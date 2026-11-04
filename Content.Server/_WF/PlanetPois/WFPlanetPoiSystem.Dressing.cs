using System.Linq;
using System.Threading.Tasks;
using System.Numerics;
using Content.Shared._WF.PlanetPois;
using Content.Shared.Decals;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.PlanetPois;

/// <summary>Furnishes painted roads: kerbs, lane lines, street lights and scattered traffic.</summary>
public sealed partial class WFPlanetPoiSystem
{
    /// <summary>Tiles of road a blocking piece keeps from every bend, junction and end.</summary>
    private const int ScatterClear = 5;

    /// <summary>Tiles two blocking pieces keep between them, so the road is never shut by a pair.</summary>
    private const int BlockerGap = 7;

    private const int AttemptsPerPiece = 12;

    /// <summary>Lays a network's dressing over its painted tiles. Nothing goes inside a site's own bounds.</summary>
    private async Task DressRoadsAsync(EntityUid ground, MapGridComponent grid, WFPlanetPoiComponent record, WFPlanetRoadNetwork roads,
        WFPlanetRoadPlan plan, HashSet<EntityUid> created, List<uint> createdDecals)
    {
        if (roads.Dressing is not { } id)
            return;

        if (!_proto.TryIndex(id, out var dressing))
        {
            Log.Error($"Unknown road dressing {id}; the roads of {ToPrettyString(ground)} stay bare.");
            return;
        }

        var random = new System.Random(_random.Next());
        var half = roads.Width / 2;
        var centreline = plan.Centreline;
        var straight = WFRoadDressingPlanner.StraightRuns(centreline, 0);
        var open = WFRoadDressingPlanner.StraightRuns(centreline, ScatterClear).OrderBy(pair => pair.Key.X).ThenBy(pair => pair.Key.Y).ToList();
        var occupied = new HashSet<Vector2i>();

        bool InSite(Vector2i tile) => record.Sites.Any(site => Contains(site.Bounds, tile));

        // Kerbs: each shoulder tile shows its edge to the road.
        var kerbs = new List<(Vector2i, Tile)>();
        foreach (var tile in plan.ShoulderTiles)
        {
            if (!InSite(tile)
                && WFRoadDressingPlanner.KerbOf(plan.RoadTiles, tile) is { } kerb
                && dressing.Kerbs.TryGetValue(kerb, out var kerbTile))
                kerbs.Add((tile, new Tile(_tiles[kerbTile.Id].TileId)));
        }

        _maps.SetTiles(ground, grid, kerbs);
        await _preload.Breathe();

        // The line down the middle.
        EnsureComp<DecalGridComponent>(ground);
        foreach (var (tile, axis) in straight)
        {
            var line = axis == WFRoadAxis.NorthSouth ? dressing.LineNorthSouth : dressing.LineEastWest;
            var along = axis == WFRoadAxis.NorthSouth ? tile.Y : tile.X;
            if (line == null || InSite(tile) || dressing.LineEvery < 1 || along % dressing.LineEvery != 0)
                continue;

            AddDecal(ground, line.Value, tile, createdDecals);
            await _preload.Breathe();
        }

        // Street lights, clear of the corners the signals take.
        if (dressing.Light is { } light)
        {
            var lit = WFRoadDressingPlanner.StraightRuns(centreline, half + 2);
            foreach (var tile in WFRoadDressingPlanner.Lights(lit, plan.ShoulderTiles, half, dressing.LightSpacing))
            {
                if (InSite(tile) || !occupied.Add(tile))
                    continue;

                var broken = dressing.BrokenLight != null && random.NextDouble() < dressing.BrokenLightChance;
                await _preload.Breathe();
                if (TerminatingOrDeleted(ground))
                    return;
                Furnish(ground, record, broken ? dressing.BrokenLight!.Value : light, tile, Angle.Zero, created);
            }
        }

        // Signals where roads meet, on two facing corners.
        if (dressing.JunctionLight is { } signal)
        {
            foreach (var tile in centreline.OrderBy(tile => tile.X).ThenBy(tile => tile.Y))
            {
                if (!WFRoadDressingPlanner.IsJunction(centreline, tile))
                    continue;

                foreach (var corner in new[] { new Vector2i(half + 1, half + 1), new Vector2i(-half - 1, -half - 1) })
                {
                    var spot = tile + corner;
                    if (!plan.ShoulderTiles.Contains(spot) || InSite(spot) || !occupied.Add(spot))
                        continue;
                    await _preload.Breathe();
                    if (TerminatingOrDeleted(ground))
                        return;
                    Furnish(ground, record, signal, spot, Angle.Zero, created);
                }
            }
        }

        if (open.Count == 0)
            return;

        // Scattered pieces: parked and wrecked traffic in the lanes, furniture on the shoulders.
        var blockers = new List<Vector2i>();
        foreach (var rule in dressing.Scatter)
        {
            if (rule.Prototypes.Count == 0)
                continue;

            var wanted = Count(rule.Per100, open.Count, random);
            for (var attempt = 0; attempt < wanted * AttemptsPerPiece && wanted > 0; attempt++)
            {
                await _preload.Breathe();
                if (TerminatingOrDeleted(ground))
                    return;
                var (centre, axis) = open[random.Next(open.Count)];
                if (rule.Axis != WFRoadAxis.Any && rule.Axis != axis)
                    continue;

                var side = random.Next(2) == 0 ? 1 : -1;
                var angle = !rule.Align ? Angle.Zero
                    : Angle.FromDegrees((axis == WFRoadAxis.NorthSouth ? 0 : 90) + (random.Next(2) == 0 ? 0 : 180));
                var proto = rule.Prototypes[random.Next(rule.Prototypes.Count)];
                var bounds = BoundsOf(proto, angle);
                var allowed = rule.Where == WFRoadSpot.Shoulder ? plan.ShoulderTiles : plan.RoadTiles;

                // Against the kerb means the outermost lane it fits in: a piece two lanes wide takes the outer two.
                var first = rule.Where switch
                {
                    WFRoadSpot.Lane => random.Next(1, half + 1),
                    WFRoadSpot.Kerb => half,
                    _ => half + random.Next(1, roads.ShoulderWidth + 1),
                };
                var last = rule.Where == WFRoadSpot.Kerb ? 1 : first;
                Vector2i? found = null;
                List<Vector2i>? tiles = null;
                for (var offset = first; offset >= last && found == null; offset--)
                {
                    var spot = centre + WFRoadDressingPlanner.Across(axis) * (side * offset);
                    var covered = WFRoadDressingPlanner.Footprint(bounds, spot).ToList();
                    if (covered.Any(tile => !allowed.Contains(tile) || centreline.Contains(tile) || occupied.Contains(tile) || InSite(tile))
                        || rule.Where == WFRoadSpot.Kerb && !covered.Any(tile => WFRoadDressingPlanner.TouchesAny(tile, plan.ShoulderTiles)))
                        continue;

                    found = spot;
                    tiles = covered;
                }

                if (found is not { } origin || tiles == null
                    || rule.Blocks && blockers.Any(other => Math.Max(Math.Abs(other.X - centre.X), Math.Abs(other.Y - centre.Y)) < BlockerGap))
                    continue;

                occupied.UnionWith(tiles);
                if (rule.Blocks)
                    blockers.Add(centre);

                Furnish(ground, record, proto, origin, angle, created);
                wanted--;
            }
        }

        foreach (var rule in dressing.Decals)
        {
            if (rule.Decals.Count == 0)
                continue;

            var wanted = Count(rule.Per100, open.Count, random);
            for (var attempt = 0; attempt < wanted * AttemptsPerPiece && wanted > 0; attempt++)
            {
                var (centre, axis) = open[random.Next(open.Count)];
                if (rule.Axis != WFRoadAxis.Any && rule.Axis != axis)
                    continue;

                var offset = rule.Where switch
                {
                    WFRoadSpot.Lane => random.Next(-half, half + 1),
                    WFRoadSpot.Kerb => half * (random.Next(2) == 0 ? 1 : -1),
                    _ => (half + random.Next(1, roads.ShoulderWidth + 1)) * (random.Next(2) == 0 ? 1 : -1),
                };
                var tile = centre + WFRoadDressingPlanner.Across(axis) * offset;
                var allowed = rule.Where == WFRoadSpot.Shoulder ? plan.ShoulderTiles : plan.RoadTiles;
                if (!allowed.Contains(tile) || InSite(tile))
                    continue;

                AddDecal(ground, rule.Decals[random.Next(rule.Decals.Count)], tile, createdDecals);
                wanted--;
                await _preload.Breathe();
                if (TerminatingOrDeleted(ground))
                    return;
            }
        }
    }

    /// <summary>How many of a scatter rule a length of road gets: its rate, with the odd fraction rolled.</summary>
    private static int Count(float per100, int length, System.Random random)
    {
        var exact = Math.Max(0f, per100) * length / 100f;
        var whole = (int) exact;
        return whole + (random.NextDouble() < exact - whole ? 1 : 0);
    }

    private void Furnish(EntityUid ground, WFPlanetPoiComponent record, EntProtoId proto, Vector2i tile, Angle angle,
        HashSet<EntityUid> created)
    {
        var uid = Spawn(proto, new EntityCoordinates(ground, new Vector2(tile.X + 0.5f, tile.Y + 0.5f)));
        if (angle != Angle.Zero)
            _transform.SetLocalRotation(uid, angle);

        created.Add(uid);
        record.Furniture.Add(uid);
    }

    /// <summary>
    /// Clears the road furniture inside a site laid after the roads were dressed (a wreck, a camp): nothing of the
    /// roads' stands inside a site's bounds.
    /// </summary>
    private void ClearFurnitureInside(WFPlanetPoiComponent record, Box2i bounds)
    {
        for (var i = record.Furniture.Count - 1; i >= 0; i--)
        {
            var uid = record.Furniture[i];
            if (TerminatingOrDeleted(uid))
            {
                record.Furniture.RemoveAt(i);
                continue;
            }
            var tile = Transform(uid).LocalPosition.Floored();
            if (!Contains(bounds, tile))
                continue;
            QueueDel(uid);
            record.Furniture.RemoveAt(i);
        }
    }

    private void AddDecal(EntityUid ground, ProtoId<DecalPrototype> decal, Vector2i tile, List<uint> createdDecals)
    {
        if (_decals.TryAddDecal(decal.Id, new EntityCoordinates(ground, tile), out var id))
            createdDecals.Add(id);
    }

    /// <summary>The bounds of a prototype's fixtures about its own centre, turned by an angle; a small box when it has none.</summary>
    private Box2 BoundsOf(EntProtoId proto, Angle angle)
    {
        Box2? bounds = null;
        if (_proto.Index(proto).TryGetComponent<FixturesComponent>(out var fixtures, EntityManager.ComponentFactory))
        {
            var transform = new Transform(Vector2.Zero, angle);
            foreach (var fixture in fixtures.Fixtures.Values)
            {
                for (var child = 0; child < fixture.Shape.ChildCount; child++)
                {
                    var box = fixture.Shape.ComputeAABB(transform, child);
                    bounds = bounds?.Union(box) ?? box;
                }
            }
        }

        return bounds ?? Box2.CenteredAround(Vector2.Zero, new Vector2(0.5f, 0.5f));
    }
}
