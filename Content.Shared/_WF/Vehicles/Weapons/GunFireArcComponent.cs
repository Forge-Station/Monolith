using Robust.Shared.GameStates;

namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>The cone the gun can shoot into, centred on the facing of what it sits in.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GunFireArcComponent : Component
{
    [DataField, AutoNetworkedField]
    public Angle Arc = Angle.FromDegrees(90);

    [DataField, AutoNetworkedField]
    public Angle AngleOffset = Angle.Zero;
}
