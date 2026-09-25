using Robust.Shared.Audio;

namespace Content.Server._Forge.Botany.BluespaceTomato;

[RegisterComponent]
public sealed partial class BluespaceTomatoComponent : Component
{
    [DataField("radius")]
    public float Radius = 5f;

    [DataField("cooldown")]
    public float Cooldown = 30f;

    [DataField("attempts")]
    public int Attempts = 10;

    [DataField("sound")]
    public SoundPathSpecifier SoundOnTeleport = new("/Audio/Effects/teleport_arrival.ogg");

    public bool Spent;
}
