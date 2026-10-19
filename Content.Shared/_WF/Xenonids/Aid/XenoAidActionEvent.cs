using Content.Shared.Actions;

namespace Content.Shared._WF.Xenonids.Aid;

public sealed partial class XenoAidActionEvent : EntityTargetActionEvent
{
    [DataField]
    public XenoAidMode aidType = XenoAidMode.Healing;
}
