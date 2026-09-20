using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Leviathans.Components;

/// <summary>
/// Cosmic jellyfish leviathan. Does not chew hulls — it latches shuttles,
/// EMP-blinds electronics, freezes crews in a stasis veil, and rains shock polyps.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VoidMedusaComponent : Component
{
    [DataField]
    public float HuntRange = 2048f;

    [DataField]
    public float HuntSpeed = 42f;

    [DataField]
    public float HoverRange = 24f;

    [DataField]
    public float LatchRange = 42f;

    [DataField]
    public float LatchPull = 22f;

    [DataField]
    public TimeSpan LatchAnnounceCooldown = TimeSpan.FromSeconds(8);

    [DataField]
    public float IonRange = 36f;

    [DataField]
    public float IonEnergy = 2700000f;

    [DataField]
    public TimeSpan IonDuration = TimeSpan.FromSeconds(18);

    [DataField]
    public TimeSpan IonCooldown = TimeSpan.FromSeconds(16);

    [DataField]
    public float StasisRange = 30f;

    [DataField]
    public float StasisWalkMultiplier = 0.22f;

    [DataField]
    public float StasisRunMultiplier = 0.22f;

    [DataField]
    public TimeSpan StasisDuration = TimeSpan.FromSeconds(7);

    [DataField]
    public TimeSpan StasisCooldown = TimeSpan.FromSeconds(14);

    [DataField]
    public float StasisGridDamp = 0.18f;

    [DataField]
    public EntProtoId PolypPrototype = "MobForgeVoidMedusaPolyp";

    [DataField]
    public int PolypCount = 5;

    [DataField]
    public float PolypRadius = 18f;

    [DataField]
    public TimeSpan PolypCooldown = TimeSpan.FromSeconds(32);

    [DataField]
    public int MinHealth = 100000;

    [DataField]
    public int MaxHealth = 1000000;

    [DataField]
    public float ShootRange = 180f;

    [DataField]
    public TimeSpan ShootInterval = TimeSpan.FromSeconds(2.4);

    [DataField]
    public TimeSpan NpcAbilityRangeCheck = TimeSpan.FromSeconds(1.4);

    public EntityUid? LatchedGrid;
    public TimeSpan NextLatchAnnounce;
    public TimeSpan NextIon;
    public TimeSpan NextStasis;
    public TimeSpan NextPolyp;
    public TimeSpan NextNpcCheck;
    public TimeSpan NextShot;
    public TimeSpan StasisUntil;

    [DataField]
    public SoundSpecifier? SoundLatch = new SoundPathSpecifier("/Audio/Effects/Lightning/lightningbolt.ogg")
    {
        Params = AudioParams.Default.WithVolume(3f)
    };

    [DataField]
    public SoundSpecifier? SoundIon = new SoundPathSpecifier("/Audio/Effects/Lightning/lightningbolt.ogg");

    [DataField]
    public SoundSpecifier? SoundStasis = new SoundPathSpecifier("/Audio/Magic/blink.ogg");
}
