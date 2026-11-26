using Content.Server.Players.PlayTimeTracking;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Newbie;
using Content.Shared.Ghost;
using Content.Shared.Players.PlayTimeTracking;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Newbie;

/// <summary>
/// Tags the bodies and ships of players under the newbie playtime, unless they turned their tag off.
/// </summary>
public sealed partial class WFNewbieTagSystem : EntitySystem
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private INetConfigurationManager _netCfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private PlayTimeTrackingManager _playTime = default!;

    private TimeSpan _nextRefresh;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnDetached);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextRefresh)
            return;

        _nextRefresh = _timing.CurTime + RefreshInterval;
        RefreshAll();
    }

    /// <summary>
    /// Brings every attached body and owned ship in line with its player's tag.
    /// </summary>
    public void RefreshAll()
    {
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is { } body)
                SetTag(body, IsTagged(session));
        }

        var ships = EntityQueryEnumerator<ShipOwnershipComponent>();
        while (ships.MoveNext(out var ship, out var owner))
        {
            // An offline owner keeps whatever tag their ship last had.
            if (_players.TryGetSessionById(owner.OwnerUserId, out var session))
                SetShipTag(ship, IsTagged(session));
        }
    }

    /// <summary>
    /// Whether a player is under the newbie playtime and shows their tag.
    /// </summary>
    public bool IsTagged(ICommonSession session)
    {
        var hours = _cfg.GetCVar(WFNewbieCVars.Hours);
        if (hours <= 0 || session.Status != SessionStatus.InGame)
            return false;

        // Playtime loads from the database after connecting; until then nobody is tagged.
        if (!TryGetPlaytime(session, out var playtime) || playtime >= TimeSpan.FromHours(hours))
            return false;

        return _netCfg.GetClientCVar(session.Channel, WFNewbieCVars.ShowTag);
    }

    /// <summary>
    /// A player's total playtime, once it has loaded from the database.
    /// </summary>
    public bool TryGetPlaytime(ICommonSession session, out TimeSpan playtime)
    {
        playtime = TimeSpan.Zero;
        if (!_playTime.TryGetTrackerTimes(session, out var times))
            return false;

        // Stored times lag behind the session until flushed, which would keep a player tagged past the threshold.
        _playTime.FlushTracker(session);
        playtime = times.GetValueOrDefault(PlayTimeTrackingShared.TrackerOverall);
        return true;
    }

    private void OnAttached(PlayerAttachedEvent args)
    {
        SetTag(args.Entity, IsTagged(args.Player));
    }

    private void OnDetached(PlayerDetachedEvent args)
    {
        SetTag(args.Entity, false);
    }

    private void SetTag(EntityUid body, bool tagged)
    {
        // Ghosts are only seen by other ghosts, who have no use for the mark.
        if (tagged && !HasComp<GhostComponent>(body))
            EnsureComp<WFNewbieComponent>(body);
        else
            RemComp<WFNewbieComponent>(body);
    }

    private void SetShipTag(EntityUid ship, bool tagged)
    {
        if (tagged)
            EnsureComp<WFNewbieShipComponent>(ship);
        else
            RemComp<WFNewbieShipComponent>(ship);
    }
}
