using Content.Shared._WF.Minigames;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Minigames;

/// <summary>Opens the game window and sends its moves; the server decides everything.</summary>
public sealed class MinigameBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private MinigameWindow? _window;

    public MinigameBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<MinigameWindow>();
        _window.OnMove += move => SendMessage(new MinigameMoveMessage(move));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is MinigameState game)
            _window?.UpdateState(game);
    }
}
