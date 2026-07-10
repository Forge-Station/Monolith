using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Weapons;

[RegisterComponent, NetworkedComponent]
public sealed partial class KnockdownOnHitComponent : Component
{
    [DataField("duration")]
    public float Duration = 0.5f;

    [DataField("chance")]
    public float Chance = 1.0f;
}
