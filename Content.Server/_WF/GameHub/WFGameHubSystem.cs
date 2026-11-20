using Content.Server.CartridgeLoader;
using Content.Server.Database;
using Content.Server.Popups;
using Content.Shared._WF.GameHub;
using Content.Shared.CartridgeLoader;
using Content.Shared.GameTicking;
using Content.Shared.PDA;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.GameHub;

/// <summary>Passes a game's sound on to nearby players, keeps its leaderboard and pays cabinet prizes.</summary>
public sealed partial class WFGameHubSystem : EntitySystem
{
    private const int LeaderboardSize = 10;

    [Dependency] private CartridgeLoaderSystem _cartridges = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private ITaskManager _task = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private TransformSystem _transform = default!;

    private readonly Dictionary<NetUserId, (TimeSpan Second, int Count)> _noise = new();
    private readonly Dictionary<NetUserId, TimeSpan> _lastLevel = new();
    private readonly Dictionary<NetUserId, HashSet<(string Game, int Level)>> _prized = new();
    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFGameHubInstallerComponent, MapInitEvent>(OnInstallerMapInit);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        SubscribeNetworkEvent<WFGameBroadcastEvent>(OnBroadcast);
        SubscribeNetworkEvent<WFGameNoiseEvent>(OnNoise);
        SubscribeNetworkEvent<WFGameLevelCompleteEvent>((ev, args) => CompleteLevel(ev, args.SenderSession));
        SubscribeNetworkEvent<WFGameLeaderboardRequestEvent>(OnLeaderboardRequest);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(1);

        var query = EntityQueryEnumerator<WFGameBroadcastComponent>();
        while (query.MoveNext(out var uid, out var broadcast))
        {
            // A broadcast with no player, such as an attract mode, isn't tied to anyone's reach.
            if (broadcast.Player is not { } player)
                continue;

            if (!_players.TryGetSessionById(player, out var session) || !CanPlayOn(session, uid))
                RemCompDeferred<WFGameBroadcastComponent>(uid);
        }
    }

    /// <summary>Score for one finished level, from what the client reported. Full marks and a fast clear reach 2,800.</summary>
    public static int ScoreLevel(int killPercent, int itemPercent, int secretPercent, int seconds)
    {
        var percents = Math.Clamp(killPercent, 0, 100) + Math.Clamp(itemPercent, 0, 100) + Math.Clamp(secretPercent, 0, 100);
        return 1000 + percents * 5 + Math.Clamp(300 - seconds, 0, 300);
    }

    /// <summary>Records a finished level and pays any prize. Returns false if the report is refused.</summary>
    public bool CompleteLevel(WFGameLevelCompleteEvent ev, ICommonSession session)
    {
        if (!_protos.TryIndex<WFGamePrototype>(ev.Game, out var game) ||
            ev.Level < game.MinLevel || ev.Level > game.MaxLevel ||
            !TryGetEntity(ev.Source, out var source) ||
            !CanPlayOn(session, source.Value))
        {
            return false;
        }

        var minimum = _cfg.GetCVar(WFGameHubCVars.MinLevelSeconds);
        var now = _timing.CurTime;
        if (ev.Seconds < minimum ||
            _lastLevel.TryGetValue(session.UserId, out var last) && (now - last).TotalSeconds < minimum)
        {
            return false;
        }

        _lastLevel[session.UserId] = now;

        var score = ScoreLevel(ev.KillPercent, ev.ItemPercent, ev.SecretPercent, ev.Seconds);
        _ = _db.SaveGameLevelScoreAsync(session.UserId, session.Name, game.ID, ev.Level, score);

        if (TryComp<WFGameCabinetComponent>(source, out var cabinet) && cabinet.Game == game.ID)
            TryPayPrize(session, source.Value, cabinet, game.ID, ev.Level, score);

        return true;
    }

    private void TryPayPrize(ICommonSession session, EntityUid uid, WFGameCabinetComponent cabinet, string game, int level, int score)
    {
        if (!_prized.TryGetValue(session.UserId, out var prized))
            _prized[session.UserId] = prized = new HashSet<(string, int)>();

        if (prized.Count >= _cfg.GetCVar(WFGameHubCVars.MaxPrizesPerRound) || !prized.Add((game, level)))
            return;

        Spawn(score >= cabinet.HighPrizeScore ? cabinet.HighPrize : cabinet.LowPrize, Transform(uid).Coordinates);
        _popup.PopupEntity(Loc.GetString("wf-gamehub-prize"), uid);
    }

    private void OnInstallerMapInit(Entity<WFGameHubInstallerComponent> ent, ref MapInitEvent args)
    {
        if (TryComp<CartridgeLoaderComponent>(ent, out var loader))
            _cartridges.InstallProgram(ent, ent.Comp.Program, deinstallable: false, loader: loader);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _prized.Clear();
        _lastLevel.Clear();
        _noise.Clear();
    }

    private void OnBroadcast(WFGameBroadcastEvent ev, EntitySessionEventArgs args)
    {
        if (!TryGetEntity(ev.Source, out var source) || !_protos.HasIndex<WFGamePrototype>(ev.Game))
            return;

        var session = args.SenderSession;

        if (ev.Stopped)
        {
            if (TryComp<WFGameBroadcastComponent>(source, out var playing) && playing.Player == session.UserId)
                RemComp<WFGameBroadcastComponent>(source.Value);
            return;
        }

        if (!CanPlayOn(session, source.Value))
            return;

        var broadcast = EnsureComp<WFGameBroadcastComponent>(source.Value);
        broadcast.Game = ev.Game;
        broadcast.Track = ev.Track;
        broadcast.Loop = ev.Loop;
        broadcast.Player = session.UserId;
        Dirty(source.Value, broadcast);
    }

    private void OnNoise(WFGameNoiseEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        if (ev.Sound.Length > WFGameHubProtocol.MaxNoiseName ||
            !_protos.HasIndex<WFGamePrototype>(ev.Game) ||
            !TryGetEntity(ev.Source, out var source) ||
            !CanPlayOn(session, source.Value) ||
            !AllowNoise(session.UserId))
        {
            return;
        }

        var filter = Filter.Pvs(source.Value).RemovePlayer(session);
        RaiseNetworkEvent(new WFGameNoiseHeardEvent(ev.Source, ev.Game, ev.Sound, ev.Volume), filter);
    }

    private bool AllowNoise(NetUserId player)
    {
        var second = TimeSpan.FromSeconds(Math.Floor(_timing.CurTime.TotalSeconds));
        _noise.TryGetValue(player, out var count);
        count = count.Second == second ? (second, count.Count + 1) : (second, 1);
        _noise[player] = count;
        return count.Count <= _cfg.GetCVar(WFGameHubCVars.MaxNoisePerSecond);
    }

    private async void OnLeaderboardRequest(WFGameLeaderboardRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!_protos.HasIndex<WFGamePrototype>(ev.Game))
            return;

        var channel = args.SenderSession.Channel;
        try
        {
            var entries = await _db.GetGameLeaderboardAsync(ev.Game, LeaderboardSize);
            _task.RunOnMainThread(() =>
            {
                if (channel.IsConnected)
                    RaiseNetworkEvent(new WFGameLeaderboardEvent(ev.Game, entries), channel);
            });
        }
        catch (Exception e)
        {
            Log.Error($"Can't read the leaderboard for game {ev.Game}: {e}");
        }
    }

    /// <summary>The source must be a cabinet or PDA within reach of the player's body.</summary>
    private bool CanPlayOn(ICommonSession session, EntityUid source)
    {
        if (session.AttachedEntity is not { } body || !HasComp<WFGameCabinetComponent>(source) && !HasComp<PdaComponent>(source))
            return false;

        var bodyPosition = _transform.GetMapCoordinates(body);
        var sourcePosition = _transform.GetMapCoordinates(source);
        return bodyPosition.InRange(sourcePosition, WFGameHubProtocol.PlayRange);
    }
}
