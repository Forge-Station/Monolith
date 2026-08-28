using Content.Shared._EE.Contractors;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._EinsteinEngines.Contractors.UI;

[UsedImplicitly]
public sealed class PassportBoundUserInterface : BoundUserInterface
{
    private PassportWindow? _window;

    public PassportBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<PassportWindow>();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is PassportBoundUserInterfaceState passportState)
            _window?.UpdateState(passportState);
    }
}
