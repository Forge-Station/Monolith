using Robust.Shared.Audio;

namespace Content.Shared._WF.Wolfmed.Sounds;

/// <summary>
/// A sound a melee weapon plays over its own hit sound when it lands on flesh (playtest 4): the crowbar's clang
/// over its thud. The weapon's <c>soundHit</c> still plays; this is a second, separate sound.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedHitOverlaySoundComponent : Component
{
    [DataField(required: true)]
    public SoundSpecifier Sound = default!;
}
