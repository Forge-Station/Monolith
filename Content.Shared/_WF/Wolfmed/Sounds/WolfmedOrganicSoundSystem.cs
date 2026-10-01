using System;
using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._Onyx.Wounds;
using Robust.Shared.Configuration;
using Robust.Shared.Audio.Systems;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Sounds;

/// <summary>
/// Which bodies are flesh for the Bobmed sounds, and which of Bob's sounds a melee hit on flesh makes. Shared so
/// the attacker's client predicts the same hit sound the server plays.
/// </summary>
public sealed class WolfmedOrganicSoundSystem : EntitySystem
{
    /// <summary>A held weapon's hit that is mostly Piercing.</summary>
    public static readonly ProtoId<SoundCollectionPrototype> StabCollection = "WFWolfmedStab";

    /// <summary>A mostly Blunt or Slash hit from a weapon with no hit sound of its own.</summary>
    public static readonly ProtoId<SoundCollectionPrototype> MeleeCollection = "WFWolfmedMelee";

    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;

    /// <summary>
    /// Whether this is a body of flesh. The torso decides, as it does for the synthetic HUD, so a human with a
    /// cybernetic arm is flesh and an IPC is not. Anything without a body (a wall, an item) and a borg chassis
    /// are not; a body with no wound-tracked torso is flesh unless it is a silicon.
    /// </summary>
    public bool IsOrganicBody(EntityUid body)
    {
        if (!HasComp<BodyComponent>(body) || HasComp<BorgChassisComponent>(body))
            return false;

        foreach (var (part, bodyPart) in _body.GetBodyChildren(body))
        {
            if (bodyPart.PartType == BodyPartType.Torso && TryComp(part, out WoundableComponent? woundable))
                return _traits.IsOrganic((part, woundable));
        }

        return !HasComp<SiliconComponent>(body);
    }

    /// <summary>The meaty chop a super heavy hit on flesh gets over the weapon's own sound (playtest 4).</summary>
    public static readonly ProtoId<SoundCollectionPrototype> ChopCollection = "WFWolfmedChop";

    /// <summary>
    /// The sounds a melee hit plays on top of the weapon's own (playtest 4): the weapon's overlay
    /// (<see cref="WolfmedHitOverlaySoundComponent"/>, the crowbar's clang) and, for a Blunt or Slash hit at or past
    /// wolfmed.chop_sound_damage, the meaty chop. Flesh only; predicted like the hit sound itself.
    /// </summary>
    public void PlayHitOverlays(EntityUid target, EntityUid? user, EntityUid weapon, DamageSpecifier damage)
    {
        if (!IsOrganicBody(target))
            return;

        if (TryComp(weapon, out WolfmedHitOverlaySoundComponent? overlay))
            _audio.PlayPredicted(overlay.Sound, target, user);

        if (IsChop(damage))
            _audio.PlayPredicted(new SoundCollectionSpecifier(ChopCollection), target, user);
    }

    /// <summary>A hit heavy enough for the chop: Blunt plus Slash at or past wolfmed.chop_sound_damage.</summary>
    public bool IsChop(DamageSpecifier damage)
    {
        var line = _cfg.GetCVar(WolfmedCVars.ChopSoundDamage);
        if (line <= 0f)
            return false;

        var heavy = 0f;
        foreach (var (type, amount) in damage.DamageDict)
        {
            if (type is "Blunt" or "Slash")
                heavy += amount.Float();
        }

        return heavy >= line;
    }

    /// <summary>
    /// The Bob sound a melee hit on this target makes instead of today's, or null to keep today's. Only flesh
    /// changes: a chassis, a borg and a structure keep whatever they played before.
    /// </summary>
    public SoundSpecifier? GetHitSound(
        EntityUid target,
        DamageSpecifier damage,
        EntityUid weapon,
        EntityUid user,
        MeleeWeaponComponent component)
    {
        if (!IsOrganicBody(target) ||
            PickCollection(damage, weapon != user, component.HitSound != null) is not { } collection)
            return null;

        return new SoundCollectionSpecifier(collection);
    }

    /// <summary>
    /// Which collection a hit calls for. A held weapon whose hit is mostly Piercing stabs, whatever its own sound; a
    /// natural attack keeps its bite or claw. A mostly Blunt or Slash hit from a weapon that names no sound of its own
    /// gets the standard melee sound; one that names a sound keeps it. A hit that did no damage keeps the weapon's.
    /// </summary>
    public static ProtoId<SoundCollectionPrototype>? PickCollection(
        DamageSpecifier damage,
        bool heldWeapon,
        bool ownHitSound)
    {
        if (damage.GetTotal() <= FixedPoint2.Zero || GetLargestType(damage) is not { } type)
            return null;

        return type switch
        {
            "Piercing" when heldWeapon => StabCollection,
            "Blunt" or "Slash" when !ownHitSound => MeleeCollection,
            _ => (ProtoId<SoundCollectionPrototype>?) null,
        };
    }

    /// <summary>The damage type carrying the most of the hit, or null for a hit with nothing positive in it.</summary>
    public static string? GetLargestType(DamageSpecifier damage)
    {
        string? largest = null;
        var most = FixedPoint2.Zero;
        foreach (var (type, amount) in damage.DamageDict)
        {
            if (amount <= most)
                continue;

            most = amount;
            largest = type;
        }

        return largest;
    }
}
