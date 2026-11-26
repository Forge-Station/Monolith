using System.Numerics;
using Content.Server._Mono.FireControl;
using Content.Server._WF.Shipyard;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Decals;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._WF.ShipRepair;
using Robust.Server.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.ShipRepair;

/// <summary>Keeps one repair snapshot per broken hull, hands it on when the hull goes, and merges sections back.</summary>
public sealed partial class WFHullSectionServerSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private FireControlSystem _fireControl = default!;
    [Dependency] private GridFixtureSystem _fixtures = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFHullSectionSystem _sections = default!;

    /// <summary>The share of the blueprint's tiles a section needs to take over from a deleted hull; smaller pieces are scrap.</summary>
    public const float HeirMinShare = 0.25f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);
        SubscribeLocalEvent<ShipRepairDataComponent, EntityTerminatingEvent>(OnHullTerminating);
        SubscribeLocalEvent<ShipRepairDataComponent, ComponentRemove>(OnSnapshotRemoved);
        SubscribeLocalEvent<WFHullSectionComponent, WFHullReattachEvent>(OnReattach);
        SubscribeLocalEvent<ShipSoldEvent>(OnShipSold);
    }

    /// <summary>The biggest piece stays the hull and keeps the snapshot; every other piece links back to that hull.</summary>
    private void OnGridSplit(ref GridSplitEvent args)
    {
        var hull = HasComp<ShipRepairDataComponent>(args.Grid)
            ? args.Grid
            : CompOrNull<WFHullSectionComponent>(args.Grid)?.Hull;

        if (hull is not { } owner || TerminatingOrDeleted(owner))
            return;

        foreach (var grid in args.NewGrids)
        {
            if (TerminatingOrDeleted(grid))
                continue;

            var section = EnsureComp<WFHullSectionComponent>(grid);
            section.Hull = owner;
            Dirty(grid, section);
        }
    }

    /// <summary>A sold ship's sections are wreckage, so selling the hull never hands its blueprint to a piece left behind.</summary>
    private void OnShipSold(ref ShipSoldEvent args)
    {
        var sold = new List<EntityUid>();
        var query = EntityQueryEnumerator<WFHullSectionComponent>();
        while (query.MoveNext(out var uid, out var section))
        {
            if (section.Hull == args.Shuttle)
                sold.Add(uid);
        }

        foreach (var uid in sold)
        {
            RemComp<WFHullSectionComponent>(uid);
        }
    }

    // A deleted hull is detached from its map before its components go, so it hands on while it still has one.
    private void OnHullTerminating(Entity<ShipRepairDataComponent> ent, ref EntityTerminatingEvent args)
    {
        HandOn(ent);
    }

    private void OnSnapshotRemoved(Entity<ShipRepairDataComponent> ent, ref ComponentRemove args)
    {
        if (!TerminatingOrDeleted(ent))
            HandOn(ent);
    }

    /// <summary>
    /// A hull losing its snapshot hands it to its largest section on the same map, if that holds at least
    /// <see cref="HeirMinShare"/> of the blueprint; without one, its sections are wreckage.
    /// </summary>
    private void HandOn(Entity<ShipRepairDataComponent> ent)
    {
        var map = Transform(ent).MapUid;
        var mapGone = map == null || TerminatingOrDeleted(map.Value);
        var sections = new List<Entity<WFHullSectionComponent>>();
        Entity<MapGridComponent>? heir = null;
        // One below the fewest tiles an heir may have.
        var heirTiles = (int) MathF.Ceiling(BlueprintTiles(ent.Comp) * HeirMinShare) - 1;

        var query = EntityQueryEnumerator<WFHullSectionComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var section, out var grid, out var xform))
        {
            if (section.Hull != ent.Owner || TerminatingOrDeleted(uid))
                continue;

            sections.Add((uid, section));
            if (mapGone || xform.MapUid != map)
                continue;

            var tiles = _sections.CountTiles((uid, grid));
            if (tiles <= heirTiles)
                continue;

            heir = (uid, grid);
            heirTiles = tiles;
        }

        if (heir is { } next)
        {
            CopySnapshot(ent.Comp, next);
            if (TryComp<ShipRepairRestrictComponent>(ent, out var restrict))
                EnsureComp<ShipRepairRestrictComponent>(next).ToolWhitelist = restrict.ToolWhitelist;
        }

        foreach (var section in sections)
        {
            if (heir is not { } successor || section.Owner == successor.Owner)
            {
                RemComp<WFHullSectionComponent>(section);
                continue;
            }

            section.Comp.Hull = successor.Owner;
            Dirty(section);
        }
    }

    /// <summary>How many tiles a repair snapshot holds.</summary>
    private static int BlueprintTiles(ShipRepairDataComponent data)
    {
        var count = 0;
        foreach (var chunk in data.Chunks.Values)
        {
            foreach (var tile in chunk.Tiles)
            {
                if (tile != Tile.Empty.TypeId)
                    count++;
            }
        }

        return count;
    }

    /// <summary>Gives a grid its own copy of a repair snapshot.</summary>
    private void CopySnapshot(ShipRepairDataComponent source, EntityUid target)
    {
        var copy = EnsureComp<ShipRepairDataComponent>(target);
        copy.ChunkSize = source.ChunkSize;
        copy.EntityPalette = new List<EntProtoId>(source.EntityPalette);
        copy.Chunks.Clear();

        foreach (var (index, chunk) in source.Chunks)
        {
            var chunkCopy = new ShipRepairChunk
            {
                Tiles = (int[]) chunk.Tiles.Clone(),
                NextUid = chunk.NextUid,
            };

            foreach (var (id, spec) in chunk.Entities)
            {
                chunkCopy.Entities[id] = new ShipRepairEntitySpecifier
                {
                    ProtoIndex = spec.ProtoIndex,
                    LocalPosition = spec.LocalPosition,
                    Rotation = spec.Rotation,
                    OriginalEntity = spec.OriginalEntity,
                };
            }

            copy.Chunks[index] = chunkCopy;
        }

        Dirty(target, copy);
    }

    private void OnReattach(Entity<WFHullSectionComponent> ent, ref WFHullReattachEvent args)
    {
        Reattach(args.Hull, ent.Owner);
    }

    /// <summary>Merges a section into its hull at its own tile indices, with everything aboard.</summary>
    public void Reattach(EntityUid hull, EntityUid section)
    {
        if (!TryComp<MapGridComponent>(hull, out var hullGrid) || !TryComp<MapGridComponent>(section, out var sectionGrid))
            return;

        // The engine merge moves tiles, anchored entities and whatever stands on a tile, and engines change banks as
        // they re-anchor; the rest is carried here.
        ReleaseStrays((section, sectionGrid));
        var decals = _decals.GetDecalsIntersecting(section, sectionGrid.LocalAABB.Enlarged(1f));
        var air = _atmos.WfCopyGridAir(section);
        var motion = CarriedMotion(section);

        // A section keeps the hull's tile indices, so the identity puts every tile back where it was. Splitting waits
        // until the merge is done, or a section not yet joined would be torn off halfway through.
        var canSplit = hullGrid.CanSplit;
        hullGrid.CanSplit = false;
        try
        {
            _fixtures.Merge(hull, section, Matrix3x2.Identity, hullGrid, sectionGrid);
        }
        finally
        {
            hullGrid.CanSplit = canSplit;
        }

        foreach (var (_, decal) in decals)
        {
            _decals.TryAddDecal(decal, new EntityCoordinates(hull, decal.Coordinates), out _);
        }

        _atmos.WfSeedGridAir(hull, air);

        // The move keeps each body's speed over the map; aboard the hull it keeps its speed relative to the deck instead.
        foreach (var (uid, linear, angular) in motion)
        {
            if (TerminatingOrDeleted(uid) || Transform(uid).ParentUid != hull || !TryComp<PhysicsComponent>(uid, out var body))
                continue;

            _physics.SetLinearVelocity(uid, linear, body: body);
            _physics.SetAngularVelocity(uid, angular, body: body);
        }

        // Guns on the section left the gunnery server when it broke off, and re-anchoring doesn't sign them back up.
        if (TryComp<FireControlGridComponent>(hull, out var fireControl))
            _fireControl.RefreshControllables(hull, fireControl);
    }

    /// <summary>The velocities, relative to the section, of the loose bodies aboard it.</summary>
    private List<(EntityUid Uid, Vector2 Linear, float Angular)> CarriedMotion(EntityUid section)
    {
        var motion = new List<(EntityUid, Vector2, float)>();
        var children = Transform(section).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!Transform(child).Anchored && TryComp<PhysicsComponent>(child, out var body))
                motion.Add((child, body.LinearVelocity, body.AngularVelocity));
        }

        return motion;
    }

    /// <summary>Leaves loose entities that sit off the section's tiles where they are; the merge would delete them with the grid.</summary>
    private void ReleaseStrays(Entity<MapGridComponent> section)
    {
        var xform = Transform(section);
        if (xform.MapUid is not { } map)
            return;

        var strays = new List<EntityUid>();
        var children = xform.ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            var childXform = Transform(child);
            if (!childXform.Anchored
                && _map.GetTileRef(section, _sections.TileOf(section, childXform.LocalPosition)).Tile.IsEmpty)
                strays.Add(child);
        }

        foreach (var stray in strays)
        {
            var (position, rotation) = _transform.GetWorldPositionRotation(stray);
            _transform.SetCoordinates(stray, Transform(stray), new EntityCoordinates(map, position), rotation);
        }
    }
}
