using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.PlanetCities;
using Content.Shared._WF.PlanetPois;
using Content.Shared.EntityTable;
using Content.Shared.Gravity;
using Content.Shared.Light.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Content.Shared.Physics;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;

namespace Content.Server._WF.PlanetPois;

/// <summary>
/// Crashed ships: a shipyard hull, torn open where it hit, lying at whatever angle it came to rest off the roads, with
/// the world creeping over its deck and its wildlife living inside.
/// </summary>
public sealed partial class WFPlanetPoiSystem
{
    [Dependency] private EntityTableSystem _entityTables = default!;

    private const string WreckName = "wf-poi-wreck";
    private const int WreckAttempts = 64;

    /// <summary>Tiles kept between a wreck and anything pinned: sites, roads, cavern mouths.</summary>
    private const float WreckClearance = 3f;

    /// <summary>Tiles between the spots tried when random tries find nowhere for a wreck or camp.</summary>
    private const int WreckSweepStep = 6;

    private async Task PlaceWrecksAsync(EntityUid ground, MapGridComponent grid, BiomeComponent biome, EntityUid orbit, Vector2 centre, float radius,
        WFPlanetPoiTablePrototype table, WFPlanetPoiComponent record, HashSet<EntityUid> created)
    {
        if (table.Wrecks is not { } wrecks)
            return;
        if (!_proto.TryIndex(wrecks.Theme, out var theme))
        {
            Log.Error($"Unknown wreck theme {wrecks.Theme} in {table.ID}.");
            return;
        }

        var pool = _proto.EnumeratePrototypes<VesselPrototype>()
            .Where(vessel => !vessel.Abstract && vessel.Purchasable && wrecks.Sizes.Contains(vessel.Category) && vessel.ShuttlePath != default)
            .OrderBy(vessel => vessel.ID)
            .ToList();
        if (pool.Count == 0)
            return;

        var count = _random.Next(wrecks.MinCount, wrecks.MaxCount + 1);
        for (var i = 0; i < count; i++)
        {
            await _preload.Breathe();
            if (TerminatingOrDeleted(ground))
                return;
            var vessel = _random.Pick(pool);
            if (!await TryPlaceWreckAsync(ground, grid, biome, orbit, centre, radius, table, record, wrecks, theme, vessel, created))
                Log.Warning($"No wreck of {vessel.ID} on {ToPrettyString(ground)}; it is skipped.");
        }
    }

    private async Task<bool> TryPlaceWreckAsync(EntityUid ground, MapGridComponent grid, BiomeComponent biome, EntityUid orbit, Vector2 centre, float radius,
        WFPlanetPoiTablePrototype table, WFPlanetPoiComponent record, WFPlanetWrecks wrecks, WFWreckThemePrototype theme, VesselPrototype vessel,
        HashSet<EntityUid> created)
    {
        var stagingMap = _maps.CreateMap(out var stagingId, runMapInit: false);
        try
        {
            if (await LoadGridAsync(stagingId, vessel.ShuttlePath) is not { } ship || TerminatingOrDeleted(ground))
                return false;
            var hull = ship.Comp.LocalAABB;
            var reach = hull.Size.Length() / 2f;
            if (!TryWreckSpot((ground, biome), grid, centre, radius, table, reach, out var spot, wrecks.EdgeMargin))
                return false;

            WreckHull(ship, theme, wrecks.Breach);

            // The hull comes to rest at any angle, its middle on the spot.
            var angle = _random.NextAngle();
            var targetMap = Transform(ground).MapUid ?? ground;
            var world = _transform.ToMapCoordinates(new EntityCoordinates(ground, spot)).Position - angle.RotateVec(hull.Center);
            _transform.SetCoordinates(ship.Owner, new EntityCoordinates(targetMap, world));
            _transform.SetLocalRotation(ship.Owner, angle);
            created.Add(ship.Owner);
            await InitializeTransferredAsync(ship.Owner, _maps.IsInitialized(targetMap), _maps.IsPaused(targetMap));
            if (TerminatingOrDeleted(ground) || TerminatingOrDeleted(ship))
                return false;
            SettleWreck(ship);

            // Clear and pin the ground under it, so nothing the biome grows comes up through the deck.
            var under = new HashSet<Vector2i>();
            var anchored = new List<EntityUid>();
            for (var x = (int) MathF.Floor(spot.X - reach); x <= (int) MathF.Ceiling(spot.X + reach); x++)
            for (var y = (int) MathF.Floor(spot.Y - reach); y <= (int) MathF.Ceiling(spot.Y + reach); y++)
            {
                var tile = new Vector2i(x, y);
                if (Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), spot) > reach)
                    continue;
                under.Add(tile);
                anchored.Clear();
                var enumerator = _maps.GetAnchoredEntitiesEnumerator(ground, grid, tile);
                while (enumerator.MoveNext(out var uid))
                    anchored.Add(uid.Value);
                foreach (var uid in anchored)
                    QueueDel(uid);
            }

            // Ground the biome hasn't laid yet would stay empty once pinned: lay it now.
            var fill = new List<(Vector2i, Tile)>();
            foreach (var tile in under)
            {
                if (_maps.GetTileRef(ground, grid, tile).Tile.IsEmpty && _biome.TryGetBiomeTile(ground, grid, tile, out var natural))
                    fill.Add((tile, natural.Value));
            }
            if (fill.Count > 0)
                _maps.SetTiles(ground, grid, fill);
            _biome.WfPinTiles((ground, biome), under);

            await _preload.Breathe();
            if (TerminatingOrDeleted(ground) || TerminatingOrDeleted(ship))
                return false;
            await SpawnWreckFaunaAsync(ship, theme);
            if (TerminatingOrDeleted(ground) || TerminatingOrDeleted(ship))
                return false;

            var origin = spot.Floored();
            var bounds = new Box2i((int) MathF.Floor(spot.X - reach), (int) MathF.Floor(spot.Y - reach),
                (int) MathF.Ceiling(spot.X + reach) + 1, (int) MathF.Ceiling(spot.Y + reach) + 1);
            var site = new WFPlanetPoiSite
            {
                Name = WreckName,
                Origin = origin,
                Bounds = bounds,
                TileCount = under.Count,
                Connection = WFPlanetPoiConnection.Isolated,
                Expeditions = false,
                Difficulty = table.Difficulty,
            };
            site.Signal = SpawnSignal(ground, orbit, origin, new WFPlanetPoiEntry { Name = WreckName }, table);
            created.Add(site.Signal);
            record.Sites.Add(site);
            ClearFurnitureInside(record, bounds);
            return true;
        }
        finally
        {
            if (!TerminatingOrDeleted(stagingMap))
                QueueDel(stagingMap);
        }
    }

    /// <summary>
    /// Somewhere off the roads, clear of the sites and the landing zone, with nothing pinned under the hull; between the
    /// landing zone and <paramref name="edgeMargin"/> (the table's when null) of the edge.
    /// </summary>
    private bool TryWreckSpot(Entity<BiomeComponent> ground, MapGridComponent grid, Vector2 centre, float radius, WFPlanetPoiTablePrototype table,
        float reach, out Vector2 spot, int? edgeMargin = null)
    {
        spot = default;
        var inner = table.CentreClear + reach + WreckClearance;
        var outer = radius - (edgeMargin ?? table.EdgeMargin) - reach;
        if (outer <= inner)
            return false;

        var clear = reach + WreckClearance;
        bool Free(Vector2 candidate)
        {
            for (var x = (int) MathF.Floor(candidate.X - clear); x <= (int) MathF.Ceiling(candidate.X + clear); x++)
            for (var y = (int) MathF.Floor(candidate.Y - clear); y <= (int) MathF.Ceiling(candidate.Y + clear); y++)
            {
                if (Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), candidate) <= clear
                    && (_biome.WfIsPinned(ground, new Vector2i(x, y)) || _biome.WfIsWater(ground, new Vector2i(x, y))))
                    return false;
            }

            return true;
        }

        for (var attempt = 0; attempt < WreckAttempts; attempt++)
        {
            spot = centre + _random.NextAngle().ToVec() * MathF.Sqrt(_random.NextFloat(inner * inner, outer * outer));
            if (Free(spot))
                return true;
        }

        // Lakes and roads can leave only a few gaps big enough, which random tries miss: sweep the whole ring.
        var sweep = new List<Vector2>();
        for (var x = -outer; x <= outer; x += WreckSweepStep)
        for (var y = -outer; y <= outer; y += WreckSweepStep)
        {
            var offset = new Vector2(x, y);
            if (offset.LengthSquared() is var distance && distance >= inner * inner && distance <= outer * outer)
                sweep.Add(centre + offset);
        }

        _random.Shuffle(sweep);
        foreach (var candidate in sweep)
        {
            if (!Free(candidate))
                continue;
            spot = candidate;
            return true;
        }

        return false;
    }

    /// <summary>Tears the hull open where it hit, lets the world creep over the deck and grow on it.</summary>
    private void WreckHull(Entity<MapGridComponent> ship, WFWreckThemePrototype theme, float breach)
    {
        ship.Comp.CanSplit = false;
        var tiles = _maps.GetAllTiles(ship.Owner, ship.Comp).Select(t => t.GridIndices).ToHashSet();
        if (tiles.Count == 0)
            return;

        // The breach: a ragged bite out of the hull's edge.
        var edge = tiles.Where(t => !tiles.Contains(t + Vector2i.Up) || !tiles.Contains(t + Vector2i.Down)
                                     || !tiles.Contains(t + Vector2i.Left) || !tiles.Contains(t + Vector2i.Right)).ToList();
        var hull = ship.Comp.LocalAABB;
        var bite = MathF.Max(2f, breach * MathF.Min(hull.Width, hull.Height));
        var impact = (Vector2) _random.Pick(edge) + new Vector2(0.5f);
        var torn = tiles.Where(t =>
        {
            var distance = Vector2.Distance((Vector2) t + new Vector2(0.5f), impact);
            return distance <= bite || distance <= bite + 1.5f && _random.Prob(0.5f);
        }).ToHashSet();

        var children = new List<EntityUid>();
        var enumerator = Transform(ship.Owner).ChildEnumerator;
        while (enumerator.MoveNext(out var child))
            children.Add(child);
        foreach (var child in children)
        {
            if (torn.Contains(Transform(child).LocalPosition.Floored()) || HasComp<ShuttleConsoleComponent>(child))
                Del(child);
        }
        _maps.SetTiles(ship.Owner, ship.Comp, torn.Select(t => (t, Tile.Empty)).ToList());
        tiles.ExceptWith(torn);

        var occupied = SolidTiles(ship);

        if (theme.FloorTile is { } floorId && _tiles.TryGetDefinition(floorId, out var floor))
        {
            var open = tiles.Where(t => !occupied.Contains(t)).ToList();
            var creep = open.Where(_ => _random.Prob(theme.FloorChance)).Select(t => (t, new Tile(floor.TileId))).ToList();
            // However the rolls fall, the world has got in somewhere.
            if (creep.Count == 0 && (open.Count > 0 ? open : tiles.ToList()) is { Count: > 0 } any)
                creep.Add((_random.Pick(any), new Tile(floor.TileId)));
            _maps.SetTiles(ship.Owner, ship.Comp, creep);
        }

        if (theme.Overgrowth.Count > 0)
        {
            foreach (var tile in tiles)
            {
                if (occupied.Contains(tile) || !_random.Prob(theme.OvergrowthChance))
                    continue;
                Spawn(_random.Pick(theme.Overgrowth), new EntityCoordinates(ship.Owner, (Vector2) tile + new Vector2(0.5f)));
            }
        }
    }

    /// <summary>A wreck is ground now: it doesn't fly, fall, belong to anyone or count as a vessel, and it has gravity and a roof.</summary>
    private void SettleWreck(Entity<MapGridComponent> ship)
    {
        RemComp<ShuttleComponent>(ship);
        RemComp<ShuttleDeedComponent>(ship);
        RemComp<BecomesStationComponent>(ship);
        EnsureComp<CEZMappingAnchorGridComponent>(ship);
        EnsureComp<WFPlanetCityGridComponent>(ship);
        _physics.SetBodyType(ship, BodyType.Static);

        var gravity = EnsureComp<GravityComponent>(ship);
        gravity.Enabled = true;
        gravity.Inherent = true;
        Dirty(ship, gravity);

        EnsureComp<RoofComponent>(ship);
        foreach (var tile in _maps.GetAllTiles(ship.Owner, ship.Comp))
            _roof.SetRoof((ship.Owner, ship.Comp, null), tile.GridIndices, true);
    }

    /// <summary>
    /// Deck under walls and solid machines, every tile a big one such as a ship gun covers; cables and pipes under the
    /// deck don't count.
    /// </summary>
    internal HashSet<Vector2i> SolidTiles(Entity<MapGridComponent> ship)
    {
        // Slightly inside a fixture's edge, so a wall's box doesn't claim the tiles beside it.
        const float inset = 0.05f;
        var solid = new HashSet<Vector2i>();
        var enumerator = Transform(ship.Owner).ChildEnumerator;
        while (enumerator.MoveNext(out var child))
        {
            var xform = Transform(child);
            if (!xform.Anchored || !TryComp(child, out FixturesComponent? fixtures))
                continue;

            var pose = new Robust.Shared.Physics.Transform(xform.LocalPosition, xform.LocalRotation);
            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (!fixture.Hard || (fixture.CollisionLayer & (int) CollisionGroup.MidImpassable) == 0)
                    continue;

                solid.Add(xform.LocalPosition.Floored());
                var box = fixture.Shape.ComputeAABB(pose, 0);
                for (var x = (int) MathF.Floor(box.Left + inset); x <= (int) MathF.Floor(box.Right - inset); x++)
                for (var y = (int) MathF.Floor(box.Bottom + inset); y <= (int) MathF.Floor(box.Top - inset); y++)
                    solid.Add(new Vector2i(x, y));
            }
        }
        return solid;
    }

    /// <summary>The wildlife that moved in, what's left in the hold and what's left of the crew.</summary>
    private async Task SpawnWreckFaunaAsync(Entity<MapGridComponent> ship, WFWreckThemePrototype theme)
    {
        if (theme.Fauna is not { } fauna)
            return;
        var solid = SolidTiles(ship);
        var open = _maps.GetAllTiles(ship.Owner, ship.Comp).Select(t => t.GridIndices).Where(t => !solid.Contains(t)).ToList();
        if (open.Count == 0)
            return;

        var count = _random.Next(theme.MinFauna, theme.MaxFauna + 1);
        for (var i = 0; i < count; i++)
        {
            foreach (var id in _entityTables.GetSpawns(fauna))
            {
                await _preload.Breathe();
                if (TerminatingOrDeleted(ship))
                    return;
                Spawn(id, new EntityCoordinates(ship.Owner, (Vector2) _random.Pick(open) + new Vector2(0.5f)));
            }
        }

        if (theme.Loot is { } loot && _random.Prob(theme.LootChance))
        {
            foreach (var id in _entityTables.GetSpawns(loot))
                Spawn(id, new EntityCoordinates(ship.Owner, (Vector2) _random.Pick(open) + new Vector2(0.5f)));
        }

        if (theme.Crew is not { } crew)
            return;
        var survivors = _random.Next(theme.MinCrew, theme.MaxCrew + 1);
        for (var i = 0; i < survivors; i++)
        {
            foreach (var id in _entityTables.GetSpawns(crew))
            {
                await _preload.Breathe();
                if (TerminatingOrDeleted(ship))
                    return;
                Spawn(id, new EntityCoordinates(ship.Owner, (Vector2) _random.Pick(open) + new Vector2(0.5f)));
            }
        }
    }
}
