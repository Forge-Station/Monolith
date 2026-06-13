using Content.Shared._Forge.Shipyard;
using JetBrains.Annotations;

namespace Content.Client._Forge.Shipyard.UI;

[UsedImplicitly]
public sealed class ShipFabricatorBoundUserInterface : BoundUserInterface
{
    private ShipFabricatorWindow? _window;

    public ShipFabricatorBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = new ShipFabricatorWindow(this);
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;
        _window?.Dispose();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is ShipFabricatorBoundUserInterfaceState cast)
            _window?.UpdateState(cast);
    }

    public void SendStart() => SendMessage(new ShipFabricatorStartMessage());
    public void SendEject() => SendMessage(new ShipFabricatorEjectBlueprintMessage());
}
