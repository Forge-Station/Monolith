using Robust.Shared.Prototypes;

namespace Content.Shared._WF.CargoLoad;

/// <summary>Marks cargo whose weight loads the ship carrying it. Its class comes from the cargo table unless set here.</summary>
[RegisterComponent]
public sealed partial class WFCargoMassComponent : Component
{
    /// <summary>A weight class overriding the table's, such as Bulk for a heavy consignment.</summary>
    [DataField]
    public string? Class;

    /// <summary>Tonnes per unit of a stack, such as a sheet or a piece of ore; when set it replaces the class.</summary>
    [DataField]
    public float PerUnit;
}

/// <summary>Cargo weight tuning: classes in tonnes, each good's class, and how load slows a ship.</summary>
[Prototype("wfCargoLoad")]
public sealed partial class WFCargoLoadPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Weight classes and their tonnes, in grid-mass units.</summary>
    [DataField]
    public Dictionary<string, float> Classes = new();

    /// <summary>Each good's class.</summary>
    [DataField]
    public Dictionary<EntProtoId, string> Goods = new();

    /// <summary>The class of a good not in the table.</summary>
    [DataField]
    public string DefaultClass = "Standard";

    /// <summary>Scales every weight, for tuning against hull masses.</summary>
    [DataField]
    public float MassScale = 1f;

    /// <summary>Lowest share of thrust a loaded ship keeps in space.</summary>
    [DataField]
    public float ThrustFloor = 0.4f;

    /// <summary>Extra FTL spool per tonne, in seconds.</summary>
    [DataField]
    public float SpoolPerTonne = 0.1f;

    /// <summary>Most extra FTL spool cargo can add, in seconds.</summary>
    [DataField]
    public float SpoolCap = 6f;

    /// <summary>Thrust factors at which the helm's pilots are warned, highest first.</summary>
    [DataField]
    public List<float> Bands = new() { 0.8f, 0.6f };

    /// <summary>How far the factor must climb back past a band before it warns again.</summary>
    [DataField]
    public float Hysteresis = 0.05f;

    /// <summary>Share of their loose weight that materials keep inside a silo, lathe or other material storage.</summary>
    [DataField]
    public float StoredFactor = 0.5f;
}
