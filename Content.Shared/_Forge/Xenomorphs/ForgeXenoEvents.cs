using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Xenomorphs;

public sealed partial class ForgeXenoRestActionEvent : InstantActionEvent;

public sealed partial class ForgeXenoHideActionEvent : InstantActionEvent;

public sealed partial class ForgeXenoFortifyActionEvent : InstantActionEvent;

public sealed partial class ForgeXenoZoomActionEvent : InstantActionEvent;

public sealed partial class ForgeXenoPheromonesActionEvent : InstantActionEvent;

public sealed partial class ForgeXenoScreechActionEvent : InstantActionEvent
{
    [DataField]
    public float PlasmaCost = 250f;

    [DataField]
    public float Range = 7f;

    [DataField]
    public float StunSeconds = 3f;
}

public sealed partial class ForgeXenoStompActionEvent : InstantActionEvent
{
    [DataField]
    public float PlasmaCost = 50f;

    [DataField]
    public float Range = 1.8f;

    [DataField]
    public float StunSeconds = 2f;

    [DataField]
    public DamageSpecifier? Damage;
}

public sealed partial class ForgeXenoTailStabActionEvent : EntityTargetActionEvent
{
    [DataField]
    public float PlasmaCost;

    [DataField]
    public DamageSpecifier? Damage;
}

public sealed partial class ForgeXenoAcidActionEvent : WorldTargetActionEvent
{
    [DataField]
    public float PlasmaCost = 75f;

    [DataField]
    public EntProtoId Projectile = "BulletXenoAcid";

    [DataField]
    public float Speed = 18f;
}

public sealed partial class ForgeXenoPunchActionEvent : EntityTargetActionEvent
{
    [DataField]
    public float PlasmaCost;

    [DataField]
    public float StunSeconds = 1.5f;

    [DataField]
    public DamageSpecifier? Damage;
}

public sealed partial class ForgeXenoFlingActionEvent : EntityTargetActionEvent
{
    [DataField]
    public float PlasmaCost = 20f;

    [DataField]
    public float ThrowSpeed = 14f;

    [DataField]
    public float StunSeconds = 1f;
}

public sealed partial class ForgeXenoTransferPlasmaActionEvent : EntityTargetActionEvent
{
    [DataField]
    public float Amount = 50f;
}

public sealed partial class ForgeXenoGutActionEvent : EntityTargetActionEvent
{
    [DataField]
    public float PlasmaCost = 200f;

    [DataField]
    public DamageSpecifier? Damage;
}

public sealed partial class ForgeXenoSpitActionEvent : WorldTargetActionEvent
{
    [DataField]
    public float PlasmaCost = 25f;

    [DataField]
    public EntProtoId Projectile = "BulletAcid";

    [DataField]
    public float Speed = 25f;
}

public sealed partial class ForgeXenoLeapActionEvent : WorldTargetActionEvent
{
    [DataField]
    public float PlasmaCost;

    [DataField]
    public float Speed = 14f;

    [DataField]
    public float StunSeconds = 2f;

    [DataField]
    public bool PullOnHit;

    [DataField]
    public DamageSpecifier? HitDamage;

    /// <summary>
    /// Rush with speed instead of blinking to the target. Stops on walls.
    /// </summary>
    [DataField]
    public bool Smash;
}

public sealed partial class ForgeXenoConstructActionEvent : WorldTargetActionEvent
{
    [DataField]
    public float PlasmaCost = 75f;

    [DataField]
    public EntProtoId Prototype = "XenoWeeds";

    [DataField]
    public float Delay = 1.5f;
}

[Serializable, NetSerializable]
public sealed partial class ForgeXenoConstructDoAfterEvent : DoAfterEvent
{
    public string Prototype = string.Empty;
    public float PlasmaCost;
    public NetCoordinates Target;

    public override DoAfterEvent Clone() => this;
}
