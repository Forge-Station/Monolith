using Content.Client._Forge.Shuttles.UI;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Events;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Shuttles.BUI;

[UsedImplicitly]
public sealed class PoiCaptureConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private PoiCaptureConsoleWindow? _window;

    public PoiCaptureConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindowCenteredLeft<PoiCaptureConsoleWindow>();
        _window.CaptureStart += () => SendMessage(new PoiCaptureStartMessage());
        _window.CaptureInterrupt += () => SendMessage(new PoiCaptureInterruptMessage());
        _window.TransferOwnership += companyId =>
        {
            SendMessage(new PoiCaptureTransferOwnershipMessage
            {
                CompanyId = companyId,
            });
        };
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not PoiCaptureConsoleBoundUserInterfaceState bState)
            return;

        _window?.UpdateState(bState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _window?.Close();
            _window = null;
        }
    }
}
