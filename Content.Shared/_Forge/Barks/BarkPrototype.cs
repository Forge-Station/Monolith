using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Barks;

/// <summary>
/// A prototype for the available barges.
/// </summary>
[Prototype("bark")]
public sealed partial class BarkPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// The name of the voice.
    /// </summary>
    [DataField("name")]
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// A set of sounds used for speech.
    /// </summary>
    [DataField("soundFiles", required: true)]
    public List<string> SoundFiles { get; private set; } = new();

    /// <summary>
    /// Whether it is available for selection.
    /// </summary>
    [DataField("roundStart")]
    public bool RoundStart { get; private set; } = true;
}
