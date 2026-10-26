using Content.Shared._Forge.BsEnergy;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.BsEnergy;

[UsedImplicitly]
public sealed class BsReceiverAuthenticationBoundUserInterface : BoundUserInterface
{
    [ViewVariables] private BsReceiverAuthenticationWindow? _window;

    public BsReceiverAuthenticationBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<BsReceiverAuthenticationWindow>();

        _window.OnPasswordEntered += (transmitter, password, requestId) => SendMessage(new SendPasswordToConnectMessage { Transmitter = transmitter, Password = password, RequestId = requestId});
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is BsReceiverAuthenticationInterfaceStateMessage authenticationInputState)
            _window?.UpdateUI(authenticationInputState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }
}
