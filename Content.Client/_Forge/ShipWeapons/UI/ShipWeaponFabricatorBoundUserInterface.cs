using Content.Shared._Forge.ShipWeapons;
using JetBrains.Annotations;

namespace Content.Client._Forge.ShipWeapons.UI;

[UsedImplicitly]
public sealed class ShipWeaponFabricatorBoundUserInterface : BoundUserInterface
{
    private ShipWeaponFabricatorWindow? _window;

    public ShipWeaponFabricatorBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new ShipWeaponFabricatorWindow(this);
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _window?.Dispose();
        _window = null;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not ShipWeaponFabricatorState cast)
            return;

        _window?.UpdateState(cast);
    }

    public void SendStart()
    {
        SendMessage(new ShipWeaponFabricatorStartMessage());
    }

    public void SendEject()
    {
        SendMessage(new ShipWeaponFabricatorEjectMessage());
    }

    public void SendEjectPart(string partPrototype, int amount)
    {
        SendMessage(new ShipWeaponFabricatorEjectPartMessage(partPrototype, amount));
    }
}
