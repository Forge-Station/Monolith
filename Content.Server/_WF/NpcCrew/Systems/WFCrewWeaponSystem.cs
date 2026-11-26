using System.Diagnostics.CodeAnalysis;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.NPC.Components;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// Draws a crewman's best weapon from its equipment slots into its active hand and holsters it again. Weapons live
/// in slots, never in bags, so a draw is one container move.
/// </summary>
public sealed partial class WFCrewWeaponSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    /// <summary>Slots searched for a weapon, in the order ties are broken.</summary>
    private static readonly string[] WeaponSlots = { "back", "suitstorage", "belt", "pocket1", "pocket2" };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NPCRangedCombatComponent, ComponentInit>(OnRangedInit);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _advancing.Clear();
            _threats.Clear();
        });
    }

    /// <summary>A new ranged component starts with the crewman's skill delay, so the first shot waits for it too.</summary>
    private void OnRangedInit(EntityUid uid, NPCRangedCombatComponent component, ComponentInit args)
    {
        if (TryComp<WFCrewComponent>(uid, out var crew))
            component.ShootDelay = WFCrewSkills.Of(crew.Skill).ShootDelay;
    }

    /// <summary>Whether the drawn weapon is still in hand.</summary>
    public bool IsDrawn(EntityUid uid)
    {
        return TryComp<WFCrewWeaponComponent>(uid, out var weapon)
               && weapon.Drawn is { } drawn
               && !TerminatingOrDeleted(drawn)
               && _hands.IsHolding(uid, drawn, out _);
    }

    /// <summary>
    /// Puts the best weapon in a slot into the active hand. True when a weapon is in hand afterwards, including one
    /// that was already drawn.
    /// </summary>
    public bool TryDraw(EntityUid uid)
    {
        if (IsDrawn(uid))
            return true;

        var weapon = EnsureComp<WFCrewWeaponComponent>(uid);
        weapon.Drawn = null;
        weapon.HolsterSlot = null;

        if (!FindBest(uid, out var item, out var slot))
            return false;

        // Straight out of the slot into any free hand when the containers allow it; otherwise unequip first.
        if (!_hands.TryPickupAnyHand(uid, item.Value, checkActionBlocker: false, animateUser: false, animate: false))
        {
            if (!_inventory.TryUnequip(uid, slot, silent: true, force: true))
                return false;

            if (!_hands.TryPickupAnyHand(uid, item.Value, checkActionBlocker: false, animateUser: false, animate: false))
            {
                _inventory.TryEquip(uid, item.Value, slot, silent: true, force: true);
                return false;
            }
        }

        // The combat compounds shoot with the active hand.
        _hands.TrySelect(uid, item.Value);
        weapon.Drawn = item;
        weapon.HolsterSlot = slot;
        EntityManager.System<WFCrewSpeechSystem>().Say(uid, "engage");
        return true;
    }

    /// <summary>Whether the crewman has in hand, or would draw, a gun he can fire.</summary>
    public bool HasLoadedGun(EntityUid uid)
    {
        EntityUid? gun = null;
        if (IsDrawn(uid))
            gun = Comp<WFCrewWeaponComponent>(uid).Drawn;
        else if (FindBest(uid, out var best, out _))
            gun = best;

        return gun is { } held && HasComp<GunComponent>(held) && (AmmoCount(held) > 0 || FindMagazineForOwner(held));
    }

    /// <summary>Puts the drawn weapon back into the slot it came from. Safe to call at any time.</summary>
    public void TryHolster(EntityUid uid)
    {
        if (!TryComp<WFCrewWeaponComponent>(uid, out var weapon) || weapon.Drawn is not { } drawn)
            return;

        if (TerminatingOrDeleted(drawn) || !_hands.IsHolding(uid, drawn, out _))
        {
            weapon.Drawn = null;
            weapon.HolsterSlot = null;
            return;
        }

        if (weapon.HolsterSlot is not { } slot || !_inventory.TryEquip(uid, drawn, slot, silent: true, force: true))
            return;

        weapon.Drawn = null;
        weapon.HolsterSlot = null;
    }

    private bool FindBest(EntityUid uid, [NotNullWhen(true)] out EntityUid? item, [NotNullWhen(true)] out string? slot)
    {
        item = null;
        slot = null;
        var best = 0;

        foreach (var name in WeaponSlots)
        {
            if (!_inventory.TryGetSlotEntity(uid, name, out var candidate))
                continue;

            var score = Score(candidate.Value, name);
            if (score <= best)
                continue;

            best = score;
            item = candidate;
            slot = name;
        }

        return item != null;
    }

    /// <summary>Longarms over sidearms over anything that swings; everything else is not a weapon.</summary>
    private int Score(EntityUid item, string slot)
    {
        if (HasComp<Content.Shared.Atmos.Components.GasTankComponent>(item))
            return 0;
        if (HasComp<GunComponent>(item))
            return AmmoCount(item) > 0 || FindMagazineForOwner(item) ? (slot == "back" ? 3 : 2) : 0;

        return HasComp<MeleeWeaponComponent>(item) ? 1 : 0;
    }
}
