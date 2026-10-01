using Content.Shared._Onyx.Wounds;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Body;

/// <summary>Organ health data Onyx keeps on its own OrganComponent; Wolfgate stays on Shitmed's, so it lives here.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedOrganComponent : Component
{
    /// <summary>Current organ health; at or below zero the organ stops working and is destroyed.</summary>
    [DataField, AutoNetworkedField] public FixedPoint2 Health = FixedPoint2.New(15);

    /// <summary>Health the organ starts at and is clamped to.</summary>
    [DataField, AutoNetworkedField] public FixedPoint2 MaxHealth = FixedPoint2.New(15);

    /// <summary>Wound left on the containing part when this organ is destroyed. Null leaves none.</summary>
    [DataField] public ProtoId<WoundPrototype>? DestructionWound;

    /// <summary>Severity of the wound left behind by destruction.</summary>
    [DataField] public FixedPoint2 DestructionWoundSeverity;

    /// <summary>M3 (plan §8): health fraction under which the organ is impaired and its band effects apply.</summary>
    [DataField] public float ImpairedBelow = 0.5f;

    /// <summary>
    /// M3: blood regeneration multiplier while this organ is impaired (the heart: 0.5). 1 means the organ has
    /// no say in regeneration.
    /// </summary>
    [DataField] public float ImpairedRegenFactor = 1f;

    /// <summary>
    /// M4 (plan §8.5): core cooling multiplier while this organ is impaired (a machine's coolant pump: 0.5), so an
    /// impaired pump lets the core overheat sooner. 1 means the organ does no cooling.
    /// </summary>
    [DataField] public float ImpairedCoolingFactor = 1f;

    /// <summary>
    /// Most health this organ loses to one hit through the reach lines, in place of wolfmed.organ_hit_cap when set.
    /// A chassis torso holds only the core and the pump, so every heavy round hit both at the global cap and an IPC
    /// shut down on the fifth rifle round and lost its core on the eighth (playtest 3).
    /// </summary>
    [DataField] public float? HitCap;

    /// <summary>
    /// M5 (plan §3.8, OD13): toxin clearance multiplier while this organ is impaired (the liver: 0.5). Read only
    /// for a liver; a failed liver clears nothing.
    /// </summary>
    [DataField] public float ImpairedClearanceFactor = 1f;

    /// <summary>OK, impaired or failed, from health against <see cref="ImpairedBelow"/>.</summary>
    public WolfmedOrganBand Band =>
        Health <= FixedPoint2.Zero ? WolfmedOrganBand.Failed
        : MaxHealth > FixedPoint2.Zero && Health.Float() / MaxHealth.Float() < ImpairedBelow ? WolfmedOrganBand.Impaired
        : WolfmedOrganBand.Ok;

    /// <summary>Health as a fraction of the maximum, 0 to 1.</summary>
    public float Fraction => MaxHealth > FixedPoint2.Zero ? Math.Clamp(Health.Float() / MaxHealth.Float(), 0f, 1f) : 1f;
}

/// <summary>M3 (plan §8): how an organ is doing, in the analyzer's words.</summary>
[Serializable, NetSerializable]
public enum WolfmedOrganBand : byte
{
    Ok = 0,
    Impaired = 1,
    Failed = 2,
}
