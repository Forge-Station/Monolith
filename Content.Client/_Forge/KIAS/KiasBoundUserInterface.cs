using Content.Shared._Forge.KIAS;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.KIAS;

[UsedImplicitly]
public sealed class KiasBoundUserInterface : BoundUserInterface
{
    private KiasWindow? _window;

    public KiasBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<KiasWindow>();
        _window.RefreshRequested += () => SendMessage(new KiasRefreshMessage());
        _window.ControlRequested += reset => SendMessage(new KiasControlMessage { Reset = reset });
        _window.AudioChanged += message => SendMessage(message);
        _window.QuietRequested += () => SendMessage(new KiasControlMessage { Quiet = true });
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is KiasManagementState kias)
            _window?.UpdateState(kias);
    }
}
