using Content.Shared._Onyx.Wounds;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.ArmorSpecialization;

/// <summary>
/// Worn armour that changes which wounds Wolfmed's rules make on the parts it covers: some it refuses outright,
/// others need a bigger hit. Spall liners, sealed layers, insulation, bone plating and the wards are all this.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFWoundWardComponent : Component
{
    /// <summary>Wounds no hit makes on a covered part.</summary>
    [DataField]
    public HashSet<ProtoId<WoundPrototype>> Blocks = new();

    /// <summary>Wounds a hit makes only past this multiple of its rule's usual minimum damage.</summary>
    [DataField]
    public Dictionary<ProtoId<WoundPrototype>, float> Hardens = new();

    /// <summary>Localization id for the examine line naming what the armour does.</summary>
    [DataField]
    public LocId? Examine;
}

/// <summary>Worn armour that keeps the limbs it covers on: a finishing hit or blast doesn't take them off.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFLimbLockComponent : Component
{
    /// <summary>A single damage type this large still takes the limb. Zero means nothing does.</summary>
    [DataField]
    public FixedPoint2 Breakthrough = FixedPoint2.Zero;
}

/// <summary>Worn armour that dulls pain, the same way a painkiller in the blood does.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFPainDampenerComponent : Component
{
    /// <summary>Pain units taken off before pain decides whether the wearer goes down. An analgesic gives 22.</summary>
    [DataField]
    public float Relief = 15f;
}

/// <summary>
/// A passive coat that soaks a share of one damage group from a small pool, then needs a lull to recover.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WFAblativeCoatComponent : Component
{
    [DataField]
    public ProtoId<DamageGroupPrototype> DamageGroup = "Burn";

    /// <summary>Share of that damage the coat soaks while it has charge.</summary>
    [DataField]
    public float Reduction = 0.5f;

    [DataField]
    public float MaxCharge = 40f;

    /// <summary>Charge regained per second once the coat has gone this long without a hit.</summary>
    [DataField]
    public float RegenRate = 8f;

    [DataField]
    public TimeSpan RecoveryDelay = TimeSpan.FromSeconds(6);

    [DataField, AutoNetworkedField]
    public float Charge = 40f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan LastHit;
}

/// <summary>
/// Clamps an untreated arterial bleed on a covered limb by itself. Each clamp spends a charge, and charges come
/// back over time.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WFAutoTourniquetComponent : Component
{
    [DataField]
    public int MaxCharges = 2;

    [DataField, AutoNetworkedField]
    public int Charges = 2;

    [DataField]
    public TimeSpan RechargeTime = TimeSpan.FromMinutes(2);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextCharge;
}
