using System.Numerics;
using Content.Shared._WF.GameHub;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.GameHub;

/// <summary>Shows the top players of a game, from a cabinet or the PDA Gamehub.</summary>
public sealed partial class WFGameLeaderboardWindow : DefaultWindow
{
    [Dependency] private IEntitySystemManager _systems = default!;
    [Dependency] private IPrototypeManager _protos = default!;

    private readonly string _game;
    private readonly BoxContainer _rows;

    public WFGameLeaderboardWindow(string game)
    {
        IoCManager.InjectDependencies(this);
        _game = game;

        var name = _protos.TryIndex<WFGamePrototype>(game, out var proto) ? Loc.GetString(proto.Name) : game;
        Title = Loc.GetString("wf-gamehub-leaderboard-title", ("game", name));
        SetSize = new Vector2(420, 360);

        _rows = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        Contents.AddChild(new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, Children = { _rows } });

        var hub = _systems.GetEntitySystem<WFGameHubSystem>();
        hub.LeaderboardReceived += OnLeaderboard;
        OnClose += () => hub.LeaderboardReceived -= OnLeaderboard;
        hub.RequestLeaderboard(game);
    }

    private void OnLeaderboard(WFGameLeaderboardEvent ev)
    {
        if (ev.Game != _game)
            return;

        _rows.RemoveAllChildren();

        if (ev.Entries.Count == 0)
        {
            _rows.AddChild(new Label { Text = Loc.GetString("wf-gamehub-leaderboard-empty") });
            return;
        }

        for (var i = 0; i < ev.Entries.Count; i++)
        {
            var entry = ev.Entries[i];
            _rows.AddChild(new Label
            {
                Text = Loc.GetString("wf-gamehub-leaderboard-entry",
                    ("rank", i + 1),
                    ("name", entry.Name),
                    ("score", entry.Score),
                    ("levels", entry.Levels)),
            });
        }
    }
}
