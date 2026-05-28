using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Weapons.ChainsawShield;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ChainsawShieldSlowedComponent : Component
{
    [DataField, AutoNetworkedField]
    public float WalkSpeedModifier = 0.5f;

    [DataField, AutoNetworkedField]
    public float SprintSpeedModifier = 0.5f;
}
