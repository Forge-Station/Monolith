using Content.Shared._Forge.BsEnergy;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.BsEnergy;

[UsedImplicitly]
public sealed class BsTransmitterBoundUserInterface : BoundUserInterface
{
    [ViewVariables] private BsTransmitterWindow? _window;

    public BsTransmitterBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<BsTransmitterWindow>();
        _window.SetEntity(Owner);

        _window.OnChangeTargetPower += targetPower => SendMessage(new ChangePowerMessage { Power = targetPower});
        _window.OnEnableToggle += args => SendMessage(new EnableToggleMessage { Enabled = args.Button.Pressed});
        _window.OnWithdraw += () => SendMessage(new WithdrawMessage());
        _window.OnPriceChanged += price => SendMessage(new PriceMessage { Price = price });
        _window.OnConnectedDevicesChanged += maxConnecting => SendMessage(new ConnectingLimitMessage { LimitConnecting = maxConnecting });
        _window.OnKick += netUid => SendMessage(new KickMessage { KickedReceiver = netUid });
        _window.OnPasswordChanged += password => SendMessage(new PasswordMessage { Password = password});
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is BsTransmitterInterfaceStateMessage transmitterInputState)
            _window?.UpdateUI(transmitterInputState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }
}
