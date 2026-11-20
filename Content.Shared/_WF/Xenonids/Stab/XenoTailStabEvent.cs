using Content.Shared.Actions;

namespace Content.Shared._WF.Xenonids.Stab;

// WOLFGATE: RMC's world target carries the clicked entity; here that is an entity-world target
public sealed partial class XenoTailStabEvent : EntityWorldTargetActionEvent

{
    [DataField]
    public bool UseAltTailStab = false;
}
