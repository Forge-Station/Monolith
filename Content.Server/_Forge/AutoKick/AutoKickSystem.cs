using System.Linq;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Shared._Forge;
using Content.Shared.Ghost;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Input;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Forge.AutoKick;

/// <summary>
/// Forge-Change: disconnects latejoin players that never clock in and inactive players by input timeout.
/// </summary>
public sealed class AutoKickSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IServerNetManager _net = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    private readonly Dictionary<NetUserId, TimeSpan> _pendingClockInSinceReal = new();
    private readonly Dictionary<NetUserId, TimeSpan> _lastInputTimes = new();
    private readonly HashSet<NetUserId> _disconnectIssued = new();

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private TimeSpan _nextCheckReal;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<FullInputCmdMessage>(OnInput);
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    private void OnInput(FullInputCmdMessage message, EntitySessionEventArgs args)
    {
        _disconnectIssued.Remove(args.SenderSession.UserId);
        _lastInputTimes[args.SenderSession.UserId] = _timing.RealTime;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus is SessionStatus.Connected or SessionStatus.InGame)
        {
            // Forge-Change: new session for this user should be eligible for autokick checks again.
            _disconnectIssued.Remove(args.Session.UserId);
            _pendingClockInSinceReal.Remove(args.Session.UserId);
            _lastInputTimes[args.Session.UserId] = _timing.RealTime;
        }

        if (args.NewStatus == SessionStatus.Disconnected)
        {
            ClearTracked(args.Session.UserId);
            _disconnectIssued.Remove(args.Session.UserId);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.RealTime < _nextCheckReal)
            return;

        _nextCheckReal = _timing.RealTime + CheckInterval;

        var runLevel = _gameTicker.RunLevel;
        var inactivityEnabled = runLevel is GameRunLevel.InRound or GameRunLevel.PreRoundLobby;
        var pendingClockInEnabled = runLevel == GameRunLevel.InRound;

        if (!inactivityEnabled && !pendingClockInEnabled)
        {
            _pendingClockInSinceReal.Clear();
            _lastInputTimes.Clear();
            _disconnectIssued.Clear();
            return;
        }

        var now = _timing.RealTime;
        var pendingKickDelay = TimeSpan.FromMinutes(_cfg.GetCVar(ForgeVars.AutoKickPendingClockInMinutes));
        var inactivityKickDelay = TimeSpan.FromMinutes(_cfg.GetCVar(ForgeVars.AutoKickGuestAfkMinutes));
        var activeUsers = new HashSet<NetUserId>();

        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame)
                continue;

            activeUsers.Add(session.UserId);

            // Forge-Change: avoid issuing repeated disconnects for the same session.
            if (_disconnectIssued.Contains(session.UserId))
                continue;

            var attached = session.AttachedEntity;

            if (pendingClockInEnabled && attached is not null && HasComp<PendingClockInComponent>(attached.Value))
            {
                if (!_pendingClockInSinceReal.TryGetValue(session.UserId, out var since))
                {
                    _pendingClockInSinceReal[session.UserId] = now;
                }
                else if (now - since >= pendingKickDelay)
                {
                    _disconnectIssued.Add(session.UserId);
                    _net.DisconnectChannel(session.Channel, Loc.GetString("forge-autokick-reason-pending-clockin"));
                    ClearTracked(session.UserId);
                    continue;
                }
            }
            else
            {
                _pendingClockInSinceReal.Remove(session.UserId);
            }

            if (!inactivityEnabled)
            {
                _lastInputTimes.Remove(session.UserId);
                continue;
            }

            if (attached is not null && !IsAfkKickCandidate(attached.Value))
            {
                _lastInputTimes.Remove(session.UserId);
                continue;
            }

            if (!_lastInputTimes.TryGetValue(session.UserId, out var lastInput))
                _lastInputTimes[session.UserId] = lastInput = now;

            if (now - lastInput >= inactivityKickDelay)
            {
                _disconnectIssued.Add(session.UserId);
                _net.DisconnectChannel(session.Channel, Loc.GetString("forge-autokick-reason-guest-afk"));
                ClearTracked(session.UserId);
            }
        }

        CleanupInactive(activeUsers);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    private bool IsAfkKickCandidate(EntityUid attached)
    {
        if (HasComp<GhostComponent>(attached))
            return false;

        return true;
    }

    private void ClearTracked(NetUserId userId)
    {
        _pendingClockInSinceReal.Remove(userId);
        _lastInputTimes.Remove(userId);
    }

    private void CleanupInactive(HashSet<NetUserId> activeUsers)
    {
        foreach (var userId in _pendingClockInSinceReal.Keys.ToArray())
        {
            if (!activeUsers.Contains(userId))
                _pendingClockInSinceReal.Remove(userId);
        }

        foreach (var userId in _lastInputTimes.Keys.ToArray())
        {
            if (!activeUsers.Contains(userId))
                _lastInputTimes.Remove(userId);
        }

        foreach (var userId in _disconnectIssued.ToArray())
        {
            if (!activeUsers.Contains(userId))
                _disconnectIssued.Remove(userId);
        }
    }
}
