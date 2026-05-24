using Robust.Shared.Audio;

namespace Content.Shared._Forge.Weapons.ChainsawShield;

[RegisterComponent]
public sealed partial class ToggleActiveSoundRepeaterComponent : Component
{
    [DataField(required: true)]
    public SoundSpecifier Sound = default!;

    [DataField]
    public float Interval = 0.65f;

    [DataField]
    public float InitialDelay = 0.35f;

    public TimeSpan NextSound;
}
