using Content.Shared._WF.Xenobiology.Components;
using Content.Shared.Coordinates;
using Content.Shared.Destructible;
using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Timing;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenobiology.Systems;

/// <summary>
/// A vacuum nozzle that sucks slimes into a worn tank and blows them back out.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class XenoVacuumComponent : Component
{
    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Effects/zzzt.ogg");

    [DataField]
    public SoundSpecifier? ClearSound = new SoundPathSpecifier("/Audio/Effects/trashbag3.ogg");

    [DataField]
    public EntityWhitelist? EntityWhitelist;
}

/// <summary>
/// The tank a xenovac nozzle stores mobs in.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoVacuumTankComponent : Component
{
    [DataField]
    public string TankContainerName = "StorageTank";

    [ViewVariables]
    public Container StorageTank = default!;

    [DataField]
    public int MaxEntities = 5;

    [DataField, AutoNetworkedField]
    public EntityUid? LinkedNozzle;
}

/// <summary>
/// A mob the xenovac may suck up even though it isn't a slime.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class XenoVacTargetComponent : Component;

/// <summary>
/// Sucks mobs into a xenovac tank and throws them back out; the server pauses their NPC while stored.
/// </summary>
public abstract partial class SharedXenoVacuumSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private ThrowingSystem _throw = default!;
    [Dependency] private UseDelaySystem _useDelay = default!;

    private const string ReleaseDelayId = "release";
    private const string SuctionDelayId = "suction";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<XenoVacuumTankComponent, ComponentInit>(OnTankInit);
        SubscribeLocalEvent<XenoVacuumTankComponent, ExaminedEvent>(OnTankExamined);
        SubscribeLocalEvent<XenoVacuumTankComponent, DestructionEventArgs>(OnTankDestroyed);
        SubscribeLocalEvent<XenoVacuumComponent, GotEquippedHandEvent>(OnEquippedHand);
        SubscribeLocalEvent<XenoVacuumComponent, GotUnequippedHandEvent>(OnUnequippedHand);
        SubscribeLocalEvent<XenoVacuumComponent, AfterInteractEvent>(OnAfterInteract);
    }

    private void OnTankInit(Entity<XenoVacuumTankComponent> ent, ref ComponentInit args)
    {
        ent.Comp.StorageTank = _container.EnsureContainer<Container>(ent, ent.Comp.TankContainerName);
    }

    private void OnTankExamined(Entity<XenoVacuumTankComponent> ent, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
            args.PushMarkup(Loc.GetString("xeno-vacuum-examined", ("n", ent.Comp.StorageTank.ContainedEntities.Count)));
    }

    private void OnTankDestroyed(Entity<XenoVacuumTankComponent> ent, ref DestructionEventArgs args)
    {
        foreach (var freed in _container.EmptyContainer(ent.Comp.StorageTank))
        {
            SetHTNEnabled(freed, true, 2f);
        }
    }

    private void OnEquippedHand(Entity<XenoVacuumComponent> ent, ref GotEquippedHandEvent args)
    {
        SetTankNozzle(args.User, ent);
    }

    private void OnUnequippedHand(Entity<XenoVacuumComponent> ent, ref GotUnequippedHandEvent args)
    {
        SetTankNozzle(args.User, null);
    }

    private void OnAfterInteract(Entity<XenoVacuumComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled
            || !TryComp<UseDelayComponent>(ent, out var delay)
            || _useDelay.IsDelayed((ent, delay), SuctionDelayId)
            || _useDelay.IsDelayed((ent, delay), ReleaseDelayId))
            return;

        if (args.CanReach && args.Target is { } target && HasComp<MobStateComponent>(target))
        {
            args.Handled = true;
            TryDoSuction(args.User, target, ent);
            _useDelay.TryResetDelay((ent, delay), false, SuctionDelayId);
            return;
        }

        if (GetTank(args.User) is not { } tank || tank.Comp.StorageTank.ContainedEntities.Count == 0)
            return;

        args.Handled = true;
        var coords = args.Target?.ToCoordinates() ?? args.ClickLocation;
        foreach (var removed in _container.EmptyContainer(tank.Comp.StorageTank))
        {
            _popup.PopupClient(Loc.GetString("xeno-vacuum-clear-popup", ("ent", Identity.Entity(removed, EntityManager))),
                ent, args.User);
            _throw.TryThrow(removed, coords);
            _stun.TryParalyze(removed, TimeSpan.FromSeconds(2), true);
            SetHTNEnabled(removed, true, 2f);
        }

        _useDelay.TryResetDelay((ent, delay), false, ReleaseDelayId);
        _audio.PlayPredicted(ent.Comp.ClearSound, ent, args.User, AudioParams.Default.WithVolume(-2f));
    }

    private Entity<XenoVacuumTankComponent>? GetTank(EntityUid user)
    {
        foreach (var item in _hands.EnumerateHeld(user))
        {
            if (TryComp<XenoVacuumTankComponent>(item, out var held))
                return (item, held);
        }

        if (!_inventory.TryGetContainerSlotEnumerator(user, out var slots, SlotFlags.WITHOUT_POCKET))
            return null;

        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is { } item && TryComp<XenoVacuumTankComponent>(item, out var worn))
                return (item, worn);
        }

        return null;
    }

    private void SetTankNozzle(EntityUid user, EntityUid? nozzle)
    {
        if (GetTank(user) is not { } tank)
            return;

        tank.Comp.LinkedNozzle = nozzle;
        Dirty(tank);
    }

    private bool TryDoSuction(EntityUid user, EntityUid target, Entity<XenoVacuumComponent> vacuum)
    {
        if (GetTank(user) is not { } tank)
        {
            _popup.PopupClient(Loc.GetString("xeno-vacuum-suction-fail-no-tank-popup"), vacuum, user);
            return false;
        }

        var identity = Identity.Entity(target, EntityManager);
        if (_whitelist.IsWhitelistFail(vacuum.Comp.EntityWhitelist, target))
        {
            _popup.PopupClient(Loc.GetString("xeno-vacuum-suction-fail-invalid-entity-popup", ("ent", identity)), vacuum, user);
            return false;
        }

        if (tank.Comp.StorageTank.ContainedEntities.Count >= tank.Comp.MaxEntities)
        {
            _popup.PopupClient(Loc.GetString("xeno-vacuum-suction-fail-tank-full-popup"), vacuum, user);
            return false;
        }

        if (!_container.Insert(target, tank.Comp.StorageTank))
            return false;

        SetHTNEnabled(target, false);
        _audio.PlayPredicted(vacuum.Comp.Sound, user, user);
        _popup.PopupClient(Loc.GetString("xeno-vacuum-suction-succeed-popup", ("ent", identity)), vacuum, user);
        return true;
    }

    protected virtual void SetHTNEnabled(EntityUid uid, bool enabled, float planCooldown = 0f)
    {
    }
}
