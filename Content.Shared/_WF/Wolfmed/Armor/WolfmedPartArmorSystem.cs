using Content.Shared.Armor;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Content.Shared._Onyx.Wounds;

namespace Content.Shared._WF.Wolfmed.Armor;

/// <summary>Armours a wound host's localized damage on the equipped item, once routing resolved the struck part.</summary>
/// <remarks>
/// HOOK 10's other half. SharedArmorSystem armours only the systemic types for a wound host and returns early,
/// so the localized types (Blunt, Slash, Piercing, Heat, Cold, Shock, Caustic) would otherwise reach the part
/// unarmoured. WoundDamageRoutingSystem relays PartDamageModifyEvent to the wearer's inventory; this system is
/// the only subscriber of that relayed pair. Lives in _WF rather than inside SharedArmorSystem so the upstream
/// file keeps exactly the edit PLAN §3 HOOK 10 authorises — and because Onyx's own registration site
/// (SharedArmorSystem.cs:30) would be a duplicate directed subscription and crash the server at start.
/// P3-D5 (Option B): an armour may declare an ordered PartModifiers list whose first match wins, and its global
/// Modifiers reach a part only when Coverage/CoverageSymmetry admit it. Both branches wrap PenetrateArmor (D23)
/// or every AP weapon silently stops working. Onyx's MaskComponent.IsToggled gate is deliberately not ported.
/// </remarks>
public sealed class WolfmedPartArmorSystem : EntitySystem
{
    /// <inheritdoc />
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ArmorComponent, InventoryRelayedEvent<PartDamageModifyEvent>>(OnPartDamageModify);
    }

    /// <summary>Applies this armour's per-part or global penetration-adjusted modifiers to the routed part's damage.</summary>
    private void OnPartDamageModify(EntityUid uid, ArmorComponent component, InventoryRelayedEvent<PartDamageModifyEvent> args)
    {
        foreach (var profile in component.PartModifiers) // Onyx SharedArmorSystem.cs:114-122, first match wins.
        {
            if (profile.Parts.Count != 0 && !profile.Parts.Contains(args.Args.PartType) ||
                profile.Symmetry.Count != 0 && !profile.Symmetry.Contains(args.Args.Symmetry))
                continue;

            args.Args.Damage = DamageSpecifier.ApplyModifierSet(args.Args.Damage,
                DamageSpecifier.PenetrateArmor(profile.Modifiers, args.Args.ArmorPenetration)); // D23
            return;
        }

        // The coverage gate scopes the global fallback only, matching Onyx's <Onyx-ArmorGlobalProtection> comment;
        // moving it above the loop makes a partModifiers entry unreachable on any part outside Coverage.
        if (!Covers(component, args.Args.PartType, args.Args.Symmetry))
            return;

        args.Args.Damage = DamageSpecifier.ApplyModifierSet(args.Args.Damage,
            DamageSpecifier.PenetrateArmor(component.Modifiers, args.Args.ArmorPenetration));
    }

    /// <summary>Whether this armour's global modifiers reach the given part. Unset or empty covers everything.</summary>
    /// <remarks>Public since P6, so the coverage content pass can be asserted against the shipped prototypes.</remarks>
    public static bool Covers(ArmorComponent component, BodyPartType type, BodyPartSymmetry symmetry)
    {
        if (component.Coverage is { Count: > 0 } parts && !parts.Contains(type))
            return false;

        return component.CoverageSymmetry is not { Count: > 0 } sides || sides.Contains(symmetry);
    }
}
