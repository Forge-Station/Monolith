using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Item;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Forge.Weapons.MeleeStance;

/// <summary>
/// One stance of a <see cref="MeleeStanceComponent"/> weapon.
/// Every nullable field means "use the value from the entity prototype" when left empty.
/// </summary>
[DataDefinition]
public sealed partial class MeleeStanceMode
{
    #region Info

    /// <summary>
    /// Name of the stance, shown in the popup and on examine.
    /// </summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    /// Optional description shown on examine while this stance is active.
    /// </summary>
    [DataField]
    public LocId? Description;

    /// <summary>
    /// Sound played when switching INTO this stance. Overrides <see cref="MeleeStanceComponent.SwitchSound"/>.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>
    /// Icon of the switch action while this stance is active.
    /// </summary>
    [DataField]
    public SpriteSpecifier? ActionIcon;

    #endregion

    #region Melee

    [DataField]
    public DamageSpecifier? Damage;

    [DataField]
    public float? AttackRate;

    [DataField]
    public float? Range;

    /// <summary>
    /// Width of the wide (heavy) attack arc.
    /// </summary>
    [DataField]
    public Angle? Angle;

    [DataField]
    public float? ArmorPenetration;

    [DataField]
    public bool? ResistanceBypass;

    [DataField]
    public bool? CanWideSwing;

    [DataField]
    public bool? AltDisarm;

    [DataField]
    public float? HeavyStaminaCost;

    [DataField]
    public FixedPoint2? ClickDamageModifier;

    [DataField]
    public float? ClickPartDamageMultiplier;

    [DataField]
    public float? HeavyPartDamageMultiplier;

    /// <summary>
    /// Hides melee stats from examine.
    /// </summary>
    [DataField]
    public bool? Hidden;

    [DataField]
    public SoundSpecifier? SoundHit;

    [DataField]
    public SoundSpecifier? SoundSwing;

    [DataField]
    public SoundSpecifier? SoundNoDamage;

    /// <summary>
    /// Light attack arc animation.
    /// </summary>
    [DataField]
    public EntProtoId? Animation;

    /// <summary>
    /// Wide attack arc animation.
    /// </summary>
    [DataField]
    public EntProtoId? WideAnimation;

    [DataField]
    public Angle? WideAnimationRotation;

    #endregion

    #region Reflect

    /// <summary>
    /// Reflect chance (0..1). Requires a <c>Reflect</c> component on the weapon.
    /// Use 0 in a stance that should not reflect at all.
    /// </summary>
    [DataField]
    public float? ReflectProb;

    /// <summary>
    /// What is reflected: Energy, NonEnergy or both.
    /// </summary>
    [DataField]
    public List<ReflectType>? Reflects;

    [DataField]
    public Angle? ReflectSpread;

    [DataField]
    public SoundSpecifier? SoundOnReflect;

    [DataField]
    public float? DamageOnReflectModifier;

    #endregion

    #region Stamina

    /// <summary>
    /// Stamina damage on hit. Requires a <c>StaminaDamageOnHit</c> component on the weapon.
    /// </summary>
    [DataField]
    public float? StaminaDamage;

    #endregion

    #region Item / visuals

    /// <summary>
    /// Item size in this stance (e.g. a folded blade fits in pockets).
    /// </summary>
    [DataField]
    public ProtoId<ItemSizePrototype>? Size;

    /// <summary>
    /// In-hand sprite prefix: states "{prefix}-inhand-left" / "{prefix}-inhand-right".
    /// If at least one stance sets a prefix, the prefix is fully managed by stances (null = no prefix).
    /// </summary>
    [DataField]
    public string? HeldPrefix;

    /// <summary>
    /// In-hand prefix used while the weapon is wielded. Null = keep the Wieldable prefix ("wielded").
    /// </summary>
    [DataField]
    public string? WieldedHeldPrefix;

    /// <summary>
    /// Clothing prefix for belt / back / suit storage sprites: "{prefix}-equipped-BELT" etc.
    /// If at least one stance sets a prefix, the prefix is fully managed by stances (null = no prefix).
    /// </summary>
    [DataField]
    public string? EquippedPrefix;

    /// <summary>
    /// RSI state for the layer <see cref="MeleeStanceComponent.SpriteLayer"/> (or layer 0).
    /// Can be an animated state.
    /// </summary>
    [DataField]
    public string? State;

    /// <summary>
    /// Arbitrary layer changes by map key: state, visible, color, shader, sprite, etc.
    /// Layers changed by any stance and not mentioned in the current one are restored to the prototype look.
    /// </summary>
    [DataField]
    public Dictionary<string, PrototypeLayerData> Layers = new();

    /// <summary>
    /// One-shot RSI state played on the stance layer when switching INTO this stance
    /// (e.g. a blade unfolding), after which <see cref="State"/> is shown.
    /// </summary>
    [DataField]
    public string? TransitionState;

    /// <summary>
    /// Length of <see cref="TransitionState"/> in seconds. Usually the sum of the state's frame delays.
    /// </summary>
    [DataField]
    public float TransitionTime;

    #endregion

    #region Light (null = don't touch)

    [DataField]
    public bool? LightEnabled;

    [DataField]
    public Color? LightColor;

    [DataField]
    public float? LightRadius;

    [DataField]
    public float? LightEnergy;

    #endregion

    /// <summary>
    /// Components that exist ONLY while this stance is active (added on enter, removed on leave). Server-side.
    /// Don't list components that are already on the base prototype - they would be removed on leave.
    /// </summary>
    [DataField]
    public ComponentRegistry? Components;
}
