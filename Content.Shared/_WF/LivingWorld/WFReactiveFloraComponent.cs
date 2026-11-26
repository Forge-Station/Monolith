using Content.Shared.Alert;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.LivingWorld;

/// <summary>What a reactive plant does to a living mob that comes within its range.</summary>
public enum WFReactiveFloraBehaviour : byte
{
    /// <summary>Strikes the nearest mob with <see cref="WFReactiveFloraComponent.Damage"/>, with optional knockback and stun.</summary>
    Lash,

    /// <summary>Holds the nearest mob in place for <see cref="WFReactiveFloraComponent.HoldTime"/>.</summary>
    Grab,

    /// <summary>Fires <see cref="WFReactiveFloraComponent.Projectile"/> at the nearest mob.</summary>
    Spit,

    /// <summary>Releases a cloud of <see cref="WFReactiveFloraComponent.Solution"/>.</summary>
    Cloud,

    /// <summary>Swells for <see cref="WFReactiveFloraComponent.ArmDelay"/>, then bursts.</summary>
    Explode,

    /// <summary>Irradiates every mob in range.</summary>
    Irradiate,

    /// <summary>Puts <see cref="WFReactiveFloraComponent.Solution"/> into the nearest mob's bloodstream.</summary>
    Inject,
}

/// <summary>A reactive plant's look, set on its appearance under <see cref="WFReactiveFloraVisuals.State"/>.</summary>
[Serializable, NetSerializable]
public enum WFReactiveFloraState : byte
{
    Idle,

    /// <summary>Swelling before a burst, holding a mob, or briefly after any other reaction.</summary>
    Armed,

    /// <summary>Burst and waiting to regrow.</summary>
    Spent,
}

[Serializable, NetSerializable]
public enum WFReactiveFloraVisuals : byte
{
    State,
}

/// <summary>
/// A stationary plant that reacts to living mobs within <see cref="Range"/>. It looks only when its timer runs out, every
/// <see cref="CheckInterval"/> while nothing is near and <see cref="Cooldown"/> after it reacts.
/// </summary>
[RegisterComponent]
public sealed partial class WFReactiveFloraComponent : Component
{
    [DataField(required: true)]
    public WFReactiveFloraBehaviour Behaviour;

    /// <summary>Metres within which it notices a mob.</summary>
    [DataField]
    public float Range = 1.5f;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(3);

    /// <summary>How often it looks while idle.</summary>
    [DataField]
    public TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>Mobs it affects per reaction, nearest first; 0 for every mob in range.</summary>
    [DataField]
    public int MaxTargets = 1;

    /// <summary>Only reaches mobs with nothing solid between them and the plant.</summary>
    [DataField]
    public bool RequireLineOfSight = true;

    /// <summary>Mobs of these factions are left alone.</summary>
    [DataField]
    public List<ProtoId<NpcFactionPrototype>> IgnoreFactions = new();

    /// <summary>Mobs of these factions are tended instead: they get <see cref="HealDamage"/> and <see cref="HealSolution"/>.</summary>
    [DataField]
    public List<ProtoId<NpcFactionPrototype>> HealInsteadFactions = new();

    /// <summary>Damage change for a tended mob; use negative values.</summary>
    [DataField]
    public DamageSpecifier? HealDamage;

    /// <summary>Put into a tended mob's bloodstream.</summary>
    [DataField]
    public Solution? HealSolution;

    [DataField]
    public SoundSpecifier? HealSound;

    /// <summary>Played at the plant when it reacts.</summary>
    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>Shown to the mob it reacts to.</summary>
    [DataField]
    public LocId? Popup;

    /// <summary>How long it shows <see cref="WFReactiveFloraState.Armed"/> after a reaction that isn't a hold or a swell.</summary>
    [DataField]
    public TimeSpan ActiveTime = TimeSpan.FromSeconds(0.6);

    /// <summary>Lash and Grab: damage dealt to the mob.</summary>
    [DataField]
    public DamageSpecifier? Damage;

    /// <summary>Lash: metres the mob is thrown away from the plant.</summary>
    [DataField]
    public float Knockback;

    [DataField]
    public float KnockbackSpeed = 6f;

    /// <summary>Lash: stun applied to the mob.</summary>
    [DataField]
    public TimeSpan StunTime = TimeSpan.Zero;

    /// <summary>Lash: knockdown applied to the mob.</summary>
    [DataField]
    public TimeSpan KnockdownTime = TimeSpan.Zero;

    /// <summary>Grab: how long it holds on.</summary>
    [DataField]
    public TimeSpan HoldTime = TimeSpan.FromSeconds(4);

    /// <summary>Grab: movement speed left to the mob, as a multiplier.</summary>
    [DataField]
    public float HeldSpeed;

    /// <summary>Grab: damage the plant must take while holding to let go.</summary>
    [DataField]
    public float CutFreeDamage = 10f;

    /// <summary>Grab: seconds each struggle (a click on the alert) takes off the hold.</summary>
    [DataField]
    public TimeSpan StruggleStep = TimeSpan.FromSeconds(0.75);

    [DataField]
    public ProtoId<AlertPrototype> GrabAlert = "WFFloraGrabbed";

    [DataField]
    public SoundSpecifier? ReleaseSound;

    /// <summary>Spit: what it fires.</summary>
    [DataField]
    public EntProtoId? Projectile;

    [DataField]
    public float ProjectileSpeed = 12f;

    /// <summary>Cloud and Inject: what it releases or injects. Explode: an optional cloud on the burst.</summary>
    [DataField]
    public Solution? Solution;

    /// <summary>Cloud: the cloud entity; foam works too.</summary>
    [DataField]
    public EntProtoId CloudPrototype = "Smoke";

    [DataField]
    public float CloudDuration = 6f;

    /// <summary>Cloud: tiles the cloud spreads over.</summary>
    [DataField]
    public int CloudSpread = 4;

    /// <summary>Explode: how long it swells before it bursts.</summary>
    [DataField]
    public TimeSpan ArmDelay = TimeSpan.FromSeconds(1.5);

    /// <summary>Explode: played when it starts to swell.</summary>
    [DataField]
    public SoundSpecifier? ArmSound;

    /// <summary>Explode: shown to mobs in range when it starts to swell.</summary>
    [DataField]
    public LocId? ArmPopup;

    /// <summary>Explode: metres within which the burst hurts.</summary>
    [DataField]
    public float BurstRadius = 1.5f;

    /// <summary>Explode: damage dealt to each mob within <see cref="BurstRadius"/>, ignored factions excepted.</summary>
    [DataField]
    public DamageSpecifier? BurstDamage;

    /// <summary>Explode: spilt where it stood, such as blood.</summary>
    [DataField]
    public Solution? BurstSpill;

    /// <summary>Explode: spawned where it stood.</summary>
    [DataField]
    public List<EntProtoId> BurstSpawns = new();

    /// <summary>Explode: an explosion type to queue as well; none by default. It never breaks tiles.</summary>
    [DataField]
    public string? ExplosionType;

    [DataField]
    public float ExplosionIntensity = 4f;

    [DataField]
    public float ExplosionSlope = 2f;

    [DataField]
    public float ExplosionMaxTileIntensity = 2f;

    /// <summary>Explode: the plant is deleted by its burst; otherwise it is spent until <see cref="RegrowTime"/> passes.</summary>
    [DataField]
    public bool DeleteOnBurst = true;

    [DataField]
    public TimeSpan RegrowTime = TimeSpan.FromMinutes(5);

    /// <summary>Explode: it bursts at once when damaged.</summary>
    [DataField]
    public bool BurstOnDamage = true;

    /// <summary>Irradiate: rads a second while a mob stays in range.</summary>
    [DataField]
    public float RadsPerSecond = 2f;

    [ViewVariables]
    public WFReactiveFloraState State = WFReactiveFloraState.Idle;

    [ViewVariables]
    public TimeSpan NextCheck;

    /// <summary>When an armed plant bursts, a spent one regrows, or a brief Armed look ends.</summary>
    [ViewVariables]
    public TimeSpan StateEnd;

    /// <summary>The mob it is holding.</summary>
    [ViewVariables]
    public EntityUid? Held;

    /// <summary>Damage the plant has taken since it took hold.</summary>
    [ViewVariables]
    public float DamageWhileHolding;
}

/// <summary>Raised on a mob held by a plant when it clicks the hold alert to struggle.</summary>
public sealed partial class WFFloraStruggleAlertEvent : BaseAlertEvent;
