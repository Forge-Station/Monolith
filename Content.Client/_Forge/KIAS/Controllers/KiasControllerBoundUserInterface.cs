using Content.Shared._Forge.KIAS.Controllers;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.KIAS.Controllers;

[UsedImplicitly]
public sealed class KiasControllerBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private KiasControllerWindow? _editor;
    private KiasControllerRackWindow? _rack;
    protected override void Open()
    {
        base.Open();
        if (UiKey is KiasControllerUiKey.Programmer)
        {
            _editor = this.CreateWindow<KiasControllerWindow>(); _editor.Edited += message => SendMessage(message);
        }
        else
        {
            _rack = this.CreateWindow<KiasControllerRackWindow>(); _rack.Changed += message => SendMessage(message);
        }
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is KiasControllerEditorState editor) _editor?.UpdateState(editor);
        if (state is KiasControllerRackState rack) _rack?.UpdateState(rack);
    }
}
