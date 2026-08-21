using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Radio;

/// <summary>
/// A list of preset handheld radio frequencies that players can pick from.
/// </summary>
[Prototype]
public sealed partial class RadioFrequencyPresetListPrototype : IPrototype
{
    public const string PopularHandheld = "PopularHandheld";
    public const int MaxPopularCount = 10;
    public const int DefaultMinFrequency = 1000;
    public const int DefaultMaxFrequency = 30000;

    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<int> Frequencies { get; private set; } = new();
}
