using System.Numerics;
using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Xenomorphs;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ForgeXenoFortifyComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Active;

    [DataField]
    public float DamageMultiplier = 0.4f;

    [DataField]
    public float SpeedMultiplier = 0.15f;
}

[RegisterComponent]
public sealed partial class ForgeXenoLeapComponent : Component
{
    [DataField]
    public bool Charging;

    [DataField]
    public float StunSeconds;

    [DataField]
    public DamageSpecifier? HitDamage;

    [DataField]
    public bool PullOnHit;

    public TimeSpan ChargeUntil;
}

[RegisterComponent]
public sealed partial class ForgeXenoWeedsComponent : Component
{
    /// <summary>
    /// Walk speed multiplier applied to xenomorphs standing on these weeds.
    /// Non-xenos are handled by <c>SpeedModifierContacts</c> instead.
    /// </summary>
    [DataField]
    public float XenoWalkSpeedModifier = 1.2f;

    [DataField]
    public float XenoSprintSpeedModifier = 1.2f;
}

/// <summary>
/// Marker that a xenomorph is currently contacting hive weeds.
/// </summary>
[RegisterComponent]
public sealed partial class ForgeXenoOnWeedsComponent : Component;

/// <summary>
/// Facehugger egg that stuns the first non-xenomorph who bumps it.
/// </summary>
[RegisterComponent]
public sealed partial class ForgeXenoEggTrapComponent : Component
{
    [DataField]
    public float StunSeconds = 10f;

    [DataField]
    public EntProtoId OpenedPrototype = "XenoEggOpened";
}

/// <summary>
/// A ram that speeds up, throws whatever it can pass, and stops on a solid barrier.
/// </summary>
[RegisterComponent]
public sealed partial class ForgeXenoChargeComponent : Component
{
    [DataField]
    public Vector2 Direction;

    [DataField]
    public float Speed = 3f;

    [DataField]
    public float MaxSpeed = 18f;

    [DataField]
    public float Acceleration = 40f;

    [DataField]
    public float DistanceLeft = 8f;

    [DataField]
    public float StunSeconds = 2.5f;

    [DataField]
    public DamageSpecifier? HitDamage;

    public HashSet<EntityUid> Hit = new();
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ForgeXenoPheromonesComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Active;

    [DataField]
    public float Range = 6f;

    [DataField]
    public float SpeedBonus = 1.15f;
}
