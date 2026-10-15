using Robust.Shared.Serialization;

namespace Content.Shared._Forge.KIAS;

[RegisterComponent]
public sealed partial class KiasCoreComponent : Component
{
    [DataField]
    public bool Enabled = true;
}

[RegisterComponent]
public sealed partial class KiasMasterKeyComponent : Component;

[RegisterComponent]
public sealed partial class KiasDeviceComponent : Component
{
    [DataField]
    public KiasDeviceRole Role;

    [DataField]
    public string Room = string.Empty;

    [DataField]
    public string Identifier = string.Empty;

    public EntityUid? RegisteredGrid;
    public KiasDeviceStatus Status = KiasDeviceStatus.Disconnected;
}

[RegisterComponent]
public sealed partial class KiasDataCableComponent : Component
{
    public EntityUid? RegisteredGrid;
}

public enum KiasDeviceRole : byte
{
    Core,
    Defence,
    Atmosphere,
    Power,
    Crew,
    Navigation,
    Recorder,
    Speaker,
    Display,
    Scanner,
    Horizon,
    Hull,
    Relay,
    Suppression,
    PdcRadar,
    WeaponFlash,
    Proximity,
    MasterSwitch,
    Light,
    PdcWeapon,
    Iff,
    Docking,
    Adapter,
    Resource,
    Controller,
}

[Serializable, NetSerializable]
public enum KiasVisuals : byte { Status, CardInserted }

[Serializable, NetSerializable]
public enum KiasDeviceStatus : byte
{
    Offline,
    Online,
    NoPower,
    NoDataPath,
    Disconnected,
    DuplicateCore,
}

[ByRefEvent]
public readonly record struct KiasAvailabilityChangedEvent(EntityUid Grid, bool Active);

[ByRefEvent]
public readonly record struct KiasTopologyChangedEvent(EntityUid Grid, uint Revision);

[ByRefEvent]
public readonly record struct KiasAnnouncementEvent(EntityUid Grid, string Message, bool Warning = false, EntityUid? Speaker = null, string Group = "", string Key = "", KiasAudioChannel? Channel = null);
