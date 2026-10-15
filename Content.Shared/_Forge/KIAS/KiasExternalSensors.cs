namespace Content.Shared._Forge.KIAS;

[RegisterComponent]
public sealed partial class KiasWeaponFlashComponent : Component
{
    [DataField]
    public float Range = 500;
}

[RegisterComponent]
public sealed partial class KiasProximityComponent : Component
{
    [DataField]
    public float Range = 250;
    public readonly HashSet<EntityUid> Contacts = new();
}

[ByRefEvent]
public readonly record struct KiasWeaponFiredEvent(EntityUid Source);
[ByRefEvent]
public readonly record struct KiasWeaponFlashEvent(EntityUid Grid, EntityUid Source, KiasContactDisposition Disposition);
[ByRefEvent]
public readonly record struct KiasProximityEvent(EntityUid Grid, EntityUid Contact, float Distance, KiasContactDisposition Disposition);

[RegisterComponent]
public sealed partial class KiasRecorderComponent : Component
{
    [DataField]
    public List<string> Entries = new();
    [DataField] public List<string> ProtocolEvents = new();
}
