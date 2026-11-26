using Content.Shared._WF.Newbie;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Newbie;

/// <summary>
/// Sends each new player the welcome popups once per join, as soon as their playtime has loaded.
/// </summary>
public sealed partial class WFNewbieWelcomeSystem : EntitySystem
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private WFNewbieTagSystem _tags = default!;

    /// <summary>Players already checked since they connected.</summary>
    private readonly HashSet<NetUserId> _greeted = new();

    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;
        GreetAll();
    }

    /// <summary>
    /// Greets every in-game player not yet checked this join whose playtime has loaded.
    /// </summary>
    public void GreetAll()
    {
        if (!_cfg.GetCVar(WFNewbieCVars.Welcome))
            return;

        var hours = _cfg.GetCVar(WFNewbieCVars.Hours);
        var fresh = TimeSpan.FromHours(_cfg.GetCVar(WFNewbieCVars.FreshHours));

        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame ||
                _greeted.Contains(session.UserId) ||
                !_tags.TryGetPlaytime(session, out var playtime))
            {
                continue;
            }

            _greeted.Add(session.UserId);

            if (hours > 0 && playtime < TimeSpan.FromHours(hours))
                RaiseNetworkEvent(new WFNewbieWelcomeEvent(playtime < fresh), session);
        }
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
            _greeted.Remove(args.Session.UserId);
    }
}
