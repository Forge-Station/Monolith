using Content.Shared._WF.Minigames;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Minigames.Games;

/// <summary>One game's part of the window: draws its view and turns clicks into moves.</summary>
public abstract class MinigamePanel : BoxContainer
{
    public event Action<MinigameMove>? OnMove;

    protected MinigamePanel()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 6;
        VerticalExpand = true;
    }

    public abstract void UpdateState(MinigameState state);

    protected void Move(int action, int a = 0, int b = 0)
    {
        OnMove?.Invoke(new MinigameMove(action, a, b));
    }

    protected static BoxContainer Row()
    {
        return new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            HorizontalAlignment = HAlignment.Center,
        };
    }

    protected static Button MakeButton(string text, Action pressed)
    {
        var button = new Button { Text = text, MinWidth = 64 };
        button.OnPressed += _ => pressed();
        return button;
    }
}
