using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Armor;
using Content.Shared.Armor;
using Content.Shared.Body.Part;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Raised on a body, and relayed to its worn items, before a wound rule makes <see cref="Wound"/> on a part. Cancel
/// it to refuse the wound; raise <see cref="MinDamageMultiplier"/> to make the rule need a bigger hit.
/// </summary>
[ByRefEvent]
public record struct WolfmedWoundRuleAttemptEvent(
    EntityUid Part,
    ProtoId<WoundPrototype> Wound,
    FixedPoint2 Amount) : IInventoryRelayEvent
{
    /// <summary>The wound rule asking, or null when no rule is (acid residue deepening a burn).</summary>
    public string? Rule;
    /// <summary>What dealt the hit, so armour can answer explosions alone.</summary>
    public WolfmedWoundCause Cause;
    public bool Cancelled;
    public float MinDamageMultiplier = 1f;
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;
}

/// <summary>
/// Raised on a body, and relayed to its worn items, before a hit or a blast takes a limb off. Surgery doesn't raise it.
/// <see cref="HitDamage"/> is the amount of the damage type that would take the limb.
/// </summary>
[ByRefEvent]
public record struct WolfmedSeverAttemptEvent(EntityUid Part, FixedPoint2 HitDamage) : IInventoryRelayEvent
{
    public bool IsExplosion;
    public bool Cancelled;
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;
}

/// <summary>Raised on a body, and relayed to its worn items, before an explosion damages the organs in a part.</summary>
[ByRefEvent]
public record struct WolfmedOrganBlastAttemptEvent(EntityUid Part) : IInventoryRelayEvent
{
    public bool Cancelled;
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;
}

/// <summary>Raised on a body, and relayed to its worn items, to total the pain relief they give.</summary>
[ByRefEvent]
public record struct WolfmedArmorPainReliefEvent : IInventoryRelayEvent
{
    public float Relief;
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;
}

/// <summary>
/// Raised on a body that was hit mid-treatment, and relayed to its worn items: treatment and surgery break only on a
/// hit of the interrupt line times <see cref="Multiplier"/>.
/// </summary>
[ByRefEvent]
public record struct WolfmedSteadyHandsEvent : IInventoryRelayEvent
{
    public float Multiplier = 1f;
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;

    public WolfmedSteadyHandsEvent() { }
}

/// <summary>
/// Raised on a body, and relayed to its worn items, to ask whether they keep infection out. With a
/// <see cref="Part"/>, whether a wound on that part can go bad; without one, whether wounds this body treats start clean.
/// </summary>
[ByRefEvent]
public record struct WolfmedSterileEvent(EntityUid? Part) : IInventoryRelayEvent
{
    public bool Sterile;
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;
}

/// <summary>Raised on a body, and relayed to its worn items, before it screams or cries out in pain.</summary>
[ByRefEvent]
public record struct WolfmedPainNoiseAttemptEvent : IInventoryRelayEvent
{
    public bool Cancelled;
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;
}

/// <summary>Helpers for armour that answers the events above.</summary>
public static class WolfmedArmorCoverage
{
    /// <summary>
    /// Whether worn <paramref name="item"/> protects <paramref name="part"/>: its armour coverage when it has an
    /// <see cref="ArmorComponent"/>, otherwise every part.
    /// </summary>
    public static bool Covers(IEntityManager entities, EntityUid item, EntityUid part)
    {
        if (!entities.TryGetComponent(item, out ArmorComponent? armor) ||
            !entities.TryGetComponent(part, out BodyPartComponent? bodyPart))
            return true;

        return WolfmedPartArmorSystem.Covers(armor, bodyPart.PartType, bodyPart.Symmetry);
    }
}
