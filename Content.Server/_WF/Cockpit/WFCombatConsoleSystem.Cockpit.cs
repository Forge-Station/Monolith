using Content.Server._WF.Cockpit;
using Content.Shared._Mono.FireControl;
using Content.Shared._WF.CombatConsole;

namespace Content.Server._WF.CombatConsole;

public sealed partial class WFCombatConsoleSystem
{
    /// <summary>Uses the normal group and flare handlers for an authorized cockpit operator.</summary>
    public bool WfCockpitCommand(EntityUid uid, FireControlConsoleComponent console, EntityUid actor,
        BoundUserInterfaceMessage command)
    {
        if (!EntityManager.System<WFCockpitGunnerySystem>().CanOperate(actor, uid))
            return false;
        switch (command)
        {
            case WFSaveWeaponGroupMessage group:
                OnSaveGroup(uid, console, new WFSaveWeaponGroupMessage(group.Slot, group.Weapons) { Actor = actor });
                return true;
            case WFAutomaticFlaresMessage automatic:
                OnAutomatic(uid, console, new WFAutomaticFlaresMessage(automatic.Enabled) { Actor = actor });
                return true;
            case WFDispenseFlaresMessage:
                OnDispense(uid, console, new WFDispenseFlaresMessage { Actor = actor });
                return true;
            default:
                return false;
        }
    }
}
