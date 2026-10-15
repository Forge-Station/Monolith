namespace Content.Shared._Forge.KIAS;

[Flags]
public enum KiasScannerModules : ushort
{
    None = 0, Motion = 1, Id = 2, Transponder = 4, Identity = Id | Transponder, Biometric = 8, Radiation = 16, Spectral = 32,
    Connector = 64, Optical = 128, Threat = 256,
}

[RegisterComponent]
public sealed partial class KiasRoomScannerComponent : Component
{
    [DataField]
    // Сохраняем старое поле карты, геометрию определяют стены.
    public int Range = 7;
    [DataField] public bool Advanced;
    public KiasScannerModules Modules;
    public int Entities;
}

[RegisterComponent]
public sealed partial class KiasScannerModuleComponent : Component
{
    [DataField]
    public KiasScannerModules Module;
}

[RegisterComponent]
public sealed partial class KiasCrewServerComponent : Component
{
    [DataField]
    public bool RegistrationLocked;
    [DataField]
    public HashSet<string> Registered = new();
}

[RegisterComponent]
public sealed partial class KiasTransponderComponent : Component
{
    [DataField]
    public string Serial = string.Empty;
    [DataField]
    public EntityUid? Core;
    public EntityUid? IndexedGrid;
}

[RegisterComponent]
public sealed partial class KiasTrackedEntityComponent : Component
{
    public EntityUid? IndexedGrid;
}

[ByRefEvent]
public readonly record struct KiasCountsChangedEvent(EntityUid Grid, int Entities, int Crew);
