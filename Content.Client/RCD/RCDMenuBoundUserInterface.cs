using Content.Shared.RCD;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client.RCD;

[UsedImplicitly]
public sealed partial class RCDMenuBoundUserInterface : BoundUserInterface
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IClyde _displayManager = default!;
    [Dependency] private IInputManager _inputManager = default!;

    private RCDMenu? _menu;

    public RCDMenuBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<RCDMenu>();
        _menu.SetEntity(Owner);
        _menu.SendRCDSystemMessageAction += SendRCDSystemMessage;

        // Open the menu, centered on the mouse
        var vpSize = _displayManager.ScreenSize;
        _menu.OpenCenteredAt(_inputManager.MouseScreenPosition.Position / vpSize);
    }

    public void SendRCDSystemMessage(ProtoId<RCDPrototype> protoId)
    {
        // Forge-Change: refresh placement ghost immediately for the selected recipe
        _entManager.System<RCDConstructionGhostSystem>().NotifyRecipeSelected(protoId);

        // A predicted message cannot be used here as the RCD UI is closed immediately
        // after this message is sent, which will stop the server from receiving it
        SendMessage(new RCDSystemMessage(protoId));
    }
}
