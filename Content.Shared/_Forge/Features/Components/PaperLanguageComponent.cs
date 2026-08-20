using Content.Shared._EinsteinEngines.Language;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Features.Components;

/// <summary>
/// One stretch of handwriting in a single language.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class PaperLanguageSegment
{
    [DataField]
    public string Text = string.Empty;

    [DataField]
    public ProtoId<LanguagePrototype> Language = "TauCetiBasic";
}

/// <summary>
/// Paper written in one or more languages. Unknown stretches are obfuscated for the reader.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PaperLanguageComponent : Component
{
    /// <summary>
    /// Language of the most recent stretch, and of pre-printed text until segments are recorded.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<LanguagePrototype> Language = "TauCetiBasic";

    [DataField, AutoNetworkedField]
    public List<PaperLanguageSegment> Segments = new();
}
