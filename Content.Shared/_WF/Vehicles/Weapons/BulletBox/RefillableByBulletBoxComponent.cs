using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Vehicles.Weapons.BulletBox;

/// <summary>Marks a gun or magazine that bullet boxes of <see cref="BulletType"/> can fill.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(BulletBoxSystem))]
public sealed partial class RefillableByBulletBoxComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public EntProtoId? BulletType;
}
