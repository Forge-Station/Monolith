using Content.Shared._Forge.BsEnergy;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._Forge.BsEnergy;

[UsedImplicitly]
public sealed class BsReceiverBoundUserInterface : BoundUserInterface
{
    [ViewVariables] private BsReceiverWindow? _window;

    public BsReceiverBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<BsReceiverWindow>();
        _window.SetEntity(Owner);

        _window.OnWithdraw += () => SendMessage(new WithdrawMessage());
        _window.OnPressedChoiceServer += transmitter => SendMessage(new ChoiceTransmitterMessage { Transmitter = transmitter});
        _window.OnOpenAuthentication += transmitter => SendMessage(new OpenAuthenticationMessage { Transmitter = transmitter });
        _window.OnPowerRequest += changePower => SendMessage(new ChangePowerMessage { Power = changePower});
        _window.OnEnableToggle += args => SendMessage(new EnableToggleMessage { Enabled = args.Button.Pressed});
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is BsReceiverInterfaceStateMessage receiverInputState)
            _window?.UpdateUI(receiverInputState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        Timer.Spawn(TimeSpan.Zero,
            () =>
        {
            if (EntMan.TryGetComponent<UserInterfaceComponent>(Owner, out var uiComp) && uiComp.ClientOpenInterfaces.TryGetValue(BsEnergyUiKey.AuthenticationKey, out var authBui))
                authBui.Close();
        });

        _window?.Dispose();
    }
}
