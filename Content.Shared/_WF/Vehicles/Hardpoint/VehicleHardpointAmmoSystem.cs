using System;
using System.Collections.Generic;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._WF.Vehicles.Hardpoint;

/// <summary>One magazine of a hardpoint gun: the ready slot is the one in the gun, the others are the reserve.</summary>
public readonly record struct VehicleAmmoSlotState(
    int SlotIndex,
    int Rounds,
    int Capacity,
    bool IsReadySlot);

/// <summary>Raised on a hardpoint gun when the rounds it carries change.</summary>
public readonly record struct VehicleAmmoChangedEvent(EntityUid AmmoProvider);

/// <summary>Keeps a vehicle gun's reserve magazines and chambers the next one when the ready magazine runs dry.</summary>
public sealed partial class VehicleHardpointAmmoSystem : EntitySystem
{
    [Dependency] private SharedGunSystem _gun = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<VehicleHardpointAmmoComponent, TakeAmmoEvent>(OnTakeAmmo, after: new[] { typeof(SharedGunSystem) });
        SubscribeLocalEvent<VehicleHardpointAmmoComponent, OnEmptyGunShotEvent>(OnEmptyGunShot);
    }

    private void OnTakeAmmo(Entity<VehicleHardpointAmmoComponent> ent, ref TakeAmmoEvent args)
    {
        if (!TryComp(ent.Owner, out BallisticAmmoProviderComponent? ammo))
            return;

        if (ammo.Count > 0)
            return;

        NormalizeAmmoQueue(ent, ammo);
    }

    private void OnEmptyGunShot(Entity<VehicleHardpointAmmoComponent> ent, ref OnEmptyGunShotEvent args)
    {
        if (!TryComp(ent.Owner, out BallisticAmmoProviderComponent? ammo))
            return;

        if (ammo.Count > 0)
            return;

        NormalizeAmmoQueue(ent, ammo);
    }

    /// <summary>Squeezes the reserve together and fills the ready magazine from it.</summary>
    public bool NormalizeAmmoQueue(Entity<VehicleHardpointAmmoComponent> ent, BallisticAmmoProviderComponent ammo)
    {
        if (ammo.Count > 0)
            return false;

        return TryChamberNextMagazine(ent, ammo);
    }

    /// <summary>Chambers the next reserve magazine once the ready one is empty.</summary>
    public bool TryChamberNextMagazine(Entity<VehicleHardpointAmmoComponent> ent, BallisticAmmoProviderComponent ammo)
    {
        var magazineSize = GetMagazineSize(ent.Comp, ammo);
        var slots = GetStoredRoundSlots(ent.Comp, magazineSize);
        var reserveSlot = -1;
        for (var i = 0; i < slots.Count; i++)
        {
            if (slots[i] <= 0)
                continue;

            reserveSlot = i;
            break;
        }

        if (reserveSlot < 0)
            return false;

        var chamberSize = Math.Min(magazineSize, slots[reserveSlot]);
        var updatedSlots = new int[slots.Count];
        for (var i = 0; i < slots.Count; i++)
            updatedSlots[i] = slots[i];

        updatedSlots[reserveSlot] -= chamberSize;
        CompactStoredRoundSlots(ent, updatedSlots, magazineSize);

        _gun.SetBallisticUnspawned((ent.Owner, ammo), chamberSize);
        RaiseAmmoChanged(ent.Owner);
        return true;
    }

    /// <summary>The ready magazine and reserve slots of a gun, with their rounds.</summary>
    public List<VehicleAmmoSlotState> GetAmmoQueueSlots(
        VehicleHardpointAmmoComponent hardpointAmmo,
        BallisticAmmoProviderComponent ammo)
    {
        var magazineSize = GetMagazineSize(hardpointAmmo, ammo);
        var entries = new List<VehicleAmmoSlotState>(Math.Max(1, hardpointAmmo.MaxStoredMagazines));
        entries.Add(new VehicleAmmoSlotState(
            0,
            Math.Clamp(ammo.Count, 0, magazineSize),
            magazineSize,
            true));

        var reserveSlots = GetStoredRoundSlots(hardpointAmmo, magazineSize);
        for (var i = 0; i < reserveSlots.Count; i++)
        {
            entries.Add(new VehicleAmmoSlotState(
                i + 1,
                reserveSlots[i],
                magazineSize,
                false));
        }

        return entries;
    }

    /// <summary>Whether a reserve slot has room for more rounds.</summary>
    public bool HasLoadSpace(
        VehicleHardpointAmmoComponent hardpointAmmo,
        BallisticAmmoProviderComponent ammo,
        int ammoSlot)
    {
        if (ammoSlot < 0)
            return false;

        var magazineSize = GetMagazineSize(hardpointAmmo, ammo);
        if (ammoSlot == 0)
            return Math.Clamp(ammo.Count, 0, magazineSize) < magazineSize;

        var reserveSlot = ammoSlot - 1;
        if (reserveSlot >= GetMaxStoredRoundSlots(hardpointAmmo))
            return false;

        return GetStoredSlotRounds(hardpointAmmo, reserveSlot, magazineSize) < magazineSize;
    }

    /// <summary>Whether a reserve slot holds rounds to take out.</summary>
    public bool HasUnloadRounds(
        VehicleHardpointAmmoComponent hardpointAmmo,
        BallisticAmmoProviderComponent ammo,
        int ammoSlot)
    {
        if (ammoSlot < 0)
            return false;

        var magazineSize = GetMagazineSize(hardpointAmmo, ammo);
        if (ammoSlot == 0)
            return Math.Min(Math.Clamp(ammo.Count, 0, magazineSize), ammo.UnspawnedCount) > 0;

        var reserveSlot = ammoSlot - 1;
        if (reserveSlot >= GetMaxStoredRoundSlots(hardpointAmmo))
            return false;

        return GetStoredSlotRounds(hardpointAmmo, reserveSlot, magazineSize) > 0;
    }

    /// <summary>How many rounds a load into a slot would move.</summary>
    public int GetLoadAmount(
        VehicleHardpointAmmoComponent hardpointAmmo,
        BallisticAmmoProviderComponent ammo,
        int ammoSlot,
        int availableRounds)
    {
        if (ammoSlot < 0 || availableRounds <= 0)
            return 0;

        var magazineSize = GetMagazineSize(hardpointAmmo, ammo);
        if (ammoSlot == 0)
        {
            var chambered = Math.Clamp(ammo.Count, 0, magazineSize);
            var chamberSpace = magazineSize - chambered;
            return chamberSpace <= 0 ? 0 : Math.Min(availableRounds, chamberSpace);
        }

        var reserveSlot = ammoSlot - 1;
        if (reserveSlot >= GetMaxStoredRoundSlots(hardpointAmmo))
            return 0;

        var storedRounds = GetStoredSlotRounds(hardpointAmmo, reserveSlot, magazineSize);
        var reserveSpace = magazineSize - storedRounds;
        return reserveSpace <= 0 ? 0 : Math.Min(availableRounds, reserveSpace);
    }

    /// <summary>How many rounds an unload from a slot would move.</summary>
    public int GetUnloadAmount(
        VehicleHardpointAmmoComponent hardpointAmmo,
        BallisticAmmoProviderComponent ammo,
        int ammoSlot)
    {
        if (ammoSlot < 0)
            return 0;

        var magazineSize = GetMagazineSize(hardpointAmmo, ammo);
        if (ammoSlot == 0)
            return Math.Min(Math.Clamp(ammo.Count, 0, magazineSize), ammo.UnspawnedCount);

        var reserveSlot = ammoSlot - 1;
        if (reserveSlot >= GetMaxStoredRoundSlots(hardpointAmmo))
            return 0;

        return GetStoredSlotRounds(hardpointAmmo, reserveSlot, magazineSize);
    }

    /// <summary>Loads rounds into a reserve slot.</summary>
    public bool TryLoadIntoSlot(
        Entity<VehicleHardpointAmmoComponent> ent,
        BallisticAmmoProviderComponent ammo,
        int ammoSlot,
        int rounds)
    {
        var amount = GetLoadAmount(ent.Comp, ammo, ammoSlot, rounds);
        if (amount <= 0)
            return false;

        var magazineSize = GetMagazineSize(ent.Comp, ammo);
        if (ammoSlot == 0)
        {
            _gun.SetBallisticUnspawned((ent.Owner, ammo), ammo.UnspawnedCount + amount);
            RaiseAmmoChanged(ent.Owner);
        }
        else
        {
            var reserveSlot = ammoSlot - 1;
            var storedRounds = GetStoredSlotRounds(ent.Comp, reserveSlot, magazineSize);
            SetStoredSlotRounds(ent, reserveSlot, storedRounds + amount, magazineSize);
        }

        return true;
    }

    /// <summary>Takes rounds out of a reserve slot; returns how many.</summary>
    public int TryUnloadFromSlot(
        Entity<VehicleHardpointAmmoComponent> ent,
        BallisticAmmoProviderComponent ammo,
        int ammoSlot,
        int maxRounds)
    {
        var amount = Math.Min(GetUnloadAmount(ent.Comp, ammo, ammoSlot), Math.Max(0, maxRounds));
        if (amount <= 0)
            return 0;

        var magazineSize = GetMagazineSize(ent.Comp, ammo);
        if (ammoSlot == 0)
        {
            _gun.SetBallisticUnspawned((ent.Owner, ammo), ammo.UnspawnedCount - amount);
            NormalizeAmmoQueue(ent, ammo);
            RaiseAmmoChanged(ent.Owner);
        }
        else
        {
            var reserveSlot = ammoSlot - 1;
            var storedRounds = GetStoredSlotRounds(ent.Comp, reserveSlot, magazineSize);
            SetStoredSlotRounds(ent, reserveSlot, storedRounds - amount, magazineSize);
        }

        return amount;
    }

    /// <summary>The size of one magazine of the gun.</summary>
    public int GetMagazineSize(VehicleHardpointAmmoComponent hardpointAmmo, BallisticAmmoProviderComponent ammo)
    {
        var magazineSize = Math.Max(1, hardpointAmmo.MagazineSize);
        var capacity = Math.Max(1, ammo.Capacity);
        return Math.Min(magazineSize, capacity);
    }

    /// <summary>The rounds in the whole reserve.</summary>
    public int GetStoredRounds(VehicleHardpointAmmoComponent hardpointAmmo, int magazineSize)
    {
        if (hardpointAmmo.StoredRoundSlots.Count > 0)
        {
            var total = 0;
            var slots = Math.Min(hardpointAmmo.StoredRoundSlots.Count, GetMaxStoredRoundSlots(hardpointAmmo));
            for (var i = 0; i < slots; i++)
                total += Math.Clamp(hardpointAmmo.StoredRoundSlots[i], 0, Math.Max(1, magazineSize));

            return total;
        }

        return GetStoredRoundsFallback(hardpointAmmo, magazineSize);
    }

    /// <summary>The rounds the whole reserve holds when full.</summary>
    public int GetMaxStoredRounds(VehicleHardpointAmmoComponent hardpointAmmo, int magazineSize)
    {
        return GetMaxStoredRoundSlots(hardpointAmmo) * Math.Max(1, magazineSize);
    }

    /// <summary>How many reserve slots the gun has.</summary>
    public int GetMaxStoredRoundSlots(VehicleHardpointAmmoComponent hardpointAmmo)
    {
        return Math.Max(0, hardpointAmmo.MaxStoredMagazines - 1);
    }

    /// <summary>The rounds in each reserve slot.</summary>
    public IReadOnlyList<int> GetStoredRoundSlots(VehicleHardpointAmmoComponent hardpointAmmo, int magazineSize)
    {
        var maxSlots = GetMaxStoredRoundSlots(hardpointAmmo);
        var capacity = Math.Max(1, magazineSize);
        var slots = new int[maxSlots];

        if (hardpointAmmo.StoredRoundSlots.Count > 0)
        {
            var copy = Math.Min(maxSlots, hardpointAmmo.StoredRoundSlots.Count);
            for (var i = 0; i < copy; i++)
                slots[i] = Math.Clamp(hardpointAmmo.StoredRoundSlots[i], 0, capacity);

            return slots;
        }

        var remaining = Math.Clamp(GetStoredRoundsFallback(hardpointAmmo, magazineSize), 0, maxSlots * capacity);
        for (var i = 0; i < maxSlots && remaining > 0; i++)
        {
            slots[i] = Math.Min(capacity, remaining);
            remaining -= slots[i];
        }

        return slots;
    }

    /// <summary>The rounds in one reserve slot.</summary>
    public int GetStoredSlotRounds(VehicleHardpointAmmoComponent hardpointAmmo, int reserveSlot, int magazineSize)
    {
        var slots = GetStoredRoundSlots(hardpointAmmo, magazineSize);
        return reserveSlot < 0 || reserveSlot >= slots.Count ? 0 : slots[reserveSlot];
    }

    /// <summary>Sets the rounds of the whole reserve, filling slots in order.</summary>
    public void SetStoredRounds(Entity<VehicleHardpointAmmoComponent> ent, int rounds, int magazineSize)
    {
        var maxRounds = GetMaxStoredRounds(ent.Comp, magazineSize);
        var capacity = Math.Max(1, magazineSize);
        var remaining = Math.Clamp(rounds, 0, maxRounds);
        ent.Comp.StoredRoundSlots.Clear();

        for (var i = 0; i < GetMaxStoredRoundSlots(ent.Comp); i++)
        {
            var slotRounds = Math.Min(capacity, remaining);
            ent.Comp.StoredRoundSlots.Add(slotRounds);
            remaining -= slotRounds;
        }

        UpdateStoredRoundTotals(ent.Comp, capacity);
        Dirty(ent);
        RaiseAmmoChanged(ent.Owner);
    }

    /// <summary>Sets the rounds of one reserve slot.</summary>
    public void SetStoredSlotRounds(Entity<VehicleHardpointAmmoComponent> ent, int reserveSlot, int rounds, int magazineSize)
    {
        var maxSlots = GetMaxStoredRoundSlots(ent.Comp);
        if (reserveSlot < 0 || reserveSlot >= maxSlots)
            return;

        var capacity = Math.Max(1, magazineSize);
        var slots = GetStoredRoundSlots(ent.Comp, capacity);

        ent.Comp.StoredRoundSlots.Clear();
        for (var i = 0; i < maxSlots; i++)
        {
            var slotRounds = i < slots.Count ? slots[i] : 0;
            if (i == reserveSlot)
                slotRounds = rounds;

            ent.Comp.StoredRoundSlots.Add(Math.Clamp(slotRounds, 0, capacity));
        }

        UpdateStoredRoundTotals(ent.Comp, capacity);
        Dirty(ent);
        RaiseAmmoChanged(ent.Owner);
    }

    private static int GetStoredRoundsFallback(VehicleHardpointAmmoComponent hardpointAmmo, int magazineSize)
    {
        if (hardpointAmmo.StoredRounds > 0)
            return hardpointAmmo.StoredRounds;

        return Math.Max(0, hardpointAmmo.StoredMagazines) * Math.Max(1, magazineSize);
    }

    private void CompactStoredRoundSlots(Entity<VehicleHardpointAmmoComponent> ent, IReadOnlyList<int> slots, int magazineSize)
    {
        var maxSlots = GetMaxStoredRoundSlots(ent.Comp);
        var capacity = Math.Max(1, magazineSize);
        ent.Comp.StoredRoundSlots.Clear();

        foreach (var rounds in slots)
        {
            if (ent.Comp.StoredRoundSlots.Count >= maxSlots)
                break;

            var clamped = Math.Clamp(rounds, 0, capacity);
            if (clamped <= 0)
                continue;

            ent.Comp.StoredRoundSlots.Add(clamped);
        }

        while (ent.Comp.StoredRoundSlots.Count < maxSlots)
            ent.Comp.StoredRoundSlots.Add(0);

        UpdateStoredRoundTotals(ent.Comp, capacity);
        Dirty(ent);
        RaiseAmmoChanged(ent.Owner);
    }

    private void RaiseAmmoChanged(EntityUid ammoProvider)
    {
        var ev = new VehicleAmmoChangedEvent(ammoProvider);
        RaiseLocalEvent(ammoProvider, ev, broadcast: true);
    }

    private static void UpdateStoredRoundTotals(VehicleHardpointAmmoComponent hardpointAmmo, int magazineSize)
    {
        var total = 0;
        foreach (var rounds in hardpointAmmo.StoredRoundSlots)
            total += Math.Clamp(rounds, 0, magazineSize);

        hardpointAmmo.StoredRounds = total;
        hardpointAmmo.StoredMagazines = total / Math.Max(1, magazineSize);
    }
}
