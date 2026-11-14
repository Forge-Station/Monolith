using System.Linq;
using Content.Server.GameTicking;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFRoundSetupSystem
{
    /// <summary>How long an in-round viewer keeps live snapshots after its last request.</summary>
    public static readonly TimeSpan WatchLease = TimeSpan.FromSeconds(60);

    /// <summary>Catches slow drift the change checks miss, such as job slots and playtime unlocks.</summary>
    private static readonly TimeSpan SafetyBroadcast = TimeSpan.FromSeconds(30);

    private readonly Dictionary<NetUserId, TimeSpan> _watchUntil = new();
    private readonly Dictionary<NetUserId, ICharacterProfile?> _portraitSources = new();
    private TimeSpan _nextSafetyBroadcast;

    private void InitializeBroadcast()
    {
        SubscribeLocalEvent<GameRunLevelChangedEvent>(_ => Broadcast());
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    /// <inheritdoc/>
    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    // Online flags, invite targets and request lists all depend on who is connected.
    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args) => Broadcast();

    /// <summary>Registers a viewer and extends its in-round lease.</summary>
    private void Watch(NetUserId user)
    {
        _watchers.Add(user);
        _watchUntil[user] = _timing.CurTime + WatchLease;
    }

    /// <summary>Queues a snapshot only when lobby state could have changed without a request.</summary>
    private void BroadcastIfChanged(bool pruned)
    {
        var now = _timing.CurTime;
        if (pruned || PortraitsChanged() || now >= _nextSafetyBroadcast)
            Broadcast();
    }

    /// <summary>Detects character edits by the preference manager replacing the selected profile.</summary>
    private bool PortraitsChanged()
    {
        var changed = false;
        foreach (var lobby in _lobbies.Values)
        foreach (var (user, member) in lobby.Members)
        {
            if (member.Spawned)
                continue;
            var current = _preferences.GetPreferencesOrNull(user)?.SelectedCharacter;
            if (!_portraitSources.TryGetValue(user, out var known) || !ReferenceEquals(known, current))
                changed = true;
        }
        return changed;
    }

    /// <summary>Lobby-screen viewers, crew leaders and invitees always follow; others only while they look.</summary>
    private bool WantsBroadcast(ICommonSession session)
    {
        var user = session.UserId;
        if (!_ticker.UserHasJoinedGame(session) || FindLobby(user)?.Host == user ||
            _lobbies.Values.Any(lobby => lobby.Invitations.Contains(user)))
            return true;
        return _watchUntil.TryGetValue(user, out var until) && until > _timing.CurTime;
    }

    /// <summary>Delivers queued snapshots to interested viewers and forgets disconnected ones.</summary>
    private void FlushBroadcast()
    {
        _nextSafetyBroadcast = _timing.CurTime + SafetyBroadcast;
        RefreshPortraits();
        _portraitSources.Clear();
        foreach (var lobby in _lobbies.Values)
        foreach (var (user, member) in lobby.Members)
        {
            _portraitSources[user] = _preferences.GetPreferencesOrNull(user)?.SelectedCharacter;
            // This snapshot carries the status, so the next poll must not resend it.
            _knownStatuses[user] = MemberStatus(member);
        }
        foreach (var user in _watchers.ToArray())
        {
            if (_players.TryGetSessionById(user, out var session))
            {
                if (WantsBroadcast(session))
                    SendState(session, refreshPortraits: false);
                continue;
            }
            _watchers.Remove(user);
            _watchUntil.Remove(user);
            _nextRequest.Remove(user);
            _nextCodeAttempt.Remove(user);
        }
    }
}
