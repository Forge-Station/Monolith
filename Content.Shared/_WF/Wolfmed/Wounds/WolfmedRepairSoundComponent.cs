using Robust.Shared.Audio;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// The noise a tool makes when it finishes a pass of chassis repair. Sits on the tool beside
/// <c>WeldingHealing</c>; the start of the pass already plays the tool's own <c>useSound</c>, so only the
/// finish was silent.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedRepairSoundComponent : Component
{
    [DataField]
    public SoundSpecifier? EndSound = new SoundCollectionSpecifier("WFWolfmedPanelBeat");
}
