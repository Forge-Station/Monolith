using Content.Shared._WF.Minigames;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WF.Minigames.Games;

/// <summary>Riddles: the riddle and four answer buttons.</summary>
public sealed class RiddlePanel : MinigamePanel
{
    private readonly Label _number = new() { HorizontalAlignment = HAlignment.Center };
    private readonly RichTextLabel _question = new() { MinHeight = 60 };
    private readonly List<Button> _answers = new();

    public RiddlePanel()
    {
        AddChild(_number);
        AddChild(_question);
        for (var i = 0; i < RiddleGame.Answers; i++)
        {
            var answer = i;
            var button = MakeButton(string.Empty, () => Move(answer));
            button.HorizontalExpand = true;
            _answers.Add(button);
            AddChild(button);
        }
    }

    public override void UpdateState(MinigameState state)
    {
        if (state.View is not RiddleView view)
            return;

        _number.Text = Loc.GetString("wf-minigame-riddle-number", ("number", view.Number), ("total", view.Total));
        _question.SetMessage(FormattedMessage.FromUnformatted(view.Question));
        for (var i = 0; i < _answers.Count; i++)
        {
            _answers[i].Text = i < view.Answers.Count ? view.Answers[i] : string.Empty;
            _answers[i].Disabled = state.Status != MinigameStatus.Playing;
        }
    }
}
