using Content.Client.UserInterface.Fragments;
using Content.Shared._WF.GameHub;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.GameHub;

/// <summary>The PDA Gamehub program: a list of games to download, play and compare scores on.</summary>
public sealed partial class WFGameHubUi : UIFragment
{
    private WFGameHubFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new WFGameHubFragment(userInterface.Owner);
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
    }
}

/// <summary>Lists every game prototype with its download state.</summary>
public sealed partial class WFGameHubFragment : BoxContainer
{
    private const float BytesPerMegabyte = 1024f * 1024f;

    [Dependency] private IEntitySystemManager _systems = default!;
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private WFGameDataManager _data = default!;

    private readonly EntityUid _pda;
    private readonly List<Row> _rows = new();

    public WFGameHubFragment(EntityUid pda)
    {
        IoCManager.InjectDependencies(this);
        _pda = pda;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(6);

        var hub = _systems.GetEntitySystem<WFGameHubSystem>();
        foreach (var game in _protos.EnumeratePrototypes<WFGamePrototype>())
        {
            var row = new Row(game, this);
            _rows.Add(row);
            AddChild(row.Root);
            hub.RequestManifest(game.ID);
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        foreach (var row in _rows)
        {
            row.Update(_data);
        }
    }

    private void OnAction(string game)
    {
        var hub = _systems.GetEntitySystem<WFGameHubSystem>();
        switch (_data.GetStatus(game))
        {
            case WFGameDataStatus.Ready:
                hub.Launch(game, _pda);
                break;
            case WFGameDataStatus.Missing:
            case WFGameDataStatus.Failed:
                hub.StartDownload(game);
                break;
        }
    }

    private void OnLeaderboard(string game)
    {
        _systems.GetEntitySystem<WFGameHubSystem>().OpenLeaderboard(game);
    }

    private sealed class Row
    {
        private readonly string _game;
        private readonly Button _action;
        private readonly ProgressBar _progress;
        private readonly Label _status;

        public readonly BoxContainer Root;

        public Row(WFGamePrototype game, WFGameHubFragment owner)
        {
            _game = game.ID;
            _action = new Button { HorizontalExpand = true };
            _action.OnPressed += _ => owner.OnAction(_game);
            _progress = new ProgressBar { MinValue = 0, MaxValue = 1, MinHeight = 16, Visible = false };
            _status = new Label { Visible = false };

            var leaderboard = new Button { Text = Loc.GetString("wf-gamehub-leaderboard") };
            leaderboard.OnPressed += _ => owner.OnLeaderboard(_game);

            Root = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                Margin = new Thickness(0, 0, 0, 10),
                Children =
                {
                    new Label { Text = Loc.GetString(game.Name), StyleClasses = { "LabelHeading" } },
                    new RichTextLabel { Text = Loc.GetString(game.Description) },
                    _progress,
                    _status,
                    new BoxContainer
                    {
                        Orientation = LayoutOrientation.Horizontal,
                        Children = { _action, leaderboard },
                    },
                },
            };
        }

        public void Update(WFGameDataManager data)
        {
            var status = data.GetStatus(_game);
            var (received, total) = data.GetProgress(_game);

            _progress.Visible = status == WFGameDataStatus.Downloading;
            _status.Visible = status is WFGameDataStatus.Downloading or WFGameDataStatus.Failed;
            _action.Disabled = status is WFGameDataStatus.Unknown or WFGameDataStatus.Downloading;

            switch (status)
            {
                case WFGameDataStatus.Unknown:
                    _action.Text = Loc.GetString("wf-gamehub-checking");
                    break;
                case WFGameDataStatus.Missing:
                    _action.Text = Loc.GetString("wf-gamehub-download", ("size", Megabytes(total)));
                    break;
                case WFGameDataStatus.Downloading:
                    _action.Text = Loc.GetString("wf-gamehub-play");
                    _progress.Value = total > 0 ? (float) received / total : 0f;
                    _status.Text = Loc.GetString("wf-gamehub-downloading", ("received", Megabytes(received)), ("total", Megabytes(total)));
                    break;
                case WFGameDataStatus.Ready:
                    _action.Text = Loc.GetString("wf-gamehub-play");
                    break;
                case WFGameDataStatus.Failed:
                    _action.Text = Loc.GetString("wf-gamehub-retry");
                    _status.Text = Loc.GetString("wf-gamehub-download-failed");
                    break;
            }
        }

        private static string Megabytes(long bytes) => (bytes / BytesPerMegabyte).ToString("0.0");
    }
}
