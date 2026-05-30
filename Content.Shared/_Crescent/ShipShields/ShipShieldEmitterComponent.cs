using Content.Shared.Guidebook;
using Robust.Shared.Audio;
using Robust.Shared.GameStates; // Forge-Change

namespace Content.Shared._Crescent.ShipShields;

/// <summary>
/// Drives one ship shield field. At most one bubble exists per grid; <see cref="ShipShieldedComponent.Source"/> is the canonical emitter for radar/HUD when the bubble is up.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState] // Forge-Change: networked so HUD reads emitter directly instead of pulling via BUI refresh
public sealed partial class ShipShieldEmitterComponent : Component
{
    [ViewVariables]
    public EntityUid? Shield;

    [ViewVariables]
    public EntityUid? Shielded;

    [DataField]
    public float Accumulator;

    [DataField, AutoNetworkedField] // Forge-Change
    public float Damage = 0f;

    /// <summary>
    /// Exponential growth factor for power consumption based on damage.
    /// Higher values make damage more punishing in terms of power consumption.
    /// </summary>
    [DataField]
    [GuidebookData]
    public float DamageExp = 1.1f;

    /// <summary>
    /// Modifies the total power consumed after damage exponentials are applied.
    /// </summary>
    [DataField]
    [GuidebookData]
    public float PowerModifier = 0.5f;

    /// <summary>
    /// Linear term in additional load: Damage × this coefficient plus the exponential term, clamped to <see cref="MaxDraw"/>.
    /// </summary>
    [DataField]
    public float DamageLinearLoadCoefficient = 0f;

    /// <summary>
    /// When true, healing per tick is scaled by <c>PowerReceived / Load</c> on the APC receiver (0–1).
    /// </summary>
    [DataField]
    public bool HealScalesWithPowerReceived = false;

    /// <summary>
    /// Added to <see cref="Damage"/> each second (before overload checks), e.g. lattice bleed.
    /// </summary>
    [DataField]
    public float PassiveShieldDamagePerSecond = 0f;

    /// <summary>
    /// Multiplies overload duration scaling from damage above <see cref="DamageLimit"/>. 0 keeps legacy flat punishment.
    /// </summary>
    [DataField]
    public float OverloadPunishmentScale = 0f;

    /// <summary>
    /// Upper cap on scaled overload seconds; 0 means no cap.
    /// </summary>
    [DataField]
    public float OverloadPunishmentMax = 0f;

    /// <summary>
    /// Fraction of raw hit magnitude applied to <see cref="Damage"/> (0–1). Below 1 lets part of the projectile continue through the field.
    /// </summary>
    [DataField]
    public float ShieldProjectileAbsorptionFraction = 1f;

    /// <summary>
    /// Reduces effective absorption by this fraction times normalized field stress (Damage / DamageLimit).
    /// </summary>
    [DataField]
    public float ShieldPassthroughFromStress = 0f;

    /// <summary>
    /// Max shield-pool damage added per hit; 0 = unlimited.
    /// </summary>
    [DataField]
    public float ShieldHitDamageCap = 0f;

    /// <summary>
    /// When absorption leaves the projectile alive, ignore this many shield contact evaluations against <see cref="ShieldBubblePassImmunityComponent.ShieldBubble"/>.
    /// </summary>
    [DataField]
    public int ShieldPassthroughImmunityTicks = 3;

    /// <summary>
    /// At full field stress, collision uses this multiplier instead of <see cref="CollisionResistanceMultiplier"/>. Negative disables stress blending.
    /// </summary>
    [DataField]
    public float CollisionResistanceAtFullStress = -1f;

    /// <summary>
    /// Exponent on normalized damage/limit for collision stress; 0 or negative disables blending.
    /// </summary>
    [DataField]
    public float CollisionStressExponent = 1f;

    /// <summary>
    /// Seconds between emitter processing ticks. Healing per tick is <see cref="HealPerSecond"/> times this value
    /// (multiplied by <see cref="UnpoweredBonus"/> while <see cref="Recharging"/>).
    /// </summary>
    [DataField]
    public float EmitterUpdateInterval = 1.5f;

    /// <summary>
    /// Per damage type prototype id, multiplier applied to projectile damage before it is added to <see cref="Damage"/>.
    /// Types not listed use 1.0.
    /// </summary>
    [DataField]
    public Dictionary<string, float> ProjectileDamageTypeMultipliers = new();

    /// <summary>
    /// Multiplier on EMP contribution (after clamp) from deflected payloads with <c>EmpOnTrigger</c>.
    /// </summary>
    [DataField]
    [GuidebookData]
    public float ShieldEmpDamageMultiplier = 1f;

    /// <summary>
    /// Multiplier on explosion-intensity contribution from deflected explosives.
    /// </summary>
    [DataField]
    [GuidebookData]
    public float ShieldExplosionDamageMultiplier = 1f;

    /// <summary>
    /// Rate at which the emitter heals/reduces its damage per second when powered.
    /// </summary>
    [DataField]
    [GuidebookData]
    public float HealPerSecond = 250f;

    /// <summary>
    /// Multiplier applied to healing rate when the emitter is in recharge mode.
    /// </summary>
    [DataField]
    [GuidebookData]
    public float UnpoweredBonus = 6f;

    /// <summary>
    /// Maximum power consumption limit for additional emitter load in watts.
    /// </summary>
    [DataField]
    [GuidebookData]
    public float MaxDraw = 150000f;

    /// <summary>
    /// Base power consumption of the emitter when undamaged, in watts.
    /// </summary>
    [DataField]
    [GuidebookData]
    public float BaseDraw = 50000f;

    [DataField, AutoNetworkedField] // Forge-Change
    public bool Recharging = false;

    /// <summary>
    /// Damage threshold that triggers overload protection.
    /// </summary>
    [DataField, AutoNetworkedField] // Forge-Change
    [GuidebookData]
    public float DamageLimit = 3500;

    /// <summary>
    /// Duration in seconds that the emitter remains in overload state after exceeding DamageLimit.
    /// </summary>
    [DataField]
    [GuidebookData]
    public float DamageOverloadTimePunishment = 30;

    /// <summary>
    /// The color of the shield generated by this emitter.
    /// </summary>
    [DataField]
    public Color ShieldColor = Color.White;

    [ViewVariables, AutoNetworkedField] // Forge-Change
    public float OverloadAccumulator = 0f;

    // Forge-Change-Start: replicated state used by client HUD instead of server-side BUI pushes.
    /// <summary>
    /// Wall-clock end of the current recharge/overload window. Clients render the countdown locally as <c>RechargeEndTime - CurTime</c>.
    /// Null when the emitter is not in recharge.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public TimeSpan? RechargeEndTime;

    /// <summary>
    /// True while a shield bubble is active for this emitter.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public bool Online;
    // Forge-Change-End

    /// <summary>
    /// On power up, players for all on vessel, pitched down.
    /// </summary>
    [DataField]
    public SoundSpecifier PowerUpSound = new SoundPathSpecifier("/Audio/Effects/teleport_arrival.ogg");

    [DataField]
    public SoundSpecifier PowerDownSound = new SoundPathSpecifier("/Audio/Effects/teleport_departure.ogg");

    /// <summary>
    /// While shield is active, multiplies impact energy to both grids from grid collisions by this much.
    /// </summary>
    [DataField]
    [GuidebookData]
    public float CollisionResistanceMultiplier = 1.0f;

    /// <summary>
    /// Shield tier (1-4) for bluespace boarding bypass. When zero, inferred from damage limit.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int ShieldTier;

    /// <summary>
    /// When true, active shield blocks all boarding teleports through this grid and prevents using boarding teleports from this grid.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool BluespaceTeleportImmune;
}
