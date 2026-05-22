using Content.Shared.Containers.ItemSlots;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Whitelist;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;

namespace Content.Shared._Forge.Weapons.ThrowingAmmoProvider;

public sealed partial class ThrowingAmmoProviderSystem : EntitySystem
{
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedItemSystem _itemSystem = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private INetManager _netManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ThrowingAmmoProviderComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, TakeAmmoEvent>(OnTakeAmmo);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, GetAmmoCountEvent>(OnGetAmmoCount);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, InteractUsingEvent>(OnInteractUsing, before: new[] { typeof(ItemSlotsSystem) });
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, EntInsertedIntoContainerMessage>(OnSlotChanged);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, EntRemovedFromContainerMessage>(OnSlotChanged);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, ItemWieldedEvent>(OnWielded);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, ItemUnwieldedEvent>(OnUnwielded);

        SubscribeLocalEvent<CannonBoostedComponent, ThrownEvent>(OnThrown);
        SubscribeLocalEvent<CannonBoostedComponent, ThrowDoHitEvent>(OnThrowDoHit);
        SubscribeLocalEvent<CannonBoostedComponent, StopThrowEvent>(OnStopThrow);
    }

    // -- ThrowingAmmoProvider --

    private void OnMapInit(EntityUid uid, ThrowingAmmoProviderComponent comp, MapInitEvent args)
    {
        EnsureSlots(uid, comp);
        SyncState(uid, comp);
    }

    private void EnsureSlots(EntityUid uid, ThrowingAmmoProviderComponent comp)
    {
        EnsureComp<ItemSlotsComponent>(uid);

        for (var i = 0; i < comp.Capacity; i++)
        {
            var slotId = GetSlotId(comp, i);
            if (_itemSlots.TryGetSlot(uid, slotId, out _))
                continue;

            var slot = new ItemSlot(comp.SlotTemplate)
            {
                Name = string.Empty,
                Whitelist = _netManager.IsClient ? null : comp.Whitelist,
            };
            _itemSlots.AddItemSlot(uid, slotId, slot);
        }
    }

    private void OnGetAmmoCount(EntityUid uid, ThrowingAmmoProviderComponent comp, ref GetAmmoCountEvent args)
    {
        args.Count = comp.AmmoCount;
        args.Capacity = comp.Capacity;
    }

    private void OnTakeAmmo(EntityUid uid, ThrowingAmmoProviderComponent comp, TakeAmmoEvent args)
    {
        var shotsTaken = 0;
        var attemptsLeft = Math.Max(args.Shots + comp.Capacity, args.Shots);
        while (shotsTaken < args.Shots && attemptsLeft-- > 0)
        {
            var result = TryTakeLastAmmo(uid, comp, out var ent);
            if (result == TakeAmmoResult.Stop)
                break;

            if (result == TakeAmmoResult.Retry)
                continue;

            AddTakenAmmo(ent, comp, args, args.WillBeFired);
            shotsTaken++;
        }
    }

    private void OnInteractUsing(EntityUid uid, ThrowingAmmoProviderComponent comp, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!CanUseAsAmmo(args.Used, comp))
            return;

        if (TryBlockPacifiedLoading(args))
            return;

        for (var i = 0; i < comp.Capacity; i++)
        {
            var slotId = GetSlotId(comp, i);

            if (!_itemSlots.TryGetSlot(uid, slotId, out var slot) || slot.Item != null)
                continue;

            if (TryInsertAmmo(uid, slot, args.User))
            {
                args.Handled = true;
                return;
            }
        }
    }

    private void OnAttemptShoot(EntityUid uid, ThrowingAmmoProviderComponent comp, ref AttemptShootEvent args)
    {
        args.ThrowItems = true;
    }

    private void OnGunShot(EntityUid uid, ThrowingAmmoProviderComponent comp, ref GunShotEvent args)
    {
        // Server gun code already plays the shot sound after throwing the item.
        // This keeps local prediction responsive without duplicating the sound server-side.
        if (!_netManager.IsClient)
            return;

        if (TryComp<GunComponent>(uid, out var gun))
            _audio.PlayPredicted(gun.SoundGunshotModified ?? gun.SoundGunshot, uid, args.User);
    }

    private void OnSlotChanged(EntityUid uid, ThrowingAmmoProviderComponent comp, ContainerModifiedMessage args)
    {
        if (!IsOurSlot(uid, comp, args.Container))
            return;

        SyncState(uid, comp);
    }

    private void OnWielded(EntityUid uid, ThrowingAmmoProviderComponent comp, ref ItemWieldedEvent args)
    {
        UpdateHeldPrefix(uid, comp);
    }

    private void OnUnwielded(EntityUid uid, ThrowingAmmoProviderComponent comp, ref ItemUnwieldedEvent args)
    {
        UpdateHeldPrefix(uid, comp);
    }

    // -- CannonBoosted --

    private void OnThrown(EntityUid uid, CannonBoostedComponent comp, ref ThrownEvent args)
    {
        RemCompDeferred<HiddenUntilThrownComponent>(uid);

        if (comp.MinimumFlyTime <= 0f)
            return;

        if (!TryComp<ThrownItemComponent>(uid, out var thrown) || thrown.ThrownTime == null)
            return;

        var minTime = TimeSpan.FromSeconds(comp.MinimumFlyTime);
        var thrownTime = thrown.ThrownTime.Value;
        var landTime = thrown.LandTime ?? thrownTime;

        if (landTime - thrownTime >= minTime)
            return;

        thrown.LandTime = thrownTime + minTime;
        Dirty(uid, thrown);
    }

    private void OnThrowDoHit(EntityUid uid, CannonBoostedComponent comp, ref ThrowDoHitEvent args)
    {
        RemCompDeferred<HiddenUntilThrownComponent>(uid);
        RemCompDeferred<ThrowingAmmoDamageBoostComponent>(uid);
        RemCompDeferred<CannonBoostedComponent>(uid);
    }

    private void OnStopThrow(EntityUid uid, CannonBoostedComponent comp, ref StopThrowEvent args)
    {
        RemCompDeferred<HiddenUntilThrownComponent>(uid);
        RemCompDeferred<ThrowingAmmoDamageBoostComponent>(uid);
        RemCompDeferred<CannonBoostedComponent>(uid);
    }

    // -- Helpers --

    private TakeAmmoResult TryTakeLastAmmo(EntityUid uid, ThrowingAmmoProviderComponent comp, out EntityUid ent)
    {
        ent = default;

        if (!TryGetLastLoadedSlot(uid, comp, out var slot))
            return TakeAmmoResult.Stop;

        if (!TryGetContainedAmmo(slot, out ent, out var container))
            return TakeAmmoResult.Stop;

        return _containers.Remove(ent, container)
            ? TakeAmmoResult.Success
            : TakeAmmoResult.Retry;
    }

    private bool TryGetLastLoadedSlot(
        EntityUid uid,
        ThrowingAmmoProviderComponent comp,
        [NotNullWhen(true)] out ItemSlot? slot)
    {
        for (var i = comp.Capacity - 1; i >= 0; i--)
        {
            var slotId = GetSlotId(comp, i);
            if (_itemSlots.TryGetSlot(uid, slotId, out slot) && slot.Item != null)
                return true;
        }

        slot = null;
        return false;
    }

    private static bool TryGetContainedAmmo(
        ItemSlot slot,
        out EntityUid ent,
        [NotNullWhen(true)] out ContainerSlot? container)
    {
        ent = default;
        container = slot.ContainerSlot;

        if (slot.Locked || container?.ContainedEntity == null)
            return false;

        ent = container.ContainedEntity.Value;
        return true;
    }

    private void AddTakenAmmo(EntityUid ent, ThrowingAmmoProviderComponent comp, TakeAmmoEvent args, bool willBeFired)
    {
        if (willBeFired)
        {
            EnsureComp<HiddenUntilThrownComponent>(ent);
            ApplyCannonBoost(ent, comp);
        }

        var ammo = EnsureComp<AmmoComponent>(ent);
        args.Ammo.Add((ent, ammo));
    }

    private void ApplyCannonBoost(EntityUid uid, ThrowingAmmoProviderComponent comp)
    {
        var boosted = EnsureComp<CannonBoostedComponent>(uid);
        boosted.MinimumFlyTime = comp.MinimumThrowFlyTime;
        Dirty(uid, boosted);

        var damageBoost = EnsureComp<ThrowingAmmoDamageBoostComponent>(uid);
        damageBoost.DamageMultiplier = comp.DamageMultiplier;
    }

    private void SyncState(EntityUid uid, ThrowingAmmoProviderComponent comp)
    {
        var count = 0;
        for (var i = 0; i < comp.Capacity; i++)
        {
            var slotId = GetSlotId(comp, i);
            if (_itemSlots.TryGetSlot(uid, slotId, out var slot) && slot.Item != null)
                count++;
        }

        comp.AmmoCount = count;
        Dirty(uid, comp);

        UpdateAppearance(uid, comp);

        if (HasComp<ItemComponent>(uid))
            UpdateHeldPrefix(uid, comp);

        var updateAmmo = new UpdateClientAmmoEvent();
        RaiseLocalEvent(uid, ref updateAmmo);
    }

    private void UpdateAppearance(EntityUid uid, ThrowingAmmoProviderComponent comp)
    {
        if (!TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        _appearance.SetData(uid, AmmoVisuals.AmmoCount, comp.AmmoCount, appearance);
        _appearance.SetData(uid, AmmoVisuals.AmmoMax, comp.Capacity, appearance);
        _appearance.SetData(uid, AmmoVisuals.HasAmmo, comp.AmmoCount > 0, appearance);
    }

    private void UpdateHeldPrefix(EntityUid uid, ThrowingAmmoProviderComponent comp)
    {
        var isWielded = TryComp<WieldableComponent>(uid, out var wieldable) && wieldable.Wielded;

        string? prefix = null;
        if (isWielded)
            prefix = comp.AmmoCount > 0 ? "wielded-loaded" : "wielded";

        _itemSystem.SetHeldPrefix(uid, prefix, true);
    }

    private bool IsOurSlot(EntityUid uid, ThrowingAmmoProviderComponent comp, BaseContainer container)
    {
        for (var i = 0; i < comp.Capacity; i++)
        {
            var slotId = GetSlotId(comp, i);
            if (_itemSlots.TryGetSlot(uid, slotId, out var slot) && slot.ContainerSlot == container)
                return true;
        }
        return false;
    }

    private bool CanUseAsAmmo(EntityUid uid, ThrowingAmmoProviderComponent comp)
    {
        if (!HasComp<ItemComponent>(uid))
            return false;

        if (comp.Whitelist == null)
            return true;

        // DamageOtherOnHit is server-only in this fork, so the client cannot validate this whitelist.
        // Let the client predict insertion; the server slot whitelist remains authoritative.
        return _netManager.IsClient || _whitelist.IsValid(comp.Whitelist, uid);
    }

    private bool TryBlockPacifiedLoading(InteractUsingEvent args)
    {
        if (!HasComp<PacifiedComponent>(args.User))
            return false;

        var ev = new AttemptPacifiedThrowEvent(args.Used, args.User);
        RaiseLocalEvent(args.Used, ref ev);
        if (!ev.Cancelled)
            return false;

        args.Handled = true;

        var cannotUseMessage = ev.CancelReasonMessageId ?? "pacified-cannot-throw";
        var itemName = Identity.Entity(args.Used, EntityManager);
        _popup.PopupEntity(Loc.GetString(cannotUseMessage, ("projectile", itemName)), args.Used, args.User);

        return true;
    }

    private bool TryInsertAmmo(EntityUid uid, ItemSlot slot, EntityUid user)
    {
        if (!_netManager.IsClient)
            return _itemSlots.TryInsertFromHand(uid, slot, user, excludeUserAudio: true);

        var whitelist = slot.Whitelist;
        slot.Whitelist = null;
        try
        {
            return _itemSlots.TryInsertFromHand(uid, slot, user, excludeUserAudio: true);
        }
        finally
        {
            slot.Whitelist = whitelist;
        }
    }

    private static string GetSlotId(ThrowingAmmoProviderComponent comp, int index)
    {
        return $"{comp.ContainerId}_{index}";
    }

    private enum TakeAmmoResult : byte
    {
        Stop,
        Retry,
        Success,
    }
}
