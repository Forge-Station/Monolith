using System.Numerics;
using Content.Client._WF.Minigames.Games;
using Content.Client.Administration.UI.CustomControls;
using Content.Shared._NF.Bank;
using Content.Shared._WF.Minigames;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._WF.Minigames;

/// <summary>The window every hosted game shares: stakes and the last thing said above, the game's own panel below.</summary>
public sealed class MinigameWindow : DefaultWindow
{
    private readonly Label _header = new();
    private readonly Label _stakes = new();
    private readonly RichTextLabel _line = new() { MinHeight = 40 };
    private readonly Label _result = new() { HorizontalAlignment = HAlignment.Center };
    private readonly BoxContainer _panelSlot = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true };

    private MinigamePanel? _panel;
    private MinigameKind? _kind;

    public event Action<MinigameMove>? OnMove;

    public MinigameWindow()
    {
        Title = Loc.GetString("wf-minigame-window-title");
        MinSize = new Vector2(440, 380);
        SetSize = new Vector2(480, 440);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        _header.StyleClasses.Add("LabelHeading");
        root.AddChild(_header);
        root.AddChild(_stakes);
        root.AddChild(_line);
        root.AddChild(new HSeparator());
        root.AddChild(_panelSlot);
        root.AddChild(_result);
        Contents.AddChild(root);
    }

    public void UpdateState(MinigameState state)
    {
        _header.Text = Loc.GetString("wf-minigame-window-header",
            ("game", Loc.GetString($"wf-minigame-name-{state.Kind.ToString().ToLowerInvariant()}")),
            ("difficulty", Loc.GetString($"wf-minigame-difficulty-{state.Difficulty.ToString().ToLowerInvariant()}")));
        _stakes.Text = Loc.GetString("wf-minigame-window-stakes", ("stake", BankSystemExtensions.ToSpesoString(state.Stake)), ("prize", state.Prize));
        _line.SetMessage(FormattedMessage.FromUnformatted(state.Line));
        _result.Text = state.Status switch
        {
            MinigameStatus.Won => Loc.GetString("wf-minigame-window-won"),
            MinigameStatus.Lost => Loc.GetString("wf-minigame-window-lost"),
            MinigameStatus.NpcTurn => Loc.GetString("wf-minigame-window-thinking"),
            _ => string.Empty,
        };

        if (_kind != state.Kind || _panel == null)
        {
            _panelSlot.RemoveAllChildren();
            _panel = MakePanel(state.Kind);
            _panel.OnMove += move => OnMove?.Invoke(move);
            _panelSlot.AddChild(_panel);
            _kind = state.Kind;
        }

        _panel.UpdateState(state);
    }

    private static MinigamePanel MakePanel(MinigameKind kind)
    {
        return kind switch
        {
            MinigameKind.OrePiles => new OrePilesPanel(),
            MinigameKind.LiarsDice => new LiarsDicePanel(),
            MinigameKind.Lockbox => new LockboxPanel(),
            MinigameKind.HigherLower => new HigherLowerPanel(),
            MinigameKind.Memory => new MemoryPanel(),
            _ => new RiddlePanel(),
        };
    }
}
