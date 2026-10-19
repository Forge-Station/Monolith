using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Designer;

[Access(typeof(DesignerConstructNodeSystem))]
[RegisterComponent, NetworkedComponent]
public sealed partial class CanBuildThickFromConstructNodeComponent : Component
{
}
