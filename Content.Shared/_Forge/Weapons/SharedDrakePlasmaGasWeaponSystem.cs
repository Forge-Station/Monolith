using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._Forge.Weapons;

public abstract partial class SharedDrakePlasmaGasWeaponSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    /// <summary>Ignore trace amounts when distinguishing empty tanks from wrong gas.</summary>
    private const float TankMoleThreshold = 0.01f;

    private (EntityUid User, string LocKey, TimeSpan Time)? _lastPopup;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DrakePlasmaGasWeaponComponent, InteractUsingEvent>(OnInteractUsing, before: [typeof(ItemSlotsSystem)]);
        SubscribeLocalEvent<DrakePlasmaGasWeaponComponent, ContainerIsInsertingAttemptEvent>(OnContainerInserting);
        SubscribeLocalEvent<DrakePlasmaGasWeaponComponent, ItemSlotInsertAttemptEvent>(OnItemSlotInsertAttempt);
        SubscribeLocalEvent<DrakePlasmaGasWeaponComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<DrakePlasmaGasWeaponComponent, TakeAmmoEvent>(OnTakeAmmo, after: [typeof(SharedGunSystem)]);
        SubscribeLocalEvent<DrakePlasmaGasWeaponComponent, ComponentInit>(OnPlasmaWeaponInit);
        SubscribeLocalEvent<DrakePlasmaGasWeaponComponent, EntInsertedIntoContainerMessage>(OnTankContainerChanged);
        SubscribeLocalEvent<DrakePlasmaGasWeaponComponent, EntRemovedFromContainerMessage>(OnTankContainerChanged);
    }

    private void OnPlasmaWeaponInit(EntityUid uid, DrakePlasmaGasWeaponComponent component, ComponentInit args)
    {
        UpdateTankAppearance(uid, component);
    }

    private void OnTankContainerChanged(EntityUid uid, DrakePlasmaGasWeaponComponent component, ContainerModifiedMessage args)
    {
        if (args.Container.ID != DrakePlasmaGasWeaponComponent.TankSlotId)
            return;

        UpdateTankAppearance(uid, component);
    }

    private void UpdateTankAppearance(EntityUid uid, DrakePlasmaGasWeaponComponent component)
    {
        if (!TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        _appearance.SetData(uid, DrakePlasmaTankVisuals.TankLoaded, GetGasTank(uid, component) != null, appearance);
    }

    private void OnInteractUsing(EntityUid uid, DrakePlasmaGasWeaponComponent component, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<GasTankComponent>(args.Used, out var gasTank))
            return;

        if (GetTankRejectionReason(args.Used, gasTank, component) is not { } reason)
            return;

        ShowPlasmaPopup(args.User, reason);
        args.Handled = true;
    }

    private void OnContainerInserting(EntityUid uid, DrakePlasmaGasWeaponComponent component, ContainerIsInsertingAttemptEvent args)
    {
        if (args.Container.ID != DrakePlasmaGasWeaponComponent.TankSlotId)
            return;

        if (!TryComp<GasTankComponent>(args.EntityUid, out var gasTank))
            return;

        if (GetTankRejectionReason(args.EntityUid, gasTank, component) == null)
            return;

        args.Cancel();
    }

    private void OnItemSlotInsertAttempt(EntityUid uid, DrakePlasmaGasWeaponComponent component, ref ItemSlotInsertAttemptEvent args)
    {
        if (args.Cancelled || args.Slot.ID != DrakePlasmaGasWeaponComponent.TankSlotId)
            return;

        if (!TryComp<GasTankComponent>(args.Item, out var gasTank))
            return;

        if (GetTankRejectionReason(args.Item, gasTank, component) is not { } reason)
            return;

        args.Cancelled = true;
        ShowPlasmaPopup(args.User, reason);
    }

    private void OnAttemptShoot(EntityUid uid, DrakePlasmaGasWeaponComponent component, ref AttemptShootEvent args)
    {
        if (args.Cancelled || component.GasUsage <= 0f)
            return;

        if (HasUsablePlasma(uid, component))
            return;

        args.Cancelled = true;

        var tank = GetGasTank(uid, component);
        if (tank == null)
        {
            args.Message = Loc.GetString("drake-plasma-missing");
            return;
        }

        args.Message = Loc.GetString(GetTankRejectionReason(tank.Value.Owner, tank.Value.Comp, component) ?? "drake-plasma-empty");
    }

    /// <summary>
    /// Strips ammo spawned by <see cref="BasicEntityAmmoProviderComponent"/> when plasma is unavailable.
    /// </summary>
    private void OnTakeAmmo(EntityUid uid, DrakePlasmaGasWeaponComponent component, TakeAmmoEvent args)
    {
        if (!args.WillBeFired || component.GasUsage <= 0f || args.Ammo.Count == 0)
            return;

        if (HasUsablePlasma(uid, component))
            return;

        var taken = args.Ammo.Count;
        foreach (var (ent, _) in args.Ammo)
        {
            if (ent != null)
                Del(ent.Value);
        }

        args.Ammo.Clear();

        if (TryComp<BasicEntityAmmoProviderComponent>(uid, out var basic) && basic.Count != null)
        {
            basic.Count = Math.Min(basic.Count.Value + taken, basic.Capacity ?? int.MaxValue);
            Dirty(uid, basic);
        }
    }

    private bool HasUsablePlasma(EntityUid uid, DrakePlasmaGasWeaponComponent component)
    {
        var gas = GetGasTank(uid, component);
        return gas != null && GetPlasmaMoles(gas.Value) >= component.GasUsage;
    }

    protected Entity<GasTankComponent>? GetGasTank(EntityUid uid, DrakePlasmaGasWeaponComponent component)
    {
        if (!_container.TryGetContainer(uid, DrakePlasmaGasWeaponComponent.TankSlotId, out var container)
            || container is not ContainerSlot slot
            || slot.ContainedEntity is not { } item
            || !TryComp<GasTankComponent>(item, out var gasTank))
        {
            return null;
        }

        return (item, gasTank);
    }

    protected static float GetPlasmaMoles(Entity<GasTankComponent> gas)
    {
        return gas.Comp.Air?.GetMoles(Gas.Plasma) ?? 0f;
    }

    protected string? GetTankRejectionReason(EntityUid tank, GasTankComponent gas, DrakePlasmaGasWeaponComponent component)
    {
        if (!_tag.HasTag(tank, "DrakePlasmaTank"))
            return "drake-plasma-wrong-tank";

        var plasma = GetPlasmaMoles((tank, gas));
        if (plasma >= component.GasUsage)
            return null;

        var air = gas.Air;
        if (air == null || air.TotalMoles < TankMoleThreshold)
            return "drake-plasma-empty";

        if (plasma < TankMoleThreshold)
            return "drake-plasma-wrong-gas";

        return "drake-plasma-empty";
    }

    private void ShowPlasmaPopup(EntityUid? user, string locKey)
    {
        if (user == null)
            return;

        var now = _timing.CurTime;
        if (_lastPopup is { } last
            && last.User == user
            && last.LocKey == locKey
            && now - last.Time < TimeSpan.FromMilliseconds(500))
        {
            return;
        }

        _lastPopup = (user.Value, locKey, now);

        var message = Loc.GetString(locKey);

        if (_net.IsServer)
        {
            _popup.PopupCursor(message, user.Value, PopupType.Medium);
            return;
        }

        if (_timing.IsFirstTimePredicted)
            _popup.PopupPredictedCursor(message, user.Value, PopupType.Medium);
    }
}
