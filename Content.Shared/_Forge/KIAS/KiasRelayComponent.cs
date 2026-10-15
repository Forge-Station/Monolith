using Content.Shared.Power;

namespace Content.Shared._Forge.KIAS;

[RegisterComponent]
public sealed partial class KiasRelayComponent : Component
{
    [DataField]
    public CableType Channel = CableType.Data;

    [DataField]
    public bool Closed = true;

    public (EntityUid Grid, Vector2i Tile, CableType Channel)? Indexed;
}
