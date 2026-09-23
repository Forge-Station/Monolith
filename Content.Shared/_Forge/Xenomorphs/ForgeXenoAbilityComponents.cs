using System.Numerics;
using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Xenomorphs;

[RegisterComponent, NetworkedComponent]
public sealed partial class ForgeXenoRestingComponent : Component;

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
public sealed partial class ForgeXenoWeedsComponent : Component;

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
