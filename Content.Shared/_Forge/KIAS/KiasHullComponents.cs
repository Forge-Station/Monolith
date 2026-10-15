namespace Content.Shared._Forge.KIAS;

[RegisterComponent]
public sealed partial class KiasHullStructureComponent : Component;

[RegisterComponent]
public sealed partial class KiasHullSensorComponent : Component
{
    [DataField] public float Range = 100;
}

[RegisterComponent]
public sealed partial class KiasIntegrityMonitorComponent : Component;

[RegisterComponent]
public sealed partial class KiasCollisionMonitorComponent : Component
{
    [DataField]
    public float MinimumSpeed = 1;
}

[ByRefEvent]
public readonly record struct KiasHullDamageEvent(EntityUid Grid, EntityUid Structure, float Damage);

[ByRefEvent]
public readonly record struct KiasHullImpactEvent(EntityUid Grid, EntityUid Structure);

[ByRefEvent]
public readonly record struct KiasGridCollisionEvent(EntityUid Grid, EntityUid OtherGrid, float RelativeSpeed);
