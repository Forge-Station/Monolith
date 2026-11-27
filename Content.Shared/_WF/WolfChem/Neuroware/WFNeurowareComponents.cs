using Content.Shared._WF.WolfChem.Narcotics;
using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.WolfChem.Neuroware;

/// <summary>Appearance keys for a neuroware stick.</summary>
[Serializable, NetSerializable]
public enum WFNeurowareStickVisuals : byte
{
    /// <summary>A blank that has taken a copy.</summary>
    Copied,
}

/// <summary>
/// A data stick holding one drug profile's neuroware twin. Loading it into a host runs the program; using it on a
/// blank stick copies it, a little worse each generation, and a blank used on a drug reads it in.
/// </summary>
[RegisterComponent]
public sealed partial class WFNeurowareStickComponent : Component
{
    /// <summary>The program on the stick, or null for a blank.</summary>
    [DataField]
    public ProtoId<WFDrugProfilePrototype>? Profile;

    /// <summary>How intact the build is, 0-1. Multiplies the dose; the missing share causes glitches.</summary>
    [DataField]
    public float Integrity = 1f;

    /// <summary>Loads left before the stick is spent.</summary>
    [DataField]
    public int Loads = 3;

    /// <summary>Level a load delivers in total, or null for the twin's full load.</summary>
    [DataField]
    public float? Amount;

    /// <summary>Level delivered a second while loading.</summary>
    [DataField]
    public float Rate = 0.5f;

    [DataField]
    public TimeSpan SelfDelay = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan OtherDelay = TimeSpan.FromSeconds(4);

    /// <summary>How long a blank takes to read a drug in.</summary>
    [DataField]
    public TimeSpan EncodeDelay = TimeSpan.FromSeconds(3);
}

/// <summary>Neuroware running on a host body.</summary>
[RegisterComponent]
public sealed partial class WFNeurowareComponent : Component
{
    [DataField]
    public List<WFNeurowareProgram> Programs = new();

    /// <summary>A false fault a glitch put on the HUD, and when it clears.</summary>
    [DataField]
    public LocId? FalseFault;

    [DataField]
    public TimeSpan FalseFaultUntil;
}

/// <summary>One program delivering its profile's level.</summary>
[DataDefinition]
public sealed partial class WFNeurowareProgram
{
    [DataField(required: true)]
    public ProtoId<WFDrugProfilePrototype> Profile;

    [DataField]
    public float Remaining;

    [DataField]
    public float Rate;

    [DataField]
    public float Integrity = 1f;
}

[Serializable, NetSerializable]
public sealed partial class WFNeurowareLoadDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class WFNeurowareEncodeDoAfterEvent : SimpleDoAfterEvent;
