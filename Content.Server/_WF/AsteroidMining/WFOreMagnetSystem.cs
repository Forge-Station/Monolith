using Content.Server.Power.EntitySystems;
using Content.Shared._WF.AsteroidMining;
using Content.Shared.Examine;
using Content.Shared.Item;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.AsteroidMining;

/// <summary>
/// Runs ore magnets: finds loose ore in range and in sight, locks onto it, and pulls it into storage once the lock has
/// held. A lock breaks if the ore drifts out of range or behind a wall, or the magnet loses power or is switched off.
/// </summary>
public sealed partial class WFOreMagnetSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private ItemToggleSystem _toggle = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private SharedStackSystem _stack = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    private readonly List<EntityUid> _done = new();
    private readonly HashSet<Entity<ItemComponent>> _nearby = new();
    /// <summary>Stack types the scanning magnet holds a stack of with room left.</summary>
    private readonly HashSet<string> _openStacks = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFOreMagnetComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<WFOreMagnetComponent> ent, ref ComponentShutdown args)
    {
        ReleaseAll(ent.Comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFOreMagnetComponent, StorageComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var magnet, out var storage, out var xform))
        {
            var active = xform.Anchored && _toggle.IsActivated(uid) && this.IsPowered(uid, EntityManager);
            if (active != magnet.Active)
            {
                magnet.Active = active;
                _appearance.SetData(uid, WFOreMagnetVisuals.Active, active);
                if (!active)
                    ReleaseAll(magnet);
            }

            if (!active)
                continue;

            var scan = now >= magnet.NextScan;
            UpdatePulls((uid, magnet, storage, xform), now, scan);
            if (!scan)
                continue;

            magnet.NextScan = now + magnet.ScanInterval;
            Scan((uid, magnet, storage, xform), now);
        }
    }

    /// <summary>
    /// Pulls in the ore whose lock has held, and drops any it has lost. Sight is rechecked on scans and when a lock
    /// comes due, rather than every tick.
    /// </summary>
    private void UpdatePulls(Entity<WFOreMagnetComponent, StorageComponent, TransformComponent> ent, TimeSpan now,
        bool checkSight)
    {
        var (uid, magnet, storage, xform) = ent;
        _done.Clear();
        foreach (var ore in magnet.Pulling)
        {
            // Ore picked up into a hand or bag mid-lock is no longer loose, so the lock breaks.
            if (TerminatingOrDeleted(ore) || !TryComp<WFOreMagnetPulledComponent>(ore, out var pulled) ||
                _container.IsEntityInContainer(ore))
            {
                _done.Add(ore);
                continue;
            }

            var due = now >= pulled.End;
            if ((due || checkSight) && !CanSee(uid, magnet, ore))
            {
                _done.Add(ore);
                continue;
            }

            if (!due)
                continue;

            _done.Add(ore);
            var oreXform = Transform(ore);
            var from = _transform.ToCoordinates(_transform.GetMoverCoordinates(uid, xform).EntityId,
                _transform.GetMapCoordinates(ore, oreXform));
            var rotation = oreXform.LocalRotation;
            if (_storage.Insert(uid, ore, out var stacked, storageComp: storage))
                _storage.PlayPickupAnimation(stacked ?? ore, from, xform.Coordinates, rotation);
        }

        foreach (var ore in _done)
        {
            magnet.Pulling.Remove(ore);
            if (!TerminatingOrDeleted(ore))
                RemComp<WFOreMagnetPulledComponent>(ore);
        }
    }

    /// <summary>Locks onto loose ore in range and in sight, as far as free locks and storage space allow.</summary>
    private void Scan(Entity<WFOreMagnetComponent, StorageComponent, TransformComponent> ent, TimeSpan now)
    {
        var (uid, magnet, storage, xform) = ent;
        if (magnet.Pulling.Count >= magnet.MaxPulls)
            return;

        _openStacks.Clear();
        foreach (var stored in storage.Container.ContainedEntities)
        {
            if (TryComp<StackComponent>(stored, out var storedStack) && _stack.GetAvailableSpace(storedStack) > 0)
                _openStacks.Add(storedStack.StackTypeId);
        }

        // Room left, less what the ore already locked on will take. Ore that tops up a stored stack takes none.
        var free = storage.Grid.GetArea() - _storage.GetCumulativeItemAreas((uid, storage));
        foreach (var locked in magnet.Pulling)
        {
            if (TryComp<ItemComponent>(locked, out var lockedItem) && !TopsUpStack(locked))
                free -= _item.GetItemShape((locked, lockedItem)).GetArea();
        }

        // A full magnet can only top up its stacks, so it skips the lookup when it has none with room.
        if (free <= 0 && _openStacks.Count == 0)
            return;

        var origin = _transform.GetMapCoordinates(uid, xform);
        // The smallest ore that found no slot this scan; ore as big skips the storage grid search, which is costly.
        var noRoom = int.MaxValue;
        _nearby.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, magnet.Range, _nearby, LookupFlags.Dynamic | LookupFlags.Sundries);
        foreach (var (ore, item) in _nearby)
        {
            if (magnet.Pulling.Count >= magnet.MaxPulls)
                break;

            if (HasComp<WFOreMagnetPulledComponent>(ore) ||
                !TryComp<PhysicsComponent>(ore, out var physics) || physics.BodyStatus != BodyStatus.OnGround)
                continue;

            // Cheapest first: the space it needs, then whether storage takes it, then the sight raycast.
            var size = TopsUpStack(ore) ? 0 : _item.GetItemShape((ore, item)).GetArea();
            if (size > free || (size > 0 && size >= noRoom))
                continue;

            if (!_storage.CanInsert(uid, ore, out var reason, storage, item))
            {
                if (size > 0 && reason == "comp-storage-insufficient-capacity")
                    noRoom = size;
                continue;
            }

            if (!CanSee(uid, magnet, ore))
                continue;

            free -= size;
            var distance = (_transform.GetMapCoordinates(ore).Position - origin.Position).Length();
            var pulled = EnsureComp<WFOreMagnetPulledComponent>(ore);
            pulled.Magnet = uid;
            pulled.Start = now;
            pulled.End = now + TimeSpan.FromSeconds(magnet.PullTimeBase + distance * magnet.PullTimePerMetre);
            Dirty(ore, pulled);
            magnet.Pulling.Add(ore);
        }
    }

    /// <summary>Whether the ore is in range and nothing that blocks sight, such as a wall, stands between them.</summary>
    private bool CanSee(EntityUid magnet, WFOreMagnetComponent comp, EntityUid ore)
    {
        return _examine.InRangeUnOccluded(_transform.GetMapCoordinates(magnet), _transform.GetMapCoordinates(ore), comp.Range,
            e => e == magnet || e == ore);
    }

    /// <summary>Whether a stack of the same kind in storage has room for more, so the ore needs no new slot.</summary>
    private bool TopsUpStack(EntityUid ore)
    {
        return TryComp<StackComponent>(ore, out var stack) && _openStacks.Contains(stack.StackTypeId);
    }

    private void ReleaseAll(WFOreMagnetComponent magnet)
    {
        foreach (var ore in magnet.Pulling)
        {
            if (!TerminatingOrDeleted(ore))
                RemComp<WFOreMagnetPulledComponent>(ore);
        }

        magnet.Pulling.Clear();
    }
}
