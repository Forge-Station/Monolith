namespace Content.Server._WF.Xenonids.Construction;

[RegisterComponent]
[Access(typeof(XenoPylonSystem))]
public sealed partial class XenoHiveCoreRoleComponent : Component
{
    [DataField]
    public EntityUid? Core;
}
