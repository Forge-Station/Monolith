using Content.Client.UserInterface.Fragments;
using Content.Shared._Forge.Company;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Company;

public sealed partial class CompanyManagementUi : UIFragment
{
    private CompanyManagementWindow? _window;
    private CompanyManagementPdaNotice? _notice;
    private BoundUserInterface? _bui;
    private EntityUid? _cartridge;
    private bool _userClosed;

    public override Control GetUIFragmentRoot()
    {
        if (_notice == null || _notice.Disposed)
        {
            _notice = new CompanyManagementPdaNotice(ShowWindow);
            _notice.OnDisposed += OnNoticeDisposed;
        }

        return _notice;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _bui = userInterface;
        _cartridge = fragmentOwner;

        if (_userClosed)
            return;

        EnsureWindow();
        if (_window is { IsOpen: false })
            _window.OpenCentered();
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is CompanyManagementUiState cast)
            _window?.Fragment.UpdateState(cast);
    }

    private void ShowWindow()
    {
        _userClosed = false;
        EnsureWindow();
        if (_window is { IsOpen: false })
            _window.OpenCentered();
    }

    private void EnsureWindow()
    {
        if (_window is { Disposed: false })
            return;

        var fragment = new CompanyManagementUiFragment();
        fragment.OnMessage += ev => _bui?.SendMessage(new CartridgeUiMessage(ev));
        _window = new CompanyManagementWindow(fragment);
        _window.OnClose += OnWindowClosed;
    }

    private void OnWindowClosed()
    {
        if (_userClosed || _window == null || _bui == null || _cartridge is not { } cartridge)
            return;

        _userClosed = true;
        _bui.SendMessage(new CartridgeLoaderUiMessage(
            IoCManager.Resolve<IEntityManager>().GetNetEntity(cartridge),
            CartridgeUiMessageAction.Deactivate));
    }

    private void OnNoticeDisposed()
    {
        _userClosed = false;
        var window = _window;
        _window = null;
        if (window is { IsOpen: true })
            window.Close();
    }
}
