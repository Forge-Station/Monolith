using Content.Client.UserInterface.Fragments;
using Content.Shared._Forge.Company;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Company;

public sealed partial class CompanyManagementUi : UIFragment
{
    private CompanyManagementUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new CompanyManagementUiFragment();
        _fragment.OnMessage += ev => userInterface.SendMessage(new CartridgeUiMessage(ev));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is CompanyManagementUiState cast)
            _fragment?.UpdateState(cast);
    }
}
