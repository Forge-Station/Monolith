using System.Text;
using Content.Shared._WF.Minigames;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Minigames.Games;

/// <summary>Ore Piles: the pile drawn as ore chunks and a button per take.</summary>
public sealed class OrePilesPanel : MinigamePanel
{
    private readonly Label _pile = new() { HorizontalAlignment = HAlignment.Center };
    private readonly Label _chunks = new() { HorizontalAlignment = HAlignment.Center, FontColorOverride = Color.FromHex("#C9A26B") };
    private readonly List<Button> _takes = new();

    public OrePilesPanel()
    {
        AddChild(new Label { Text = Loc.GetString("wf-minigame-ore-rules"), HorizontalAlignment = HAlignment.Center });
        AddChild(_pile);
        AddChild(_chunks);

        var row = Row();
        for (var i = 1; i <= MinigameRules.OrePilesMaxTake; i++)
        {
            var take = i;
            var button = MakeButton(Loc.GetString("wf-minigame-ore-take", ("count", take)), () => Move(take));
            _takes.Add(button);
            row.AddChild(button);
        }

        AddChild(row);
    }

    public override void UpdateState(MinigameState state)
    {
        if (state.View is not OrePilesView view)
            return;

        _pile.Text = Loc.GetString("wf-minigame-ore-pile", ("pile", view.Pile));
        var chunks = new StringBuilder();
        for (var i = 0; i < view.Pile; i++)
            chunks.Append(i % 10 == 9 ? "o\n" : "o ");

        _chunks.Text = chunks.ToString();
        for (var i = 0; i < _takes.Count; i++)
            _takes[i].Disabled = state.Status != MinigameStatus.Playing || i + 1 > view.MaxTake;
    }
}
