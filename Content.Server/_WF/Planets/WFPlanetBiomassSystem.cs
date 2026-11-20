using Content.Server.Spreader;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Planets;

/// <summary>Allow chimera biomass on hulls, never on streamed or detached terrain.</summary>
[RegisterComponent]
public sealed partial class WFPlanetBiomassRestrictionComponent : Component;

/// <summary>
/// Keeps chimera biomass off planet terrain and, on a planet's maps, on one hull and under a cap. Off a planet a hive
/// grows as upstream has it.
/// </summary>
public sealed partial class WFPlanetBiomassSystem : EntitySystem
{
    public const int MaxHullBiomass = 256;
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _aboard = new();
    private readonly Dictionary<EntityUid, EntityUid> _grids = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<WFPlanetBiomassRestrictionComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFPlanetBiomassRestrictionComponent, EntParentChangedMessage>(OnParentChanged);
        SubscribeLocalEvent<WFPlanetBiomassRestrictionComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<WFPlanetBiomassRestrictionComponent, SpreadNeighborsEvent>(OnSpread, before: new[] { typeof(KudzuSystem) });
    }

    /// <summary>True when the entity is on streamed or detached planet terrain rather than a hull.</summary>
    public bool IsPlanet(EntityUid uid)
    {
        if (!TryComp(uid, out TransformComponent? xform))
            return false;
        if (HasComp<WFDetachedTerrainComponent>(uid) || HasComp<WFDetachedTerrainComponent>(xform.GridUid))
            return true;
        if (HasComp<MapGridComponent>(uid) && !HasComp<MapComponent>(uid))
            return false;
        // A separate hull remains a legitimate host even when its map is a planet.
        if (xform.GridUid is { } grid && grid != xform.MapUid && HasComp<MapGridComponent>(grid))
            return false;
        return HasComp<WFPlanetLayerComponent>(uid) || HasComp<WFPlanetLayerComponent>(xform.MapUid);
    }

    /// <summary>
    /// True when the entity is on one of a planet's maps, on terrain or on a hull, or in the gap between two of them:
    /// where growth is capped and kept to its hull.
    /// </summary>
    private bool OnPlanetMap(EntityUid uid)
    {
        var map = Transform(uid).MapUid;

        // A hull climbing or hovering between two layers rides a transit map of its own.
        if (TryComp<CEZTransitMapComponent>(map, out var transit))
            map = transit.LowerMap ?? transit.UpperMap;

        return HasComp<WFPlanetLayerComponent>(map);
    }

    public int HullCount(EntityUid grid) => _aboard.TryGetValue(grid, out var entities) ? entities.Count : 0;
    private void OnMapInit(Entity<WFPlanetBiomassRestrictionComponent> ent, ref MapInitEvent args) => Track(ent);
    private void OnParentChanged(Entity<WFPlanetBiomassRestrictionComponent> ent, ref EntParentChangedMessage args) => Track(ent);
    private void OnTerminating(Entity<WFPlanetBiomassRestrictionComponent> ent, ref EntityTerminatingEvent args) => Forget(ent);

    private void Forget(EntityUid uid)
    {
        if (_grids.Remove(uid, out var grid) && _aboard.TryGetValue(grid, out var entities))
        {
            entities.Remove(uid);
            if (entities.Count == 0) _aboard.Remove(grid);
        }
    }

    private void Track(EntityUid uid)
    {
        Forget(uid);
        if (IsPlanet(uid))
        {
            QueueDel(uid);
            return;
        }
        if (Transform(uid).GridUid is not { } grid || !HasComp<MapGridComponent>(grid))
            return;
        if (!_aboard.TryGetValue(grid, out var entities))
            _aboard[grid] = entities = new();
        if (entities.Count >= MaxHullBiomass && OnPlanetMap(uid))
        {
            QueueDel(uid);
            return;
        }
        entities.Add(uid);
        _grids[uid] = grid;
    }

    private void OnSpread(Entity<WFPlanetBiomassRestrictionComponent> ent, ref SpreadNeighborsEvent args)
    {
        var terrain = IsPlanet(ent);
        if (!terrain && !OnPlanetMap(ent))
            return;

        if (terrain || Transform(ent).GridUid is not { } grid)
        {
            args.NeighborFreeTiles.Clear();
            return;
        }
        var room = Math.Max(0, MaxHullBiomass - HullCount(grid));
        // Keep candidates on this hull so growth can't spread through docks onto terrain or other grids.
        args.NeighborFreeTiles.RemoveAll(tile => tile.Tile.GridUid != grid || tile.Tile.Tile.IsEmpty);
        if (args.NeighborFreeTiles.Count > room)
            args.NeighborFreeTiles.RemoveRange(room, args.NeighborFreeTiles.Count - room);
    }
}
