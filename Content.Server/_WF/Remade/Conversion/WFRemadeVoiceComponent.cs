using Robust.Shared.Audio;

namespace Content.Server._WF.Remade.Conversion;

/// <summary>The sounds one of the Remade makes: mutters while it hunts, cries when hurt, a last noise when it dies.</summary>
[RegisterComponent]
public sealed partial class WFRemadeVoiceComponent : Component
{
    [DataField]
    public SoundSpecifier? Speech;

    [DataField]
    public SoundSpecifier? Pain;

    [DataField]
    public SoundSpecifier? Death;

    /// <summary>Its long cry: what crew hear before it arrives.</summary>
    [DataField]
    public SoundSpecifier? Shout;

    [ViewVariables]
    public TimeSpan NextSpeech;

    [ViewVariables]
    public TimeSpan NextPain;
}
