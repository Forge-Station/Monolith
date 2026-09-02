using Content.Shared._Forge.Features;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Features.UI;

[UsedImplicitly]
public sealed class CombinationLockBoundUserInterface : BoundUserInterface
{
    private CombinationLockWindow? _window;

    public CombinationLockBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<CombinationLockWindow>();
        _window.OnKeypadPressed += value => SendMessage(new CombinationLockKeypadMessage(value));
        _window.OnEnterPressed += () => SendMessage(new CombinationLockEnterMessage());
        _window.OnClearPressed += () => SendMessage(new CombinationLockClearMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is CombinationLockUiState uiState)
            _window?.UpdateState(uiState);
    }
}
