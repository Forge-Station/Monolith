using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Body.Part;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared._Shitmed.Targeting; // WOLFGATE(Wolfmed): D10 skips Onyx's Targeting stack; TargetBodyPart comes from Shitmed.
using Content.Shared._WF.Wolfmed.Damage; // WOLFGATE(Wolfmed): V3: PartDamageVisualsComponent.Degradation.
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.Wounds;

[RegisterComponent, NetworkedComponent]
public sealed partial class WoundHostComponent : Component
{
    [DataField]
    public Dictionary<BodyPartType, float> TargetWeights = new()
    {
        // WOLFGATE(Wolfmed): D9 folds Onyx's Chest 2.5 + Groin 1.5 into Wolfgate's single Torso, keeping the 4/13 share.
        [BodyPartType.Torso] = 4f,
        [BodyPartType.Head] = 1f,
        [BodyPartType.Arm] = 2f,
        [BodyPartType.Hand] = 1f,
        [BodyPartType.Leg] = 2f,
        [BodyPartType.Foot] = 1f,
        [BodyPartType.Tail] = 1f,
        [BodyPartType.Other] = 1f,
    };

    /// <summary>
    /// Damage types routed to body parts. Other types remain systemic on the wound host.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<DamageTypePrototype>> LocalizedDamageTypes =
    [
        "Blunt",
        "Slash",
        "Piercing",
        "Heat",
        "Cold",
        "Shock",
        "Caustic",
    ];

    [DataField]
    public TargetBodyPart SystemicPainTarget = TargetBodyPart.Torso; // WOLFGATE(Wolfmed): D9, Wolfgate has no TargetBodyPart.Chest.

    [DataField]
    public Dictionary<BodyPartType, FixedPoint2> DismembermentSeverities = new()
    {
        [BodyPartType.Head] = 200,
        // WOLFGATE(Wolfmed): D9 deletes the Groin row; Wolfgate's BodyPartType has no Groin.
        [BodyPartType.Arm] = 120,
        [BodyPartType.Leg] = 120,
        [BodyPartType.Hand] = 80,
        [BodyPartType.Foot] = 80,
    };

    [DataField]
    public FixedPoint2 DefaultDismembermentSeverity = 100;

    [DataField]
    public ProtoId<WoundPrototype> DismembermentWound = "DismembermentWound";

    [DataField]
    public ProtoId<WoundPrototype> AmputationConsequenceWound = "AmputationConsequenceWound";

    [DataField]
    public float SeverableResetRatio = 0.8f;

    [DataField]
    public Dictionary<ProtoId<DamageTypePrototype>, FixedPoint2> DefaultDismembermentFinishingDamage = new()
    {
        ["Slash"] = 15,
        ["Piercing"] = 40,
        ["Blunt"] = 50,
    };

    [DataField]
    public HashSet<BodyPartType> MobilityParts = [BodyPartType.Leg, BodyPartType.Foot];

    [DataField]
    public HashSet<BodyPartType> ManipulationParts = [BodyPartType.Arm, BodyPartType.Hand];

    [DataField]
    public Dictionary<BodyPartType, float> PartEffectScales = new()
    {
        [BodyPartType.Leg] = 0.5f,
        [BodyPartType.Foot] = 0.5f,
        [BodyPartType.Hand] = 0.75f,
    };
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class PartDamageVisualsComponent : Component
{
    [AutoNetworkedField]
    public Dictionary<HumanoidVisualLayers, DamageSpecifier> Damage = new();

    // WOLFGATE(Wolfmed): V3: degradation stage per layer, written by WolfmedDegradationVisualsSystem. It rides this
    // component rather than a second one so it reuses the AfterAutoHandleState hook the client already has.
    // Damage cannot stand in for it: it says nothing about wound severity, material or dead tissue.
    [AutoNetworkedField]
    public Dictionary<HumanoidVisualLayers, WolfmedPartDegradation> Degradation = new();

    // WOLFGATE(Wolfmed): GORE/G3: the dressing or splint each limb is wearing, written by
    // WolfmedTreatmentVisualsSystem. Rides here for the same reason the degradation stage does.
    [AutoNetworkedField]
    public Dictionary<HumanoidVisualLayers, WolfmedPartTreatment> Treatments = new();

    // WOLFGATE(Wolfmed) START: VISUALS: open wounds, rot and the blood colour, written by WolfmedWoundOverlaySystem
    // They ride here for the same reason the degradation stage does: one client hook draws every Wolfmed layer.
    [AutoNetworkedField]
    public Dictionary<HumanoidVisualLayers, WolfmedWoundOverlay> Wounds = new();

    [AutoNetworkedField]
    public HashSet<HumanoidVisualLayers> Rot = new();

    [AutoNetworkedField]
    public Color WoundColor = Color.White;

    /// <summary>Playtest 4: the arteries on the sprite, a cut artery on a part or the stump where a part was.</summary>
    [AutoNetworkedField]
    public Dictionary<WolfmedArterySite, WolfmedArteryOverlay> Arteries = new();

    /// <summary>
    /// Playtest 4: the stumps where limbs were, by the missing part's site: Old for an open stump that has stopped,
    /// Drip or Stream while it bleeds. The stump art itself shows for any of them; the glyph only while it bleeds.
    /// </summary>
    [AutoNetworkedField]
    public Dictionary<WolfmedArterySite, WolfmedWoundOverlay> Stumps = new();

    /// <summary>
    /// When the body last threw a blood spurt with a spurting artery on the sprite. The client plays the spray on every
    /// spurting artery when this moves and is recent; a stale time (a body seen mid-bleed, one back from out of view)
    /// plays nothing.
    /// </summary>
    [AutoNetworkedField]
    public TimeSpan ArterySprayAt;

    /// <summary>Client only: the last ArterySprayAt it played.</summary>
    public TimeSpan LastArterySprayAt;
    // WOLFGATE END
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class PainComponent : Component
{
    [AutoNetworkedField]
    public FixedPoint2 Value;

    [AutoNetworkedField]
    public FixedPoint2 Suppression;

    /// <summary>
    /// Pain floor produced by the part's wounds themselves (independent of damage).
    /// Healing a wound removes ~half immediately, the rest decays via normal recovery.
    /// </summary>
    [AutoNetworkedField]
    public FixedPoint2 WoundPain;

    [DataField]
    public Dictionary<ProtoId<DamageTypePrototype>, float> DamageMultipliers = new()
    {
        ["Blunt"] = 0.87f,
        ["Slash"] = 0.67f,
        ["Piercing"] = 0.67f,
        ["Heat"] = 0.8f,
        ["Cold"] = 0.75f,
        ["Shock"] = 0.7f,
        ["Cellular"] = 0.32f,
        ["Caustic"] = 0.12f,
        ["Radiation"] = 0.12f,
        ["Poison"] = 0.7f,
    };

    [DataField]
    public FixedPoint2 RecoveryPerSecond = FixedPoint2.New(1f / 9f);

    [DataField]
    public FixedPoint2 SoftPainCap = 135;

    [ViewVariables]
    public Dictionary<string, PainSuppressionModifier> SuppressionModifiers = new();
}

public sealed record PainSuppressionModifier(FixedPoint2 Amount, FixedPoint2 DecayPerSecond, float RecoveryMultiplier);

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PainShockTargetComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Armed = true;

    [AutoNetworkedField]
    public TimeSpan? AdrenalineEnds;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WoundableComponent : Component
{
    public const string ContainerId = "wounds";

    [DataField, AutoNetworkedField]
    public ProtoId<BodyPartProfilePrototype> Profile = "OrganicBodyPartProfile";

    [ViewVariables]
    public Container WoundsContainer = default!;

    [ViewVariables]
    public FixedPoint2 AmputationOverflow;

    [AutoNetworkedField]
    public bool Severable;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BodyPartFunctionalityComponent : Component
{
    [AutoNetworkedField]
    public BodyPartFunctionalityState State = BodyPartFunctionalityState.Functional;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WoundComponent : Component
{
    // WOLFGATE(Wolfmed): severity at the last runtime-component sync. A wound only rolls for bleeding when it has grown
    // since then (a new injury), so healing, closing or re-dressing it never restarts a bleed that was stopped.
    [ViewVariables]
    public FixedPoint2 LastSyncSeverity = FixedPoint2.Zero;

    [DataField, AutoNetworkedField]
    public EntityUid HoldingPart;

    [DataField(required: true), AutoNetworkedField]
    public ProtoId<WoundPrototype> Prototype;

    [DataField, AutoNetworkedField]
    public FixedPoint2 Severity;

    [DataField, AutoNetworkedField]
    public FixedPoint2 PeakSeverity;

    [DataField, AutoNetworkedField]
    public WoundState State = WoundState.Open;

    [ViewVariables]
    public bool ScarCreatedForCurrentClosure;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WoundFunctionalityComponent : Component
{
    [DataField, AutoNetworkedField]
    public BodyPartFunctionalityState State;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WoundBleedingComponent : Component
{
    [DataField, AutoNetworkedField]
    public float BaseRate;

    [DataField, AutoNetworkedField]
    public float CurrentRate;

    [DataField, AutoNetworkedField]
    public FixedPoint2 BleedingSeverity;

    [DataField, AutoNetworkedField]
    public BleedingTreatment Treatment;

    [DataField, AutoNetworkedField]
    public float NaturalClotting;

    [ViewVariables]
    public TimeSpan? AutomaticClottingStartedAt;

    [ViewVariables]
    public TimeSpan? AutomaticClottingAt;

}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WoundInternalBleedingComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Rate;

    [DataField, AutoNetworkedField]
    public FixedPoint2 Severity;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WoundFractureComponent : Component
{
    /// <summary>
    /// Cached severity grade. Bone damage is the wound's <see cref="WoundComponent.Severity"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FractureGrade Grade;

    [DataField, AutoNetworkedField]
    public FractureTreatment Treatment;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class WoundScarComponent : Component;

[Serializable, NetSerializable]
public enum WoundState : byte
{
    Open,
    Stabilized,
    Closed,
    Healed,
    Scarred,
}

[Serializable, NetSerializable]
public enum BleedingTreatment : byte
{
    None,
    Bandaged,
    Clamped,
    Sutured,
    Cauterized,
}

[Serializable, NetSerializable]
public enum FractureGrade : byte
{
    None,
    Hairline,
    Simple,
    Displaced,
    Comminuted,
}

[Serializable, NetSerializable]
public enum FractureTreatment : byte
{
    None,
    Reduced,
    Mended,
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SystemicDamageComponent : Component
{
    [DataField, AutoNetworkedField]
    public DamageSpecifier Damage = new();
}
