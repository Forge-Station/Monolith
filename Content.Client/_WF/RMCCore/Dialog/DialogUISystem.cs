using Content.Shared._WF.RMCCore.Dialog;

namespace Content.Client._WF.RMCCore.Dialog;

public sealed partial class DialogUISystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<DialogComponent, AfterAutoHandleStateEvent>(OnDialogState);
    }

    // WOLFGATE: RMC goes through RMCUserInterfaceSystem.TryBui, which isn't ported; same loop inline.
    private void OnDialogState(Entity<DialogComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!TryComp(ent, out UserInterfaceComponent? ui))
            return;

        foreach (var bui in ui.ClientOpenInterfaces.Values)
        {
            if (bui is DialogBui dialog)
                dialog.Refresh();
        }
    }
}
