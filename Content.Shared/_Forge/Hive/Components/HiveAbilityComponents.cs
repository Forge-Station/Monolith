using Content.Shared._Starlight.CollectiveMind;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Forge.Hive.Components;


[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HiveBodyPartComponent : Component
{

    [DataField, AutoNetworkedField]
    public EntProtoId? SourcePrototype;

    [DataField, AutoNetworkedField]
    public EntityUid? Owner;
}


/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class HiveTentacleComponent : Component
{

    [DataField]
    public bool YankItems = true;


    [DataField]
    public float PullSpeed = 10f;

    [DataField]
    public TimeSpan Knockdown = TimeSpan.FromSeconds(1);
}


[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class HiveAdrenalineComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan StunImmuneUntil;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan PainResistUntil;

    /// <summary>
    /// Мы ли добавили IgnoreSlowOnDamage (чтобы не снять чужой).
    /// </summary>
    [DataField]
    public bool AddedPainResist;
}


[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class HiveFleshmendComponent : Component
{
    [DataField]
    public Content.Shared.Damage.DamageSpecifier HealPerSecond = new();

    [DataField]
    public float BloodPerSecond;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan EndTime;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextTick;
}


[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HiveAugmentedEyesightComponent : Component
{
    [DataField, AutoNetworkedField]
    public HiveEyesightMode Mode = HiveEyesightMode.Off;


    [DataField]
    public bool AddedNightVision;

    [DataField]
    public bool AddedThermalVision;
}

public enum HiveEyesightMode : byte
{
    Off,
    Night,
    Thermal,
}


[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class HiveStrainedMusclesComponent : Component
{
    [DataField, AutoNetworkedField]
    public float SpeedMultiplier = 1.5f;

    [DataField]
    public float StaminaPerSecond = 6f;

    [DataField]
    public TimeSpan KnockdownOnEnd = TimeSpan.FromSeconds(2);

    [DataField]
    public EntityUid? Action;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextTick;
}


[RegisterComponent]
public sealed partial class HiveChameleonSkinComponent : Component
{

    [DataField]
    public bool AddedStealth;
}


[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class HiveLinkedComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan EndTime;

    [DataField]
    public EntityUid? LinkedBy;

    [DataField]
    public ProtoId<CollectiveMindPrototype> Channel = "Hive";
}
