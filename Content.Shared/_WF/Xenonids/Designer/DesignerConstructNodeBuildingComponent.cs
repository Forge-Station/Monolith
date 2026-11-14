using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using System;

namespace Content.Shared._WF.Xenonids.Designer;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DesignerConstructNodeBuildingComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid User;

    [DataField, AutoNetworkedField]
    public TimeSpan EndTime;

    [DataField, AutoNetworkedField]
    public TimeSpan BuildTime;

    [DataField, AutoNetworkedField]
    public bool ThickVariant;

    [DataField, AutoNetworkedField]
    public EntityUid? AnimationEffect;
}
