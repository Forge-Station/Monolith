using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.KIAS.Controllers;

[RegisterComponent]
public sealed partial class KiasControllerCardComponent : Component
{
    [DataField] public KiasControllerProgram Program = new();
    [DataField] public uint Revision;
    [DataField] public bool Enabled = true;
}

[RegisterComponent]
public sealed partial class KiasControllerRackComponent : Component
{
    public const int SlotCount = 8;
    public static string SlotId(int index) => $"kias-controller-{index + 1}";
    [DataField] public float BasePowerLoad = 100;
    [DataField] public float PerControllerLoad = 25;
    public float CurrentLoad;
}

[RegisterComponent]
public sealed partial class KiasControllerProgrammerComponent : Component
{
    public const string SlotId = "kias-controller";
    public KiasControllerProgram? Draft;
    public EntityUid? Card;
    public EntityUid? Editor;
    public bool DraftDirty;
    public bool DraftEnabled = true;
    public uint DraftRevision;
    public string CatalogQuery = string.Empty;
    public int CatalogPage;
    public List<string> Errors = new();
}

[Prototype("kiasControllerProfile")]
public sealed partial class KiasControllerProfilePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public string Component = string.Empty;
    [DataField] public List<KiasGraphPort> Ports = new();
}

[Prototype("kiasControllerProgram")]
public sealed partial class KiasControllerProgramPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public KiasControllerProgram Program = new();
    [DataField] public bool Enabled = true;
}

[RegisterComponent]
public sealed partial class KiasPowerProfileComponent : Component;
[RegisterComponent]
public sealed partial class KiasAtmosProfileComponent : Component;
[RegisterComponent]
public sealed partial class KiasNavigationProfileComponent : Component;
