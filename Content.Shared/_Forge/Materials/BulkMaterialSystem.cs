using System.Diagnostics.CodeAnalysis;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Item;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Materials;

/// <summary>
/// Merges nearby ore and material stacks, and sizes a stack in storage by its count.
/// </summary>
public sealed class BulkMaterialSystem : EntitySystem
{
    private const float MergeInterval = 4f;
    private const int MergeBudget = 12;

    private static readonly List<Box2i> Shape1X1 = new() { new(0, 0, 0, 0) };
    private static readonly List<Box2i> Shape2X2 = new() { new(0, 0, 1, 1) };
    private static readonly List<Box2i> Shape2X3 = new() { new(0, 0, 1, 2) };
    private static readonly List<Box2i> Shape3X3 = new() { new(0, 0, 2, 2) };

    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private SharedStackSystem _stack = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedVirtualItemSystem _virtual = default!;

    private float _accumulator;
    private readonly Queue<EntityUid> _loose = new();
    private readonly HashSet<EntityUid> _looseSet = new();
    private readonly HashSet<Entity<BulkMaterialComponent>> _nearby = new();
    private readonly HashSet<EntityUid> _suppress = new();
    private readonly HashSet<EntityUid> _applied = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BulkMaterialComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<BulkMaterialComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<BulkMaterialComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<BulkMaterialComponent, StackCountChangedEvent>(OnCountChanged);
        SubscribeLocalEvent<BulkMaterialComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<BulkMaterialComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<ItemComponent, EntGotInsertedIntoContainerMessage>(OnItemInserted);
        SubscribeLocalEvent<ItemComponent, EntGotRemovedFromContainerMessage>(OnItemRemoved);

        SubscribeLocalEvent<BulkCarrySlowdownComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        _accumulator += frameTime;
        if (_accumulator < MergeInterval)
            return;

        _accumulator = 0f;
        var budget = Math.Min(MergeBudget, _loose.Count);
        while (budget-- > 0 && _loose.Count > 0)
        {
            var uid = _loose.Dequeue();
            _looseSet.Remove(uid);

            if (!TryComp<BulkMaterialComponent>(uid, out var bulk) || !TryComp<StackComponent>(uid, out var stack))
                continue;

            // A stack already queued at count 0 must not run. It would suck the merged
            // pile back into itself and then be deleted at the end of the tick.
            if (_container.IsEntityInContainer(uid) || TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid))
                continue;

            if (stack.Count <= 0)
                continue;

            MergeNearby(uid, bulk, stack);

            if (!TerminatingOrDeleted(uid) && Exists(uid) && !_container.IsEntityInContainer(uid))
                Track(uid);
        }
    }

    private void OnInit(Entity<BulkMaterialComponent> ent, ref ComponentInit args)
    {
        // Uninitialized maps must keep prototype data. PrototypeSaveTest spawns there
        // and fails if Stack/Item are rewritten before the map itself is initialized.
        if (_net.IsServer && !IsLiveMap(ent.Owner))
            return;

        Apply(ent);
    }

    private void OnMapInit(Entity<BulkMaterialComponent> ent, ref MapInitEvent args)
    {
        if (_applied.Contains(ent.Owner))
            return;

        Apply(ent);
    }

    private void Apply(Entity<BulkMaterialComponent> ent)
    {
        _applied.Add(ent.Owner);
        Refresh(ent);
        if (_net.IsServer && !_container.IsEntityInContainer(ent.Owner))
            Track(ent.Owner);
    }

    private bool IsLiveMap(EntityUid uid)
    {
        if (!TryComp(uid, out TransformComponent? xform))
            return false;

        if (xform.MapUid is { } mapUid)
            return _map.IsInitialized(mapUid);

        return xform.MapID == MapId.Nullspace;
    }

    private void OnShutdown(Entity<BulkMaterialComponent> ent, ref ComponentShutdown args)
    {
        _applied.Remove(ent.Owner);
        Untrack(ent.Owner);
        if (FindMob(ent.Owner) is { } mob)
            RefreshCarrier(mob);
    }

    private void OnCountChanged(Entity<BulkMaterialComponent> ent, ref StackCountChangedEvent args)
    {
        Refresh(ent);
    }

    private void OnInserted(Entity<BulkMaterialComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        Untrack(ent.Owner);
        if (FindMob(ent.Owner) is { } mob)
            RefreshCarrier(mob);
    }

    private void OnRemoved(Entity<BulkMaterialComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        if (!_container.IsEntityInContainer(ent.Owner))
            Track(ent.Owner);

        if (FindMob(args.Container.Owner) is { } mob)
            RefreshCarrier(mob);
    }

    /// <summary>
    /// A bag or box of ore moving onto a person. The stacks themselves do not get a new event.
    /// </summary>
    private void OnItemInserted(Entity<ItemComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        if (HasComp<BulkMaterialComponent>(ent.Owner) || !TryFindHeavy(ent.Owner, 0, out _))
            return;

        if (FindMob(ent.Owner) is { } mob)
            RefreshCarrier(mob);
    }

    private void OnItemRemoved(Entity<ItemComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        if (HasComp<BulkMaterialComponent>(ent.Owner) || !TryFindHeavy(ent.Owner, 0, out _))
            return;

        if (FindMob(args.Container.Owner) is { } mob)
            RefreshCarrier(mob);

        if (FindMob(ent.Owner) is { } next)
            RefreshCarrier(next);
    }

    private void OnRefreshSpeed(Entity<BulkCarrySlowdownComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Modifier < 1f)
            args.ModifySpeed(ent.Comp.Modifier, ent.Comp.Modifier);
    }

    private void Refresh(Entity<BulkMaterialComponent> ent)
    {
        if (_net.IsServer && !IsLiveMap(ent.Owner))
            return;

        if (!_suppress.Add(ent.Owner))
            return;

        try
        {
            if (!TryComp<StackComponent>(ent.Owner, out var stack) || !TryComp<ItemComponent>(ent.Owner, out var item))
                return;

            if (_net.IsServer)
                EnsureMaxCount(ent.Owner, stack, ent.Comp);

            ApplyPresentation(ent.Owner, stack.Count, ent.Comp, item);

            if (_net.IsServer)
                FitOrSpill(ent.Owner, ent.Comp, stack, item);

            ApplyPresentation(ent.Owner, stack.Count, ent.Comp, item);

            if (FindMob(ent.Owner) is { } mob)
                RefreshCarrier(mob);
        }
        finally
        {
            _suppress.Remove(ent.Owner);
        }
    }

    private void EnsureMaxCount(EntityUid uid, StackComponent stack, BulkMaterialComponent bulk)
    {
        if (stack.MaxCountOverride >= bulk.MaxCount)
            return;

        if (stack.MaxCountOverride == null && _stack.GetMaxCount(stack) >= bulk.MaxCount)
            return;

        stack.MaxCountOverride = bulk.MaxCount;
        Dirty(uid, stack);
    }

    private void ApplyPresentation(EntityUid uid, int count, BulkMaterialComponent bulk, ItemComponent item)
    {
        SetBulkShape(uid, count, bulk, item);

        if (_net.IsServer)
            ApplyHands(uid, count, bulk);
    }

    private void SetBulkShape(EntityUid uid, int count, BulkMaterialComponent bulk, ItemComponent item)
    {
        _item.SetShape(uid, new List<Box2i>(ShapeFor(count, bulk)), item);
    }

    private void ApplyHands(EntityUid uid, int count, BulkMaterialComponent bulk)
    {
        if (count <= bulk.LargeMax)
        {
            if (HasComp<MultiHandedItemComponent>(uid))
                RemComp<MultiHandedItemComponent>(uid);

            if (TryGetHolder(uid, out var user))
                _virtual.DeleteInHandsMatching(user, uid);

            return;
        }

        EnsureComp<MultiHandedItemComponent>(uid);
        if (!TryGetHolder(uid, out var holder))
            return;

        if (HasVirtual(holder, uid))
            return;

        // Keep the stack in hand. Dropping it here deleted the pile the player had just combined.
        if (!TryComp<HandsComponent>(holder, out var hands) || hands.CountFreeHands() < 1)
            return;

        _virtual.TrySpawnVirtualItemInHand(uid, holder);
    }

    private void FitOrSpill(EntityUid uid, BulkMaterialComponent bulk, StackComponent stack, ItemComponent item)
    {
        if (!_container.TryGetContainingContainer(uid, out var container))
            return;

        if (!TryComp<StorageComponent>(container.Owner, out var storage))
            return;

        if (storage.Container is not { } storageContainer || !ListedIn(storageContainer, uid))
            return;

        if (TryPlace(uid, container.Owner, storage, item))
            return;

        var count = stack.Count;
        var kept = HighestFittingCount(uid, container.Owner, storage, item, bulk, count);
        if (kept <= 0 || kept >= count)
        {
            ApplyPresentation(uid, count, bulk, item);
            return;
        }

        var leftover = count - kept;
        if (!TrySpawnLeftover(stack, leftover, container.Owner))
        {
            ApplyPresentation(uid, count, bulk, item);
            return;
        }

        _stack.SetCount(uid, kept, stack);
        ApplyPresentation(uid, kept, bulk, item);
        TryPlace(uid, container.Owner, storage, item);
    }

    private static bool ListedIn(BaseContainer container, EntityUid uid)
    {
        var list = container.ContainedEntities;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == uid)
                return true;
        }

        return false;
    }

    private bool TryPlace(EntityUid uid, EntityUid storageUid, StorageComponent storage, ItemComponent item)
    {
        if (storage.StoredItems.TryGetValue(uid, out var location) &&
            _storage.ItemFitsInGridLocation((uid, item), (storageUid, storage), location))
        {
            Dirty(storageUid, storage);
            return true;
        }

        if (!_storage.TryGetAvailableGridSpace((storageUid, storage), (uid, item), out var spot))
            return false;

        storage.StoredItems[uid] = spot.Value;
        Dirty(storageUid, storage);
        return true;
    }

    private int HighestFittingCount(
        EntityUid uid,
        EntityUid storageUid,
        StorageComponent storage,
        ItemComponent item,
        BulkMaterialComponent bulk,
        int count)
    {
        var best = 0;
        foreach (var candidate in Candidates(bulk, count))
        {
            SetBulkShape(uid, candidate, bulk, item);
            if (!TryPlace(uid, storageUid, storage, item))
                continue;

            best = candidate;
            break;
        }

        return best;
    }

    private static IEnumerable<int> Candidates(BulkMaterialComponent bulk, int count)
    {
        yield return count;

        if (bulk.LargeMax < count)
            yield return bulk.LargeMax;

        if (bulk.MediumMax < count)
            yield return bulk.MediumMax;

        if (bulk.SmallMax < count)
            yield return bulk.SmallMax;

        if (count > 1)
            yield return 1;
    }

    private bool TrySpawnLeftover(StackComponent stack, int amount, EntityUid near)
    {
        if (amount <= 0 || !_prototype.TryIndex<StackPrototype>(stack.StackTypeId, out var proto))
            return false;

        var coords = _transform.GetMapCoordinates(near);
        if (coords.MapId == MapId.Nullspace)
            return false;

        var spawned = Spawn(proto.Spawn, coords);
        if (!TryComp<StackComponent>(spawned, out var spawnedStack))
            return !TerminatingOrDeleted(spawned);

        _stack.SetCount(spawned, amount, spawnedStack);
        return !TerminatingOrDeleted(spawned);
    }

    private void MergeNearby(EntityUid uid, BulkMaterialComponent bulk, StackComponent stack)
    {
        var xform = Transform(uid);
        if (xform.MapID == MapId.Nullspace)
            return;

        var coordinates = xform.Coordinates;

        _nearby.Clear();
        _lookup.GetEntitiesInRange(coordinates, bulk.MergeRange, _nearby, LookupFlags.Uncontained);

        foreach (var other in _nearby)
        {
            if (other.Owner == uid)
                continue;

            if (!TryComp<StackComponent>(other.Owner, out var otherStack))
                continue;

            if (otherStack.StackTypeId != stack.StackTypeId)
                continue;

            if (TerminatingOrDeleted(other.Owner) || EntityManager.IsQueuedForDeletion(other.Owner))
                continue;

            _stack.TryAdd(other.Owner, uid, otherStack, stack);

            if (TerminatingOrDeleted(uid) || stack.Count >= _stack.GetMaxCount(stack))
                break;
        }
    }

    private List<Box2i> ShapeFor(int count, BulkMaterialComponent bulk)
    {
        if (count <= bulk.SmallMax)
            return Shape1X1;

        if (count <= bulk.MediumMax)
            return Shape2X2;

        if (count <= bulk.LargeMax)
            return Shape2X3;

        return Shape3X3;
    }

    private void RefreshCarrier(EntityUid mob)
    {
        // The slowdown component is replicated. Adding it on the client during state
        // application crashes prediction reset.
        if (!_net.IsServer)
            return;

        if (!TryComp<MovementSpeedModifierComponent>(mob, out _))
            return;

        var modifier = TryFindHeavy(mob, 0, out var heavy) ? heavy.HeavySpeedModifier : 1f;

        if (modifier >= 1f)
        {
            if (!TryComp<BulkCarrySlowdownComponent>(mob, out var existing))
                return;

            if (existing.Modifier < 1f)
            {
                existing.Modifier = 1f;
                Dirty(mob, existing);
                _movement.RefreshMovementSpeedModifiers(mob);
            }

            RemComp<BulkCarrySlowdownComponent>(mob);
            return;
        }

        var slow = EnsureComp<BulkCarrySlowdownComponent>(mob);
        if (MathHelper.CloseTo(slow.Modifier, modifier))
            return;

        slow.Modifier = modifier;
        Dirty(mob, slow);
        _movement.RefreshMovementSpeedModifiers(mob);
    }

    private bool TryFindHeavy(EntityUid uid, int depth, [NotNullWhen(true)] out BulkMaterialComponent? bulk)
    {
        bulk = null;
        if (depth > 6)
            return false;

        if (TryComp<StackComponent>(uid, out var stack) &&
            TryComp<BulkMaterialComponent>(uid, out var found) &&
            stack.Count > found.LargeMax)
        {
            bulk = found;
            return true;
        }

        if (!TryComp<ContainerManagerComponent>(uid, out var manager))
            return false;

        foreach (var container in manager.Containers.Values)
        {
            foreach (var contained in container.ContainedEntities)
            {
                if (depth > 0 && HasComp<HandsComponent>(contained) && HasComp<MovementSpeedModifierComponent>(contained))
                    continue;

                if (TryFindHeavy(contained, depth + 1, out bulk))
                    return true;
            }
        }

        return false;
    }

    private bool TryGetHolder(EntityUid uid, out EntityUid user)
    {
        user = default;
        if (!_container.TryGetContainingContainer(uid, out var container))
            return false;

        if (!_hands.IsHolding(container.Owner, uid))
            return false;

        user = container.Owner;
        return true;
    }

    private bool HasVirtual(EntityUid user, EntityUid item)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (TryComp<VirtualItemComponent>(held, out var virt) && virt.BlockingEntity == item)
                return true;
        }

        return false;
    }

    private EntityUid? FindMob(EntityUid start)
    {
        var current = start;
        for (var i = 0; i < 8; i++)
        {
            if (HasComp<MovementSpeedModifierComponent>(current))
                return current;

            if (!_container.TryGetContainingContainer(current, out var container))
                return null;

            current = container.Owner;
        }

        return null;
    }

    private void Track(EntityUid uid)
    {
        if (!_net.IsServer || !Exists(uid))
            return;

        if (_looseSet.Add(uid))
            _loose.Enqueue(uid);
    }

    private void Untrack(EntityUid uid)
    {
        _looseSet.Remove(uid);
    }
}
