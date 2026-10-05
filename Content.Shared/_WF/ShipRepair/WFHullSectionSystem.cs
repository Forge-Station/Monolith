using System.Linq;
using System.Numerics;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Robust.Shared;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;

namespace Content.Shared._WF.ShipRepair;

/// <summary>SRD rules for broken hulls: which grid a click works on, where the hull may be rebuilt, and reattaching sections.</summary>
public sealed partial class WFHullSectionSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IMapManager _mapMan = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedChargesSystem _charges = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>How far a section may sit from its place on the hull, in tiles, and still be reattached.</summary>
    public const float ReattachRange = 32f;

    /// <summary>Seconds every reattach takes, before the tool's time multiplier.</summary>
    public const float ReattachBaseTime = 5f;

    /// <summary>Seconds each tile of the section adds.</summary>
    public const float ReattachTimePerTile = 0.1f;

    /// <summary>The longest a reattach takes, before the tool's time multiplier.</summary>
    public const float ReattachMaxTime = 30f;

    /// <summary>Half the side of the box tested for another grid on a tile; it stays inside the tile at any rotation.</summary>
    private const float OccupancyHalfExtent = 0.35f;

    /// <summary>The four tiles that share an edge with a tile; grid splitting counts only these as joined.</summary>
    private static readonly Vector2i[] EdgeNeighbours = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipRepairToolComponent, WFHullReattachDoAfterEvent>(OnReattachDoAfter);
    }

    /// <summary>
    /// The grid an SRD click works on: a section, hull or other grid with a tile under the click, then the user's own
    /// grid unless it is planet ground or a section, then a hull beside the click, then the section the user stands on,
    /// then another grid beside the click, then planet ground.
    /// </summary>
    public Entity<MapGridComponent>? PickTarget(TransformComponent toolXform, Vector2 clickWorld)
    {
        var grids = new List<Entity<MapGridComponent>>();
        _mapMan.FindGridsIntersecting(toolXform.MapID, Box2.CenteredAround(clickWorld, new Vector2(0.1f, 0.1f)), ref grids, true, false);

        Entity<MapGridComponent>? section = null, hull = null, other = null;
        foreach (var grid in grids)
        {
            if (IsGround(grid) || _map.GetTileRef(grid, _map.WorldToTile(grid, grid.Comp, clickWorld)).Tile.IsEmpty)
                continue;

            if (IsSection(grid))
                section ??= grid;
            else if (HasComp<ShipRepairDataComponent>(grid))
                hull ??= grid;
            else
                other ??= grid;
        }

        if ((section ?? hull ?? other) is { } hit)
            return hit;

        // Standing on the ship and clicking into a hole in it. From a section, a click beside the hull means the hull.
        Entity<MapGridComponent>? own = null;
        if (toolXform.GridUid is { } ownUid && !IsGround(ownUid) && TryComp<MapGridComponent>(ownUid, out var ownGrid))
            own = (ownUid, ownGrid);

        if (own is { } ship && !IsSection(ship))
            return ship;

        grids.Clear();
        _mapMan.FindGridsIntersecting(toolXform.MapID, Box2.CenteredAround(clickWorld, new Vector2(1.5f, 1.5f)), ref grids, false, false);

        Entity<MapGridComponent>? near = null;
        foreach (var grid in grids)
        {
            if (IsGround(grid))
                continue;

            if (HasComp<ShipRepairDataComponent>(grid))
                return grid;

            near ??= grid;
        }

        if ((own ?? near) is { } fallback)
            return fallback;

        if (toolXform.GridUid is { } ground && TryComp<MapGridComponent>(ground, out var groundGrid))
            return (ground, groundGrid);

        return null;
    }

    /// <summary>Whether a grid is a section still linked to a hull; one given a snapshot of its own is a hull.</summary>
    public bool IsSection(EntityUid grid)
    {
        return TryComp<WFHullSectionComponent>(grid, out var section)
               && section.Hull != null
               && !HasComp<ShipRepairDataComponent>(grid);
    }

    /// <summary>The hull a section belongs to, while that hull still holds its repair snapshot.</summary>
    public bool TryGetHull(EntityUid section, out Entity<MapGridComponent> hull)
    {
        hull = default;
        if (!TryComp<WFHullSectionComponent>(section, out var link)
            || link.Hull is not { } uid
            || TerminatingOrDeleted(uid)
            || !HasComp<ShipRepairDataComponent>(uid)
            || !TryComp<MapGridComponent>(uid, out var grid))
            return false;

        hull = (uid, grid);
        return true;
    }

    /// <summary>
    /// Whether a snapshot original standing on this grid is debris of the hull: on a section broken off it, or on
    /// planet ground it was torn off onto. The SRD rebuilds such a fixture and takes the original away as it does, so
    /// there is never a second one. On any other grid the original simply still exists.
    /// </summary>
    public bool IsDebrisOf(EntityUid? grid, EntityUid hull)
    {
        return grid is { } on && (IsGround(on) || TryGetHull(on, out var owner) && owner.Owner == hull);
    }

    /// <summary>The tile index a hull-local position falls in.</summary>
    public Vector2i TileOf(EntityUid grid, Vector2 local)
    {
        var size = CompOrNull<MapGridComponent>(grid)?.TileSize ?? 1;
        return new Vector2i((int) MathF.Floor(local.X / size), (int) MathF.Floor(local.Y / size));
    }

    /// <summary>Whether the SRD may rebuild this hull tile; tells the user why not.</summary>
    public bool CanRebuildAt(EntityUid tool, EntityUid user, EntityUid hull, Vector2i index)
    {
        if (!TryComp<MapGridComponent>(hull, out var grid))
            return false;

        if (IsOccupied((hull, grid), index, null))
            return Refuse(tool, user, "wf-ship-repair-blocked");

        if (IsReserved(hull, index))
            return Refuse(tool, user, "wf-ship-repair-reserved");

        return true;
    }

    /// <summary>Whether the SRD may lay this hull tile: <see cref="CanRebuildAt"/>, and joined to the hull.</summary>
    public bool CanRebuildTileAt(EntityUid tool, EntityUid user, EntityUid hull, Vector2i index)
    {
        if (!CanRebuildAt(tool, user, hull, index))
            return false;

        return !WouldSplitOff((hull, Comp<MapGridComponent>(hull)), index) || Refuse(tool, user, "wf-ship-repair-edge");
    }

    /// <summary>
    /// Whether a tile laid in an empty spot would share no edge with a hull tile, so grid splitting would break it
    /// straight off as a section of its own.
    /// </summary>
    public bool WouldSplitOff(Entity<MapGridComponent> hull, Vector2i index)
    {
        if (!hull.Comp.CanSplit || !_cfg.GetCVar(CVars.GridSplitting) || !_map.GetTileRef(hull, index).Tile.IsEmpty)
            return false;

        foreach (var offset in EdgeNeighbours)
        {
            if (!_map.GetTileRef(hull, index + offset).Tile.IsEmpty)
                return false;
        }

        return true;
    }

    /// <summary>Tells the user why the SRD won't rebuild here; always false.</summary>
    private bool Refuse(EntityUid tool, EntityUid user, string reason)
    {
        // The client may not see every nearby grid, so it only skips predicting the repair.
        if (_net.IsServer)
            _popup.PopupEntity(Loc.GetString(reason), tool, user, PopupType.MediumCaution);

        return false;
    }

    /// <summary>Whether a grid other than the hull, planet ground or the ignored grid covers a hull tile in the world.</summary>
    public bool IsOccupied(Entity<MapGridComponent> hull, Vector2i index, EntityUid? ignore)
    {
        var xform = Transform(hull);
        var centre = Vector2.Transform(_map.TileCenterToVector(hull, index), _transform.GetWorldMatrix(xform));
        var grids = new List<Entity<MapGridComponent>>();
        _mapMan.FindGridsIntersecting(xform.MapID, Box2.CenteredAround(centre, new Vector2(OccupancyHalfExtent * 2)), ref grids, false, false);

        foreach (var grid in grids)
        {
            if (grid.Owner != hull.Owner && grid.Owner != ignore && !IsGround(grid))
                return true;
        }

        return false;
    }

    /// <summary>Whether a hull tile is the place of a linked section on the same map and within reattach range.</summary>
    public bool IsReserved(EntityUid hull, Vector2i index)
    {
        var query = EntityQueryEnumerator<WFHullSectionComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var section, out var grid))
        {
            if (section.Hull == hull
                && !_map.GetTileRef(uid, grid, index).Tile.IsEmpty
                && InReattachRange(hull, (uid, grid)))
                return true;
        }

        return false;
    }

    /// <summary>Whether a section is on its hull's map and within reattach range of its place on the hull.</summary>
    public bool InReattachRange(EntityUid hull, Entity<MapGridComponent> section)
    {
        var mapId = Transform(hull).MapID;
        if (mapId == MapId.Nullspace || Transform(section).MapID != mapId)
            return false;

        return HomeOffset(hull, section) <= ReattachRange * section.Comp.TileSize;
    }

    /// <summary>How far a section's centre sits from where it belongs on the hull.</summary>
    public float HomeOffset(EntityUid hull, Entity<MapGridComponent> section)
    {
        var centre = section.Comp.LocalAABB.Center;
        var home = Vector2.Transform(centre, _transform.GetWorldMatrix(hull));
        var actual = Vector2.Transform(centre, _transform.GetWorldMatrix(section));
        return Vector2.Distance(home, actual);
    }

    /// <summary>Whether a section's place on the hull is free: no hull tile at its indices and no other grid in the way.</summary>
    public bool HomeClear(Entity<MapGridComponent> hull, Entity<MapGridComponent> section)
    {
        // One broad query first; tiles are only tested one by one when another grid is near the section's place.
        var xform = Transform(hull);
        var home = _transform.GetWorldMatrix(xform).TransformBox(section.Comp.LocalAABB);
        var grids = new List<Entity<MapGridComponent>>();
        _mapMan.FindGridsIntersecting(xform.MapID, home, ref grids, true, false);
        var crowded = grids.Any(grid => grid.Owner != hull.Owner && grid.Owner != section.Owner && !IsGround(grid));

        var tiles = _map.GetAllTilesEnumerator(section, section.Comp);
        while (tiles.MoveNext(out var tile))
        {
            var index = tile.Value.GridIndices;
            if (!_map.GetTileRef(hull, index).Tile.IsEmpty || crowded && IsOccupied(hull, index, section))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether a section, back in its place, shares a tile edge with the hull. One that doesn't would be a separate
    /// piece of the grid, and grid splitting would break it straight off again.
    /// </summary>
    public bool TouchesHull(Entity<MapGridComponent> hull, Entity<MapGridComponent> section)
    {
        var tiles = _map.GetAllTilesEnumerator(section, section.Comp);
        while (tiles.MoveNext(out var tile))
        {
            foreach (var offset in EdgeNeighbours)
            {
                if (!_map.GetTileRef(hull, tile.Value.GridIndices + offset).Tile.IsEmpty)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Seconds a reattach takes for a section of this many tiles, before the tool's time multiplier.</summary>
    public static float ReattachTime(int tiles)
    {
        return Math.Min(ReattachBaseTime + ReattachTimePerTile * tiles, ReattachMaxTime);
    }

    /// <summary>How many tiles a grid has.</summary>
    public int CountTiles(Entity<MapGridComponent> grid)
    {
        var count = 0;
        var tiles = _map.GetAllTilesEnumerator(grid, grid.Comp);
        while (tiles.MoveNext(out _))
        {
            count++;
        }

        return count;
    }

    /// <summary>Starts reattaching a section to its hull, server side; tells the user why it can't.</summary>
    public void TryStartReattach(Entity<ShipRepairToolComponent> tool, EntityUid user, Entity<MapGridComponent> section)
    {
        if (_net.IsClient || !CanReattach(tool, user, section, out _))
            return;

        var ev = new WFHullReattachDoAfterEvent { Section = GetNetEntity(section) };
        var delay = ReattachTime(CountTiles(section)) * tool.Comp.RepairTimeMultiplier;
        var args = new DoAfterArgs(EntityManager, user, delay, ev, tool)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            MovementThreshold = 0.5f,
            DuplicateCondition = DuplicateConditions.SameEvent,
        };

        if (_doAfter.TryStartDoAfter(args))
            _audio.PlayPvs(tool.Comp.RepairSound, tool);
    }

    private void OnReattachDoAfter(Entity<ShipRepairToolComponent> ent, ref WFHullReattachDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || _net.IsClient)
            return;

        var uid = GetEntity(args.Section);
        if (TerminatingOrDeleted(uid) || !TryComp<MapGridComponent>(uid, out var grid))
            return;

        // Anything may have moved while the do-after ran.
        if (!CanReattach(ent, args.User, (uid, grid), out var hull))
            return;

        args.Handled = true;
        if (TryComp<LimitedChargesComponent>(ent, out var charges))
            _charges.UseCharges(ent, charges.Charges, charges);

        var ev = new WFHullReattachEvent(hull);
        RaiseLocalEvent(uid, ref ev);
        _popup.PopupEntity(Loc.GetString("wf-ship-repair-reattached"), ent, args.User);
    }

    /// <summary>Whether a section can go back on its hull now, joined to it; tells the user why not.</summary>
    private bool CanReattach(Entity<ShipRepairToolComponent> tool, EntityUid user, Entity<MapGridComponent> section, out Entity<MapGridComponent> hull)
    {
        string? message = null;
        if (!TryGetHull(section, out hull))
            message = Loc.GetString("wf-ship-repair-reattach-no-hull");
        else if (TryComp<ShipRepairRestrictComponent>(hull, out var restrict) && _whitelist.IsWhitelistFail(restrict.ToolWhitelist, tool))
            message = Loc.GetString("ship-repair-tool-fail-whitelist");
        else if (TryComp<LimitedChargesComponent>(tool, out var charges) && charges.Charges < charges.MaxCharges)
            message = Loc.GetString("wf-ship-repair-reattach-charge", ("charges", charges.Charges), ("max", charges.MaxCharges));
        else if (!InReattachRange(hull, section))
            message = Loc.GetString("wf-ship-repair-reattach-range");
        else if (!HomeClear(hull, section))
            message = Loc.GetString("wf-ship-repair-reattach-blocked");
        else if (!TouchesHull(hull, section))
            message = Loc.GetString("wf-ship-repair-reattach-apart");

        if (message == null)
            return true;

        _popup.PopupEntity(message, tool, user, PopupType.MediumCaution);
        return false;
    }

    private bool IsGround(EntityUid grid)
    {
        return HasComp<MapComponent>(grid);
    }
}
