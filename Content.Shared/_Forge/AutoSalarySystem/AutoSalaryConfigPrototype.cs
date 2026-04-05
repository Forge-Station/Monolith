using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.AutoSalarySystem;

[Prototype("autoSalaryConfig")]
public sealed partial class AutoSalaryConfigPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("payInterval", required: true)]
    public float PayIntervalSeconds { get; private set; }
}
