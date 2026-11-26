using Content.Shared._Onyx.Wounds;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Surgery;

/// <summary>Gates a wound surgery on a matching wound being present on the selected part.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryWoundConditionComponent : Component
{
    [DataField] public ProtoId<WoundPrototype>? WoundPrototype;

    /// <summary>Playtest 3 IPC 2: any one of these present is a match, alongside <see cref="WoundPrototype"/>.</summary>
    [DataField] public List<ProtoId<WoundPrototype>>? WoundPrototypes;

    /// <summary>Playtest 3 IPC 2: only on a machine part (an IPC chassis, a cybernetic limb).</summary>
    [DataField] public bool Mechanical;
    [DataField] public WoundVisibility? Visibility;
    [DataField] public WoundState? State;
    [DataField] public bool Bleeding;
    [DataField] public bool InternalBleeding;
    /// <summary>Inverts the whole test, so the surgery lists only while the wound is absent.</summary>
    [DataField] public bool Inverse;
}

/// <summary>Suture step: reduces the bleeding rate of the worst bleeding wound on the part.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryClampBleedingEffectComponent : Component
{
    [DataField(required: true)] public FixedPoint2 Amount;
    [DataField] public ProtoId<WoundPrototype>? WoundPrototype;
}

/// <summary>Treats a matching wound outright; the default Amount removes it in one step.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryTreatWoundEffectComponent : Component
{
    [DataField] public ProtoId<WoundPrototype>? WoundPrototype;
    [DataField] public bool InternalBleeding;
    [DataField] public FixedPoint2 Amount = FixedPoint2.MaxValue;
}

/// <summary>Gates a surgery on the selected part carrying a fracture in a grade/treatment window.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryFractureConditionComponent : Component
{
    [DataField] public FractureGrade MinGrade = FractureGrade.Hairline;
    [DataField] public FractureGrade? Grade;
    [DataField] public FractureTreatment? Treatment;
}

/// <summary>Advances the part's fracture to a treatment stage (Reduced by a bone setter, Mended by bone gel).</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryMendFractureEffectComponent : Component
{
    [DataField] public FractureTreatment Treatment = FractureTreatment.Mended;
}

/// <summary>Gates an organ surgery on a named organ slot on the selected part being damaged but alive.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryOrganDamagedConditionComponent : Component
{
    [DataField(required: true)] public string Slot = string.Empty;
    [DataField] public bool Inverse;

    /// <summary>BRAIN: match a destroyed organ (health 0) instead of a damaged but living one.</summary>
    [DataField] public bool Destroyed;

    /// <summary>
    /// Match any organ below full health, destroyed or not. A brain at three per cent is alive, so the
    /// destroyed-only test left it with no listed procedure at all and nothing anyone could do about it.
    /// </summary>
    [DataField] public bool AnyDamage;

    /// <summary>
    /// M6: the surgery stays valid while the part carries any of these, whatever the organ's health. Core repair
    /// lists its open housing here, so the weld that closes it is still reachable once the core is whole.
    /// </summary>
    [DataField] public ComponentRegistry? ValidWhile;
}

/// <summary>
/// BRAIN: puts a destroyed brain back together. The only thing that raises a brain organ past zero, and
/// the only way a brain-dead body becomes defibrillatable again. Leaves the trauma behind.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryBrainRepairEffectComponent : Component
{
    /// <summary>M2 (OD10): the organ slot the step repairs. "posbrain" makes it core repair on a chassis.</summary>
    [DataField]
    public string Slot = "brain";
}

/// <summary>Restores health to a named organ slot on the selected part.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryOrganHealEffectComponent : Component
{
    [DataField(required: true)] public string Slot = string.Empty;
    [DataField] public FixedPoint2 Amount = 1;
}

/// <summary>Charges pain to the operated part when the step completes.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryPainEffectComponent : Component
{
    [DataField] public FixedPoint2 Amount = 5;
    /// <summary>Onyx's anaesthesia scale. Ships inert - Wolfgate has no consumer (P4-D22).</summary>
    [DataField] public FixedPoint2 SleepModifier = 1;
}

/// <summary>Opens a surgical incision as a real wound on the part, so it can bleed, be clamped and scar.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryIncisionWoundEffectComponent : Component
{
    [DataField] public FixedPoint2 Severity = 10;
    [DataField] public ProtoId<WoundPrototype> Wound = "SurgicalIncisionWound";
}

/// <summary>Clamps or closes the incision wounds this operation opened.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryIncisionTreatmentEffectComponent : Component
{
    [DataField] public ProtoId<WoundPrototype> Wound = "SurgicalIncisionWound";
    /// <summary>Clamp (the bleeders step) or Close (the close-incision step, which also rolls for a scar).</summary>
    [DataField] public WolfmedIncisionTreatment Treatment = WolfmedIncisionTreatment.Clamp;
}

/// <summary>
/// Closes an evisceration: the tear goes, and its
/// <see cref="Content.Shared._WF.Wolfmed.Wounds.WolfmedClearedWoundBehavior"/> names the sutured wound
/// left in its place, so what the patient is left with is wound data rather than an id in C#.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryCloseEviscerationEffectComponent : Component
{
    [DataField] public ProtoId<WoundPrototype> WoundPrototype = "WFWolfmedEviscerationWound";
}

/// <summary>Which half of the incision chain an incision treatment step runs.</summary>
public enum WolfmedIncisionTreatment : byte
{
    Clamp,
    Close,
}

/// <summary>Gates a surgery on the selected part still having something stuck in it.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryEmbeddedConditionComponent : Component
{
    /// <summary>Inverts the test, so the surgery lists only once the part is clear.</summary>
    [DataField] public bool Inverse;
}

/// <summary>
/// Takes everything out of the part through the same code a hemostat in a hand uses, so a round the pod
/// digs out leaves exactly the wound, the contamination and the item a medic would have left.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryExtractEmbeddedEffectComponent : Component
{
    /// <summary>A surgical tool works cleanly: no extra cut, no contamination.</summary>
    [DataField] public bool Clean = true;

    /// <summary>Objects one step may take out, so a broken count cannot spin the loop.</summary>
    [DataField] public int MaxPerStep = 24;
}

/// <summary>Puts a dislocated joint back, through the same code the relocate verb's do-after calls.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryRelocateJointEffectComponent : Component
{
    [DataField] public ProtoId<WoundPrototype> Wound = "WFWolfmedDislocationWound";
}

/// <summary>
/// Playtest 3 IPC 2: one pass of a hand welder on a chassis part, by the step's own welder. Repeats until the part
/// carries none of <see cref="Wounds"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryWeldChassisEffectComponent : Component
{
    [DataField(required: true)] public List<ProtoId<WoundPrototype>> Wounds = new();
}

/// <summary>
/// Playtest 3 IPC 2: a run of cable coil uses on a chassis part, by the step's own coil. Repeats until the part
/// carries none of <see cref="Wounds"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedSurgeryRewireChassisEffectComponent : Component
{
    [DataField(required: true)] public List<ProtoId<WoundPrototype>> Wounds = new();

    /// <summary>Coil uses in one pass: a hand coil takes 0.6 s a use, the step a few seconds.</summary>
    [DataField] public int Uses = 5;
}
