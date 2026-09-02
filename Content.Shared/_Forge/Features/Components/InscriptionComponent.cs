using Content.Shared._EinsteinEngines.Language;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Features.Components;

/// <summary>
/// Wall inscription whose text is obfuscated unless the examiner understands <see cref="Language"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class InscriptionComponent : Component
{
    [DataField]
    public string Text = string.Empty;

    [DataField]
    public ProtoId<LanguagePrototype> Language = "Draconic";
}
