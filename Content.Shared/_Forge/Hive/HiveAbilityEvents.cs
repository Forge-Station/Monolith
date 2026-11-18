using Content.Shared.Actions;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Hive;




public sealed partial class HiveToggleHandItemEvent : InstantActionEvent
{

    [DataField(required: true)]
    public EntProtoId Item;

    [DataField]
    public LocId? PopupOn;

    [DataField]
    public LocId? PopupOff;

    [DataField]
    public SoundSpecifier? Sound = new SoundCollectionSpecifier("gib");
}


public sealed partial class HiveToggleArmorEvent : InstantActionEvent
{

    [DataField(required: true)]
    public Dictionary<string, EntProtoId> Items = new();

    [DataField]
    public LocId? PopupOn;

    [DataField]
    public LocId? PopupOff;

    [DataField]
    public SoundSpecifier? Sound = new SoundCollectionSpecifier("gib");
}


public sealed partial class HiveAdrenalineEvent : InstantActionEvent
{

    [DataField]
    public TimeSpan StunImmunity = TimeSpan.FromSeconds(10);


    [DataField]
    public TimeSpan PainResistance = TimeSpan.FromSeconds(20);


    [DataField]
    public bool ResetStamina = true;


    [DataField]
    public Solution? Reagents;

    [DataField]
    public LocId Popup = "hive-adrenaline-popup";

    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Items/hypospray.ogg");
}


public sealed partial class HiveFleshmendEvent : InstantActionEvent
{

    [DataField(required: true)]
    public DamageSpecifier HealPerSecond = new();

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(10);


    [DataField]
    public float RecastMultiplier = 0.5f;


    [DataField]
    public float BloodPerSecond = 2f;

    [DataField]
    public LocId Popup = "hive-fleshmend-popup";

    [DataField]
    public LocId PopupRecast = "hive-fleshmend-popup-recast";
}


public sealed partial class HiveDissonantShriekEvent : InstantActionEvent
{
    [DataField]
    public float Range = 5f;

    [DataField]
    public float EnergyConsumption = 50000f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(20);

    [DataField]
    public LocId Popup = "hive-dissonant-shriek-popup";

    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Effects/Lightning/lightningbolt.ogg");
}


public sealed partial class HiveResonantShriekEvent : InstantActionEvent
{
    [DataField]
    public float Range = 5f;


    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(10);


    [DataField]
    public float Disorientation = 40f;

    [DataField]
    public float StaminaDamage = 40f;

    [DataField]
    public bool BreakLights = true;

    [DataField]
    public LocId Popup = "hive-resonant-shriek-popup";

    [DataField]
    public LocId PopupVictim = "hive-resonant-shriek-victim";

    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Effects/demon_attack1.ogg");
}


public sealed partial class HiveAugmentedEyesightEvent : InstantActionEvent
{

    [DataField]
    public float ThermalFlashMultiplier = 2f;
}


public sealed partial class HiveStrainedMusclesEvent : InstantActionEvent
{
    [DataField]
    public float SpeedMultiplier = 1.5f;


    [DataField]
    public float StaminaPerSecond = 6f;


    [DataField]
    public TimeSpan KnockdownOnEnd = TimeSpan.FromSeconds(2);
}


public sealed partial class HiveChameleonSkinEvent : InstantActionEvent
{

    [DataField]
    public float PassiveVisibilityRate = -0.15f;


    [DataField]
    public float MovementVisibilityRate = 0.2f;
}


public sealed partial class HiveDnaChannelEvent : InstantActionEvent;


public sealed partial class HiveHivemindLinkEvent : InstantActionEvent
{
    [DataField]
    public TimeSpan Duration = TimeSpan.FromMinutes(120);
}
