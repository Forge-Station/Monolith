using Content.Shared.Explosion;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.RMCCore.Explosion;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedRMCExplosionSystem))]
public sealed partial class MobGibbedByExplosionTypeComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<ExplosionPrototype>[] Explosions = ["RMCOB", "RMCOBXenoTunnel"];

    [DataField, AutoNetworkedField]
    public FixedPoint2 Threshold = 600;
}
