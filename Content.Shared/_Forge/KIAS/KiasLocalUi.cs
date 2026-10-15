using Robust.Shared.Serialization;

namespace Content.Shared._Forge.KIAS;

[RegisterComponent]
public sealed partial class KiasManagementComponent : Component;

[RegisterComponent]
public sealed partial class KiasLocalUiComponent : Component;

public enum KiasDisplayPage : byte { Overview, Crew, Power, Navigation, Faults, Atmos, Defence }

[Serializable, NetSerializable]
public sealed class KiasWallState : BoundUserInterfaceState
{
    public bool Online;
    public int Entities;
    public int Crew;
    public KiasDisplayPage Page;
    public string Details = string.Empty;
}

[Serializable, NetSerializable]
public sealed class KiasRecorderState : BoundUserInterfaceState
{
    public bool Online;
    public List<string> Entries = new();
}

[Serializable, NetSerializable]
public sealed class KiasServiceState : BoundUserInterfaceState
{
    public string Group = string.Empty, CurrentGroup = string.Empty, GroupKind = "lighting";
    public string Details = string.Empty;
    public string SourceName = string.Empty;
    public string TargetName = string.Empty;
    public KiasServiceMode Mode;
    public string Message = string.Empty;
    public NetEntity? Source;
    public NetEntity? Target;
    public KiasCoverageGeometry? Geometry;
    public int RotaryPositions;
    public List<int> RotarySignals = new();
    public float? SensorRange;
}

[Serializable, NetSerializable]
public abstract class KiasLocalState : BoundUserInterfaceState
{
    public string Name = string.Empty;
    public KiasDeviceStatus Status;
    public string Room = string.Empty;
}

[Serializable, NetSerializable]
public sealed class KiasScannerState : KiasLocalState
{
    public KiasCoverageGeometry? Geometry;
    public int Range;
    public KiasScannerModules Modules;
}

[Serializable, NetSerializable]
public sealed class KiasSensorState : KiasLocalState
{
    public float Range;
    public float Arc = 360;
    public KiasCoverageGeometry? Geometry;
}

[Serializable, NetSerializable]
public sealed class KiasCrewState : KiasLocalState
{
    public bool Locked;
    public int DetectedCrew;
    public List<string> Registered = new();
}

[Serializable, NetSerializable]
public sealed class KiasSpeakerState : KiasLocalState
{
    public string Group = string.Empty;
    public string Message = string.Empty;
}

[Serializable, NetSerializable]
public sealed class KiasResourceState : KiasLocalState
{
    public string Details = string.Empty;
}

[Serializable, NetSerializable]
public sealed class KiasWirelessState : KiasLocalState
{
    public string Channel = string.Empty;
    public float Range;
    public List<string> TrustedTransmitters = new();
}

[Serializable, NetSerializable]
public sealed class KiasLightState : KiasLocalState
{
    public string Group = string.Empty;
    public string Color = "#FFFFFF";
    public float Brightness;
}

[Serializable, NetSerializable]
public sealed class KiasDeviceSettingsMessage : BoundUserInterfaceMessage
{
    public int RotaryPositions;
    public List<int> RotarySignals = new();
    public string Room = string.Empty;
    public string Group = string.Empty;
    public string Message = string.Empty;
    public float Range;
    public bool LockRegistration;
    public KiasDisplayPage Page;
    public string Color = "#FFFFFF";
    public float Brightness = 0.8f;
}

[Serializable, NetSerializable]
public sealed class KiasModeMessage : BoundUserInterfaceMessage
{
    public KiasServiceMode Mode;
}
