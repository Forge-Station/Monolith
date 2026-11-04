using System.Text;
using Content.Shared._WF.Minigames;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Minigames.Games;

/// <summary>Liar's Dice: your dice, the standing bid, a bid picker and the call button.</summary>
public sealed class LiarsDicePanel : MinigamePanel
{
    private readonly Label _round = new() { HorizontalAlignment = HAlignment.Center };
    private readonly Label _yours = new() { HorizontalAlignment = HAlignment.Center };
    private readonly Label _theirs = new() { HorizontalAlignment = HAlignment.Center };
    private readonly Label _bid = new() { HorizontalAlignment = HAlignment.Center };
    private readonly Label _pick = new() { HorizontalAlignment = HAlignment.Center, MinWidth = 120 };
    private readonly Button _less;
    private readonly Button _more;
    private readonly List<Button> _faces = new();
    private readonly Button _bidButton;
    private readonly Button _call;

    private int _quantity = 1;
    private int _face = 1;
    private int _total = MinigameRules.LiarsDiceDice * 2;

    public LiarsDicePanel()
    {
        AddChild(new Label { Text = Loc.GetString("wf-minigame-dice-rules"), HorizontalAlignment = HAlignment.Center });
        AddChild(_round);
        AddChild(_yours);
        AddChild(_theirs);
        AddChild(_bid);

        var quantity = Row();
        _less = MakeButton("-", () => SetPick(_quantity - 1, _face));
        _more = MakeButton("+", () => SetPick(_quantity + 1, _face));
        quantity.AddChild(_less);
        quantity.AddChild(_pick);
        quantity.AddChild(_more);
        AddChild(quantity);

        var faces = Row();
        for (var face = 1; face <= 6; face++)
        {
            var f = face;
            var button = MakeButton(face.ToString(), () => SetPick(_quantity, f));
            button.MinWidth = 40;
            button.ToggleMode = true;
            _faces.Add(button);
            faces.AddChild(button);
        }

        AddChild(faces);

        var actions = Row();
        _bidButton = MakeButton(Loc.GetString("wf-minigame-dice-bid"), () => Move(LiarsDiceGame.Bid, _quantity, _face));
        _call = MakeButton(Loc.GetString("wf-minigame-dice-call"), () => Move(LiarsDiceGame.Call));
        actions.AddChild(_bidButton);
        actions.AddChild(_call);
        AddChild(actions);
        SetPick(1, 1);
    }

    private void SetPick(int quantity, int face)
    {
        _quantity = Math.Clamp(quantity, 1, _total);
        _face = face;
        _pick.Text = Loc.GetString("wf-minigame-dice-pick", ("count", _quantity), ("face", _face));
        for (var i = 0; i < _faces.Count; i++)
            _faces[i].Pressed = i + 1 == _face;
    }

    private static string Dice(List<int> dice)
    {
        var text = new StringBuilder();
        foreach (var die in dice)
            text.Append('[').Append(die).Append("] ");

        return text.ToString();
    }

    public override void UpdateState(MinigameState state)
    {
        if (state.View is not LiarsDiceView view)
            return;

        _total = view.PlayerDice.Count + view.NpcDiceCount;
        _round.Text = Loc.GetString("wf-minigame-dice-round", ("round", view.Round), ("rounds", view.RoundsToWin));
        _yours.Text = Loc.GetString("wf-minigame-dice-yours", ("dice", Dice(view.PlayerDice)));
        _theirs.Text = view.NpcDice != null
            ? Loc.GetString("wf-minigame-dice-theirs-shown", ("dice", Dice(view.NpcDice)))
            : Loc.GetString("wf-minigame-dice-theirs", ("count", view.NpcDiceCount));
        _bid.Text = view.BidQuantity == 0
            ? Loc.GetString("wf-minigame-dice-no-bid")
            : Loc.GetString(view.BidByPlayer ? "wf-minigame-dice-your-bid" : "wf-minigame-dice-their-bid",
                ("count", view.BidQuantity), ("face", view.BidFace));

        // Start the picker on the smallest legal raise.
        if (view.BidQuantity > 0 && (_quantity < view.BidQuantity || _quantity == view.BidQuantity && _face <= view.BidFace))
            SetPick(view.BidFace < 6 ? view.BidQuantity : view.BidQuantity + 1, view.BidFace < 6 ? view.BidFace + 1 : 1);
        else if (view.BidQuantity == 0)
            SetPick(1, _face);

        var playing = state.Status == MinigameStatus.Playing;
        _bidButton.Disabled = !playing;
        _less.Disabled = !playing;
        _more.Disabled = !playing;
        _call.Disabled = !playing || view.BidQuantity == 0 || view.BidByPlayer;
        foreach (var face in _faces)
            face.Disabled = !playing;
    }
}
