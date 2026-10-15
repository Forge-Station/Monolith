using Robust.Shared.Serialization;

namespace Content.Shared._Forge.KIAS;

[RegisterComponent]
public sealed partial class KiasDisplayComponent : Component
{
    [DataField]
    public KiasDisplayPage Page;
}

[RegisterComponent]
public sealed partial class KiasSpeakerComponent : Component
{
    [DataField]
    public string Group = "SHIP";
    public EntityUid? Tone;
    public bool FinishesShutdownTone;
    [DataField]
    public string Message = string.Empty;

    [DataField]
    public List<KiasSpeakerLink> Links = new();
}

[DataDefinition]
public sealed partial class KiasSpeakerLink
{
    [DataField]
    public EntityUid Source;
    [DataField]
    public string SourcePort = string.Empty;
    [DataField]
    public string Message = string.Empty;
}

[RegisterComponent]
public sealed partial class KiasServiceToolComponent : Component
{
    [DataField] public string Group = string.Empty;
    [DataField]
    public KiasServiceMode Mode;
    [DataField]
    public string Message = string.Empty;
    public EntityUid? Source;
    public EntityUid? Target;
    public KiasCoverageGeometry? Geometry;
    public TimeSpan NextTest;
}

public enum KiasServiceMode : byte { Link, Diagnose, Coverage, Test, Group, Room, Monitor }

[Serializable, NetSerializable]
public enum KiasUiKey : byte { Key, Wall, Recorder, Service, Scanner, Sensor, Crew, Speaker }

[Serializable, NetSerializable]
public sealed class KiasManagementState : BoundUserInterfaceState
{
    public KiasAudioSettingsView? Audio;
    public bool Online;
    public int Entities;
    public int Crew;
    public string Devices = string.Empty;
    public string Log = string.Empty;
    public string Atmos = string.Empty;
    public string CrewDetails = string.Empty;
    public string Power = string.Empty;
    public string Resources = string.Empty;
    public string Defence = string.Empty;
    public string Navigation = string.Empty;
    public string Faults = string.Empty;
    public string Alert = string.Empty;
    public string Automation = string.Empty;
}

[Serializable, NetSerializable]
public sealed class KiasRefreshMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class KiasSetMessage : BoundUserInterfaceMessage
{
    public string Message;
    public KiasSetMessage(string message) => Message = message;
}
