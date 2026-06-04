using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Weapons;

/// <summary>
/// Forge-Change: limits how many entities a projectile may pass through.
/// Keeps penetration tuning in content, without touching core projectile logic.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class LimitedPenetrationComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public int MaxHits = 1;

    [DataField, AutoNetworkedField]
    public int Hits;
}
