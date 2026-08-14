using Content.Shared._Forge.Warehouse;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Warehouse.UI;

[UsedImplicitly]
public sealed class WarehouseSiloBoundUserInterface : BoundUserInterface
{
    private WarehouseSiloMenu? _window;

    public WarehouseSiloBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WarehouseSiloMenu>();
        _window.OnWithdraw += stackType => SendMessage(new WarehouseSiloWithdrawMessage(stackType));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WarehouseSiloBuiState cast)
            _window?.Update(cast);
    }
}
