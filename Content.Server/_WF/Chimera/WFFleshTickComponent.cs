using Content.Shared._Shitmed.Targeting;
using Content.Shared.FixedPoint;

namespace Content.Server._WF.Chimera;

/// <summary>A flesh tick that leaps onto a host, latches to a body part and drains it.</summary>
[RegisterComponent]
public sealed partial class WFFleshTickComponent : Component
{
    [DataField]
    public float LeapRange = 3f;

    [DataField]
    public float LeapSpeed = 8f;

    [DataField]
    public float LeapCooldown = 4f;

    [DataField]
    public float PulseInterval = 2f;

    [DataField]
    public FixedPoint2 InjectionAmount = FixedPoint2.New(1);

    [DataField]
    public FixedPoint2 BloodDrain = FixedPoint2.New(1);

    [DataField]
    public FixedPoint2 BruteDamage = FixedPoint2.New(1);

    [DataField]
    public int MaxTicksPerHost = 3;

    [DataField]
    public float RetirementTime = 120f;

    [DataField]
    public float RetirementPlayerRange = 96f;

    [ViewVariables]
    public EntityUid? Host;

    [ViewVariables]
    public TargetBodyPart? TargetPart;

    [ViewVariables]
    public TimeSpan SpawnedAt;

    [ViewVariables]
    public TimeSpan NextLeap;

    [ViewVariables]
    public TimeSpan ResumeBitingAt;

    [ViewVariables]
    public TimeSpan NextPulse;

    [ViewVariables]
    public TimeSpan NextHuntSound;

    [ViewVariables]
    public bool Leaping;

    /// <summary>Until when the tick's own leap can still bring it down; that landing does no fall damage.</summary>
    [ViewVariables]
    public TimeSpan LeapLandsBy;

    [ViewVariables]
    public bool ProtectedFromRetirement;

    [ViewVariables]
    public bool DeathSoundPlayed;
}
