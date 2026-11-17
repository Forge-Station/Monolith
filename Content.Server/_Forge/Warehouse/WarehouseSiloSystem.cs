using Content.Shared._Forge.Warehouse;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Warehouse;

public sealed class WarehouseSiloSystem : SharedWarehouseSiloSystem
{
    private static readonly TimeSpan ScanDelay = TimeSpan.FromSeconds(1);
    private const int MaxEntitiesToInsert = 20;

    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery;
    private TimeSpan _nextScan;

    public override void Initialize()
    {
        base.Initialize();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        SubscribeLocalEvent<WarehouseSiloComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WarehouseSiloComponent, WarehouseSiloWithdrawMessage>(OnWithdraw);
        Subs.BuiEvents<WarehouseSiloComponent>(WarehouseSiloUiKey.Key,
            subs =>
            {
                subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            });
    }

    private void OnUiOpened(Entity<WarehouseSiloComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnInteractUsing(Entity<WarehouseSiloComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryInsertItem(ent, args.Used, args.User);
    }

    private void OnWithdraw(Entity<WarehouseSiloComponent> ent, ref WarehouseSiloWithdrawMessage args)
    {
        TryWithdraw(ent, args.StackType, args.Actor);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var time = _timing.CurTime;
        if (time < _nextScan)
            return;

        _nextScan = time + ScanDelay;

        var query = EntityQueryEnumerator<WarehouseSiloComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var silo, out var xform))
        {
            if (!silo.MagnetEnabled || !Power.IsPowered(uid))
                continue;

            PullNearby((uid, silo), xform);
        }
    }

    private void PullNearby(Entity<WarehouseSiloComponent> ent, TransformComponent xform)
    {
        var inserted = 0;
        foreach (var near in _lookup.GetEntitiesInRange(ent.Owner, ent.Comp.Range, LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (inserted >= MaxEntitiesToInsert)
                break;

            if (near == ent.Owner)
                continue;

            if (_transform.GetGrid(near) != xform.GridUid)
                continue;

            if (!_physicsQuery.TryComp(near, out var physics) || physics.BodyStatus != BodyStatus.OnGround)
                continue;

            if (!TryInsertItem(ent, near, user: null, playSound: false))
                continue;

            inserted++;
        }

        if (inserted > 0)
            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/id_insert.ogg"), ent.Owner);
    }

    private bool TryInsertItem(Entity<WarehouseSiloComponent> ent, EntityUid item, EntityUid? user, bool playSound = true)
    {
        if (!TryComp<StackComponent>(item, out var stack))
            return false;

        if (!Power.IsPowered(ent.Owner))
        {
            if (user != null)
                _popup.PopupEntity(Loc.GetString("warehouse-silo-unpowered"), ent, user.Value);
            return false;
        }

        if (!CanInsert(ent, item))
        {
            if (user != null)
                _popup.PopupEntity(Loc.GetString("warehouse-silo-insert-denied"), ent, user.Value);
            return false;
        }

        var count = stack.Count;
        var name = Name(item);
        InsertAmount(ent, stack.StackTypeId, count);
        Del(item);

        if (user != null)
            _popup.PopupEntity(Loc.GetString("warehouse-silo-insert", ("amount", count), ("name", name)), ent, user.Value);

        if (playSound)
            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/id_insert.ogg"), ent.Owner);

        UpdateUi(ent);
        return true;
    }

    private void TryWithdraw(Entity<WarehouseSiloComponent> ent, string stackType, EntityUid user)
    {
        if (!Power.IsPowered(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("warehouse-silo-unpowered"), ent, user);
            return;
        }

        if (!Prototypes.TryIndex<StackPrototype>(stackType, out var proto))
            return;

        if (!_hands.TryGetEmptyHand(user, out _))
        {
            _popup.PopupEntity(Loc.GetString("warehouse-silo-hands-full"), ent, user);
            return;
        }

        var spawned = Spawn(proto.Spawn, Transform(ent.Owner).Coordinates);
        if (!TryComp<StackComponent>(spawned, out var stack))
        {
            Del(spawned);
            return;
        }

        var requested = Stacks.GetMaxCount(stack);
        var amount = TakeAmount(ent, stackType, requested);
        if (amount <= 0)
        {
            Del(spawned);
            return;
        }

        Stacks.SetCount(spawned, amount);
        _hands.PickupOrDrop(user, spawned);
        _popup.PopupEntity(Loc.GetString("warehouse-silo-withdraw", ("amount", amount), ("name", Loc.GetString(proto.Name))),
            ent,
            user);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/id_swipe.ogg"), ent.Owner);
        UpdateUi(ent);
    }
}
