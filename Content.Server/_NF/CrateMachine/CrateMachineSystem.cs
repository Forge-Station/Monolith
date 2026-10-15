using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics; // Forge-change
using Content.Server.Stack; // Forge-change
using Content.Server.Storage.EntitySystems;
using Content.Shared._NF.CrateMachine;
using Content.Shared._NF.CrateMachine.Components;
using Content.Shared._NF.Market; // Forge-change
using Content.Shared._NF.Market.Components; // Forge-change
using Content.Shared.Maps;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes; // Forge-change

namespace Content.Server._NF.CrateMachine;

/// <summary>
/// The crate machine system can be used to make a crate machine open and spawn crates.
/// When calling <see cref="OpenFor"/>, the machine will open the door and give a callback to the given
/// </summary>
public sealed partial class CrateMachineSystem : SharedCrateMachineSystem
{
    [Dependency] private SharedMapSystem _mapManager = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private EntityStorageSystem _storage = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    // Forge-change-start New parameter for crate machine
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CrateMachineComponent, CrateMachineOpenedEvent>(OnCrateMachineOpened);
    }

    private void OnCrateMachineOpened(EntityUid uid, CrateMachineComponent comp, CrateMachineOpenedEvent args)
    {
        switch (comp.Kind)
        {
            case CrateMachineKind.Cargo:
                DeliverCargo(uid);
                break;
            case CrateMachineKind.BlackMarket:
                DeliverBlackMarket(uid, comp);
                break;
        }
    }

    private void DeliverCargo(EntityUid uid)
    {
        if (!TryComp<MarketItemSpawnerComponent>(uid, out var itemSpawner) || itemSpawner.ItemsToSpawn.Count == 0)
            return;

        if (!TryComp<CrateMachineComponent>(uid, out var comp))
            return;

        var targetCrate = SpawnCrate(uid, comp);
        var coordinates = Transform(targetCrate).Coordinates;

        foreach (var data in itemSpawner.ItemsToSpawn)
        {
            if (data.StackPrototype != null && _proto.TryIndex(data.StackPrototype, out var stackPrototype))
            {
                var entityList = _stack.SpawnMultiple(stackPrototype.Spawn, data.Quantity, coordinates);
                foreach (var entity in entityList)
                    InsertIntoCrate(entity, targetCrate);
            }
            else
            {
                for (var i = 0; i < data.Quantity; i++)
                {
                    var spawn = Spawn(data.Prototype, coordinates);
                    InsertIntoCrate(spawn, targetCrate);
                }
            }
        }

        itemSpawner.ItemsToSpawn = [];
    }

    partial void DeliverBlackMarket(EntityUid uid, CrateMachineComponent comp);
    // Forge-change-end

    /// <summary>
    /// Checks if there is a crate on the crate machine.
    /// </summary>
    /// <param name="crateMachineUid">The Uid of the crate machine</param>
    /// <param name="component">The crate machine component</param>
    /// <param name="ignoreAnimation">Ignores animation checks</param>
    /// <returns>False if not occupied, true if it is.</returns>
    public bool IsOccupied(EntityUid crateMachineUid, CrateMachineComponent component, bool ignoreAnimation = false)
    {
    // Forge-change-start Different types of Crates
        return IsOccupied(crateMachineUid, component, component.CratePrototype, ignoreAnimation);
    }

    public bool IsOccupied(EntityUid crateMachineUid, CrateMachineComponent component, string cratePrototype, bool ignoreAnimation = false)
    {
    // Forge-change-end
        if (!TryComp(crateMachineUid, out TransformComponent? crateMachineTransform))
            return true;
        var tileRef = _turf.GetTileRef(crateMachineTransform.Coordinates);
        if (tileRef == null)
            return true;

        if (!ignoreAnimation && (component.OpeningTimeRemaining > 0 || component.ClosingTimeRemaining > 0f))
            return true;

        // Finally check if there is a crate intersecting the crate machine.
        return _lookup.GetLocalEntitiesIntersecting(tileRef.Value, flags: LookupFlags.All | LookupFlags.Approximate)
            .Any(entity => MetaData(entity).EntityPrototype?.ID == cratePrototype); // Forge-change Different types of Crates
    }

    /// <summary>
    /// Forge-change Find the nearest unoccupied anchored crate machine of the given kind on the same grid.
    /// </summary>
    // Forge-change-start New search logic
    public bool FindNearestUnoccupied(
        EntityUid from,
        int maxDistance,
        CrateMachineKind kind,
        [NotNullWhen(true)] out EntityUid? machineUid,
        [NotNullWhen(true)] out CrateMachineComponent? component,
        string? cratePrototype = null)
    {
        machineUid = null;
        component = null;

        if (maxDistance < 0)
            return false;

        var fromXform = Transform(from);
        if (fromXform.GridUid == null)
            return false;

        var bestDistance = float.MaxValue;
        var crateMachineQuery = AllEntityQuery<CrateMachineComponent, TransformComponent>();
        while (crateMachineQuery.MoveNext(out var crateMachineUid, out var comp, out var compXform))
        {
            if (comp.Kind != kind)
                continue;

            if (compXform.GridUid == null || compXform.GridUid != fromXform.GridUid)
                continue;

            if (!compXform.Anchored)
                continue;

            if (!_transform.InRange(compXform.Coordinates, fromXform.Coordinates, maxDistance))
                continue;

            var occupiedPrototype = cratePrototype ?? comp.CratePrototype;
            if (IsOccupied(crateMachineUid, comp, occupiedPrototype))
                continue;

            var distance = Vector2.DistanceSquared(
                _transform.ToMapCoordinates(compXform.Coordinates).Position,
                _transform.ToMapCoordinates(fromXform.Coordinates).Position);

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            machineUid = crateMachineUid;
            component = comp;
        }

        return machineUid != null;
    }

    /// <summary>
    /// Find the nearest unoccupied anchored cargo crate machine on the same grid.
    /// </summary>
    public bool FindNearestUnoccupied(EntityUid from, int maxDistance, [NotNullWhen(true)] out EntityUid? machineUid)
    {
        return FindNearestUnoccupied(from, maxDistance, CrateMachineKind.Cargo, out machineUid, out _);
    }
    // Forge-change-end

    /// <summary>
    /// Convenience function that simply spawns a crate and returns the uid.
    /// </summary>
    /// <param name="uid">The Uid of the crate machine</param>
    /// <param name="component">The crate machine component</param>
    /// <returns>The Uid of the spawned crate</returns>
    public EntityUid SpawnCrate(EntityUid uid, CrateMachineComponent component)
    {
    // Forge-change-start Different types of Crates
        return SpawnCrate(uid, component.CratePrototype);
    }

    public EntityUid SpawnCrate(EntityUid uid, string cratePrototype)
    {
        return Spawn(cratePrototype, Transform(uid).Coordinates);
    // Forge-change-end
    }
    
    /// <summary>
    /// Convenience function that simply inserts a entity into the container entity and relieve the caller of
    /// using the storage system reference.
    /// </summary>
    /// <param name="uid">The Uid of the crate machine</param>
    /// <param name="container">The Uid of the container</param>
    public void InsertIntoCrate(EntityUid uid, EntityUid container)
    {
        _storage.Insert(uid, container);
    }
}
