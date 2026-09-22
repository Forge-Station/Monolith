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
