using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.Strangers;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.PlanetPois;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Roles;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.PlanetPois;

/// <summary>The hull an armed camp is built round.</summary>
[RegisterComponent]
public sealed partial class WFPlanetCampComponent : Component
{
    [ViewVariables]
    public ProtoId<WFPlanetCampPrototype> Camp;
}

/// <summary>
/// Armed camps: a ship set down intact off the roads, its crew standing guard on the deck and round the hull behind a
/// ring of cover. The deck guards come aboard with the ship; the ones outside, and their cover, wait until the ground
/// round it exists.
/// </summary>
public sealed partial class WFPlanetPoiSystem
{
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private NpcFactionSystem _factions = default!;

    private int _campCount;

    /// <summary>Tiles of camp round a hull: guards, cover, tents and fires, all on cleared ground.</summary>
    private const float CampRing = 8f;

    /// <summary>Tiles a roadside camp keeps along the road from its bends, junctions and ends.</summary>
    private const int CampRoadClear = 16;

    /// <summary>Stretches of road tried for a roadside camp before it goes out in the open instead.</summary>
    private const int RoadsideAttempts = 64;

    /// <summary>Hulls picked for a camp before it is left out.</summary>
    private const int CampAttempts = 4;

    private async Task PlaceCampsAsync(EntityUid ground, MapGridComponent grid, BiomeComponent biome, EntityUid orbit, Vector2 centre, float radius,
        WFPlanetPoiTablePrototype table, WFPlanetPoiComponent record, HashSet<EntityUid> created)
    {
        foreach (var entry in table.Camps)
        {
            if (!_proto.TryIndex(entry.Camp, out var camp))
            {
                Log.Error($"Unknown camp {entry.Camp} in {table.ID}.");
                continue;
            }

            var count = _random.Next(entry.MinCount, entry.MaxCount + 1);
            for (var i = 0; i < count; i++)
            {
                await _preload.Breathe();
                if (TerminatingOrDeleted(ground))
                    return;
                // A smaller hull of the camp's may fit where the one picked did not.
                var placed = false;
                for (var attempt = 0; attempt < CampAttempts && !placed; attempt++)
                    placed = await TryPlaceCampAsync(ground, grid, biome, orbit, centre, radius, table, record, camp, created);
                if (!placed)
                    Log.Warning($"No {camp.ID} on {ToPrettyString(ground)}; it is skipped.");
            }
        }
    }

    private async Task<bool> TryPlaceCampAsync(EntityUid ground, MapGridComponent grid, BiomeComponent biome, EntityUid orbit, Vector2 centre, float radius,
        WFPlanetPoiTablePrototype table, WFPlanetPoiComponent record, WFPlanetCampPrototype camp, HashSet<EntityUid> created)
    {
        if (camp.Vessels.Count == 0)
            return TryPlaceOpenCamp(ground, grid, biome, orbit, centre, radius, table, record, camp, created);
        if (!_proto.TryIndex(_random.Pick(camp.Vessels), out var vessel))
            return false;

        var stagingMap = _maps.CreateMap(out var stagingId, runMapInit: false);
        try
        {
            if (await LoadGridAsync(stagingId, vessel.ShuttlePath) is not { } ship || TerminatingOrDeleted(ground))
                return false;
            var hull = ship.Comp.LocalAABB;
            // Room round the hull for the guards and their cover.
            var reach = hull.Size.Length() / 2f;
            var camped = reach + CampRing;
            var spot = Vector2.Zero;
            var toRoad = Vector2i.Zero;
            var roadside = camp.Roadside && TryRoadsideSpot((ground, biome), centre, radius, table, record, camped, out spot, out toRoad);
            if (!roadside && !TryWreckSpot((ground, biome), grid, centre, radius, table, camped, out spot, table.Wrecks?.EdgeMargin))
                return false;

            // Set down square to the world, the right way up; beside a road, nose to it.
            var angle = roadside ? new Angle(MathF.Atan2(-toRoad.X, toRoad.Y)) : Angle.FromDegrees(90 * _random.Next(4));
            var targetMap = Transform(ground).MapUid ?? ground;
            var world = _transform.ToMapCoordinates(new EntityCoordinates(ground, spot)).Position - angle.RotateVec(hull.Center);
            _transform.SetCoordinates(ship.Owner, new EntityCoordinates(targetMap, world));
            _transform.SetLocalRotation(ship.Owner, angle);
            ship.Comp.CanSplit = false;
            created.Add(ship.Owner);
            await InitializeTransferredAsync(ship.Owner, _maps.IsInitialized(targetMap), _maps.IsPaused(targetMap));
            if (TerminatingOrDeleted(ground) || TerminatingOrDeleted(ship))
                return false;
            SettleWreck(ship);
            EnsureComp<WFPlanetCampComponent>(ship).Camp = camp.ID;

            var under = ClearCampGround(ground, grid, biome, spot, camped);

            var group = $"{camp.ID}-{++_campCount}";
            var deck = _maps.GetAllTiles(ship.Owner, ship.Comp).Select(t => t.GridIndices).Except(SolidTiles(ship)).ToList();
            if (deck.Count > 0)
            {
                var guards = camp.Profile == null ? 0 : _random.Next(camp.MinDeckGuards, camp.MaxDeckGuards + 1);
                for (var i = 0; i < guards; i++)
                {
                    await _preload.Breathe();
                    if (TerminatingOrDeleted(ship))
                        return false;
                    SpawnCampGuard(camp, new EntityCoordinates(ship.Owner, (Vector2) _random.Pick(deck) + new Vector2(0.5f)), group);
                }

                if (camp.Loot is { } loot)
                {
                    foreach (var id in _entityTables.GetSpawns(loot))
                        created.Add(Spawn(id, new EntityCoordinates(ship.Owner, (Vector2) _random.Pick(deck) + new Vector2(0.5f))));
                }
            }

            record.PendingCamps.Add(new WFPendingCamp(camp.ID, spot, reach, group));

            AddCampSite(ground, orbit, table, record, camp, spot, camped, under.Count, created);
            return true;
        }
        finally
        {
            if (!TerminatingOrDeleted(stagingMap))
                QueueDel(stagingMap);
        }
    }

    /// <summary>A camp in the open, round a fire: no ship, its folk and their tents set out once the ground exists.</summary>
    private bool TryPlaceOpenCamp(EntityUid ground, MapGridComponent grid, BiomeComponent biome, EntityUid orbit, Vector2 centre,
        float radius, WFPlanetPoiTablePrototype table, WFPlanetPoiComponent record, WFPlanetCampPrototype camp, HashSet<EntityUid> created)
    {
        const float reach = 1f;
        var camped = reach + CampRing;
        var spot = Vector2.Zero;
        if (!(camp.Roadside && TryRoadsideSpot((ground, biome), centre, radius, table, record, camped, out spot, out _))
            && !TryWreckSpot((ground, biome), grid, centre, radius, table, camped, out spot, table.Wrecks?.EdgeMargin))
            return false;
        var under = ClearCampGround(ground, grid, biome, spot, camped);
        record.PendingCamps.Add(new WFPendingCamp(camp.ID, spot, reach, $"{camp.ID}-{++_campCount}"));
        AddCampSite(ground, orbit, table, record, camp, spot, camped, under.Count, created);
        return true;
    }

    /// <summary>
    /// Somewhere just off a road, beside a straight stretch well clear of its bends, junctions and tunnels, with nothing
    /// pinned or wet in the camp's ring, so the road and its shoulder stay outside it; and the way from there to the road.
    /// </summary>
    private bool TryRoadsideSpot(Entity<BiomeComponent> ground, Vector2 centre, float radius, WFPlanetPoiTablePrototype table,
        WFPlanetPoiComponent record, float camped, out Vector2 spot, out Vector2i toRoad)
    {
        spot = default;
        toRoad = default;
        if (table.Roads is not { } roads || record.Centreline.Count == 0)
            return false;

        var tunnels = record.Tunnels.SelectMany(tunnel => tunnel).ToHashSet();
        var straight = WFRoadDressingPlanner.StraightRuns(record.Centreline, CampRoadClear)
            .Where(pair => !tunnels.Contains(pair.Key))
            .OrderBy(pair => pair.Key.X).ThenBy(pair => pair.Key.Y)
            .ToList();
        if (straight.Count == 0)
            return false;

        // The ring's edge a tile off the shoulder's.
        var off = roads.Width / 2 + roads.ShoulderWidth + camped + 2f;
        for (var attempt = 0; attempt < RoadsideAttempts; attempt++)
        {
            var (tile, axis) = _random.Pick(straight);
            var across = WFRoadDressingPlanner.Across(axis) * (_random.Prob(0.5f) ? 1 : -1);
            var candidate = new Vector2(tile.X + 0.5f, tile.Y + 0.5f) + (Vector2) across * off;
            var distance = Vector2.Distance(candidate, centre);
            if (distance + camped > radius - 2f || distance - camped < table.CentreClear || !CampGroundFree(ground, candidate, camped + 1f))
                continue;
            spot = candidate;
            toRoad = -across;
            return true;
        }
        return false;
    }

    /// <summary>Whether nothing is pinned and no water lies within a circle.</summary>
    private bool CampGroundFree(Entity<BiomeComponent> ground, Vector2 spot, float clear)
    {
        for (var x = (int) MathF.Floor(spot.X - clear); x <= (int) MathF.Ceiling(spot.X + clear); x++)
        for (var y = (int) MathF.Floor(spot.Y - clear); y <= (int) MathF.Ceiling(spot.Y + clear); y++)
        {
            var tile = new Vector2i(x, y);
            if (Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), spot) <= clear && (_biome.WfIsPinned(ground, tile) || _biome.WfIsWater(ground, tile)))
                return false;
        }
        return true;
    }

    /// <summary>The ground under a camp: whatever grew there cleared, the world's own tiles laid, and all of it pinned.</summary>
    private HashSet<Vector2i> ClearCampGround(EntityUid ground, MapGridComponent grid, BiomeComponent biome, Vector2 spot, float camped)
    {
        var under = new HashSet<Vector2i>();
        var anchored = new List<EntityUid>();
        for (var x = (int) MathF.Floor(spot.X - camped); x <= (int) MathF.Ceiling(spot.X + camped); x++)
        for (var y = (int) MathF.Floor(spot.Y - camped); y <= (int) MathF.Ceiling(spot.Y + camped); y++)
        {
            var tile = new Vector2i(x, y);
            if (Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), spot) > camped)
                continue;
            under.Add(tile);
            anchored.Clear();
            var enumerator = _maps.GetAnchoredEntitiesEnumerator(ground, grid, tile);
            while (enumerator.MoveNext(out var uid))
                anchored.Add(uid.Value);
            foreach (var uid in anchored)
                QueueDel(uid);
        }
        var fill = new List<(Vector2i, Tile)>();
        foreach (var tile in under)
        {
            if (_maps.GetTileRef(ground, grid, tile).Tile.IsEmpty && _biome.TryGetBiomeTile(ground, grid, tile, out var natural))
                fill.Add((tile, natural.Value));
        }
        if (fill.Count > 0)
            _maps.SetTiles(ground, grid, fill);
        _biome.WfPinTiles((ground, biome), under);
        return under;
    }

    /// <summary>Records the camp as a site, with its orbit signal.</summary>
    private void AddCampSite(EntityUid ground, EntityUid orbit, WFPlanetPoiTablePrototype table, WFPlanetPoiComponent record,
        WFPlanetCampPrototype camp, Vector2 spot, float camped, int tiles, HashSet<EntityUid> created)
    {
        var origin = spot.Floored();
        var site = new WFPlanetPoiSite
        {
            Name = camp.Name,
            Origin = origin,
            Bounds = new Box2i((int) MathF.Floor(spot.X - camped), (int) MathF.Floor(spot.Y - camped),
                (int) MathF.Ceiling(spot.X + camped) + 1, (int) MathF.Ceiling(spot.Y + camped) + 1),
            TileCount = tiles,
            Connection = WFPlanetPoiConnection.Isolated,
            Expeditions = false,
            Difficulty = table.Difficulty,
        };
        site.Signal = SpawnSignal(ground, orbit, origin, new WFPlanetPoiEntry { Name = camp.Name }, table);
        created.Add(site.Signal);
        record.Sites.Add(site);
        ClearFurnitureInside(record, site.Bounds);
    }

    /// <summary>Once the ground exists: the guards outside, in a loose ring round the hull, and their cover.</summary>
    private async Task SpawnPendingCampsAsync(Entity<BiomeComponent> ground, MapGridComponent grid, WFPlanetPoiComponent record)
    {
        var camps = record.PendingCamps.ToList();
        record.PendingCamps.Clear();
        foreach (var pending in camps)
        {
            await _preload.Breathe();
            if (TerminatingOrDeleted(ground))
                return;
            if (!_proto.TryIndex(pending.Camp, out var camp))
                continue;

            bool Open(Vector2i tile) => !_maps.GetTileRef(ground, grid, tile).Tile.IsEmpty
                                        && !_maps.GetAnchoredEntitiesEnumerator(ground, grid, tile).MoveNext(out _);

            // What the camp has claimed so far: a prop's footprint (a tile unless it says more) is kept clear of the rest.
            var reserved = new List<Box2>();
            var limit = pending.Reach + CampRing;

            Vector2i? RingTile(float distance, Box2? footprint = null)
            {
                var local = footprint ?? new Box2(-0.5f, -0.5f, 0.5f, 0.5f);
                for (var attempt = 0; attempt < 48; attempt++)
                {
                    var tile = (pending.Centre + _random.NextAngle().ToVec() * distance).Floored();
                    var box = local.Translated((Vector2) tile + new Vector2(0.5f));
                    if (!Open(tile) || !FitsCamp(box, pending.Centre, limit, reserved, Open))
                        continue;
                    reserved.Add(box);
                    return tile;
                }
                return null;
            }

            foreach (var prop in camp.Props)
            {
                if (prop.Ids.Count == 0)
                    continue;
                var props = _random.Next(prop.Min, prop.Max + 1);
                for (var i = 0; i < props; i++)
                {
                    if (RingTile(pending.Reach + MathF.Min(prop.Distance, CampRing - 1f), prop.Footprint) is { } tile)
                        Spawn(_random.Pick(prop.Ids), new EntityCoordinates(ground, (Vector2) tile + new Vector2(0.5f)));
                }
            }

            var cover = camp.Cover.Count == 0 ? 0 : _random.Next(camp.MinCover, camp.MaxCover + 1);
            for (var i = 0; i < cover; i++)
            {
                if (RingTile(pending.Reach + 3f) is { } tile)
                    Spawn(_random.Pick(camp.Cover), new EntityCoordinates(ground, (Vector2) tile + new Vector2(0.5f)));
            }

            var guards = camp.Profile == null ? 0 : _random.Next(camp.MinGroundGuards, camp.MaxGroundGuards + 1);
            for (var i = 0; i < guards; i++)
            {
                await _preload.Breathe();
                if (TerminatingOrDeleted(ground))
                    return;
                if (RingTile(pending.Reach + _random.NextFloat(1.5f, 2.5f)) is { } tile)
                    SpawnCampGuard(camp, new EntityCoordinates(ground, (Vector2) tile + new Vector2(0.5f)), pending.Group);
            }

            if (camp.Home is { } home)
                SpawnHome(home, new EntityCoordinates(ground, pending.Centre), pending.Reach + CampRing - 2f);

            if (camp.Residents is { } residents)
            {
                var count = _random.Next(camp.MinResidents, camp.MaxResidents + 1);
                for (var i = 0; i < count; i++)
                {
                    foreach (var id in _entityTables.GetSpawns(residents))
                    {
                        if (RingTile(pending.Reach + _random.NextFloat(1.5f, 3f)) is { } tile)
                            Spawn(id, new EntityCoordinates(ground, (Vector2) tile + new Vector2(0.5f)));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Whether a prop's footprint lies inside the camp's cleared ring, over open ground, and clear of what the camp has
    /// already claimed.
    /// </summary>
    public static bool FitsCamp(Box2 box, Vector2 centre, float limit, List<Box2> reserved, Func<Vector2i, bool> open)
    {
        foreach (var corner in new[] { box.BottomLeft, box.BottomRight, box.TopLeft, box.TopRight })
        {
            if (Vector2.Distance(corner, centre) > limit)
                return false;
        }

        // Boxes that only touch don't overlap.
        var inner = box.Enlarged(-0.01f);
        foreach (var claimed in reserved)
        {
            if (claimed.Intersects(inner))
                return false;
        }

        for (var x = (int) MathF.Floor(inner.Left); x <= (int) MathF.Floor(inner.Right); x++)
        for (var y = (int) MathF.Floor(inner.Bottom); y <= (int) MathF.Floor(inner.Top); y++)
        {
            if (!open(new Vector2i(x, y)))
                return false;
        }

        return true;
    }

    /// <summary>Marks a camp or house as somewhere strangers live, so whoever lives there guards it.</summary>
    private void SpawnHome(EntProtoId id, EntityCoordinates centre, float radius)
    {
        var home = Spawn(id, centre);
        if (!TryComp(home, out WFStrangerHomeComponent? comp))
            return;
        comp.WarnRadius += radius - comp.Radius;
        comp.Radius = radius;
    }

    /// <summary>One guard from the camp's profile, posted where he stands, on the camp's side.</summary>
    private EntityUid? SpawnCampGuard(WFPlanetCampPrototype camp, EntityCoordinates post, string group)
    {
        if (camp.Profile is not { } profileId)
            return null;
        _proto.TryIndex(profileId, out var profile);
        ProtoId<StartingGearPrototype>? loadout = null;
        if (profile != null && profile.Loadouts.TryGetValue(camp.Role, out var pool) && pool.Count > 0)
            loadout = _random.Pick(pool);
        var body = loadout != null && profile != null && profile.Bodies.Count > 0 ? _random.Pick(profile.Bodies) : (EntProtoId?) null;
        string? name = null;
        if (profile?.FirstNames is { } firstId && profile.LastNames is { } lastId
            && _proto.TryIndex(firstId, out var first) && _proto.TryIndex(lastId, out var last)
            && first.Values.Count > 0 && last.Values.Count > 0)
            name = $"{_random.Pick(first.Values)} {_random.Pick(last.Values)}";

        if (_crew.SpawnCrewman(camp.Role, post, group, loadout, body, name, profileId) is not { } guard)
            return null;

        if (profile != null && profile.Components.Count > 0)
            EntityManager.AddComponents(guard, profile.Components);
        if (TryComp<WFCrewComponent>(guard, out var crewman))
        {
            crewman.Skill = camp.Skill;
            _crew.Apply((guard, crewman));
        }

        EnsureComp<NpcFactionMemberComponent>(guard);
        _factions.ClearFactions(guard, false);
        _factions.AddFaction(guard, camp.Faction);
        return guard;
    }
}
