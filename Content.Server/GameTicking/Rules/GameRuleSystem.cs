using System.Diagnostics.CodeAnalysis;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Shared.GameTicking.Components;
using Content.Shared.Station.Components;
using Robust.Shared.Collections;
using Robust.Shared.Map;

namespace Content.Server.GameTicking.Rules;

/// <summary>
/// Server half of <see cref="Content.Shared.GameTicking.Rules.GameRuleSystem{T}"/>.
/// Keeps the local minimum-player check that cancels a preset or ends the rule.
/// </summary>
public abstract partial class GameRuleSystem<T> : Content.Shared.GameTicking.Rules.GameRuleSystem<T>
    where T : IComponent
{
    [Dependency] protected IChatManager ChatManager = default!;

    /// <summary>
    /// Server ticker. Shared rules only see <see cref="GameTicker"/>.
    /// </summary>
    [Dependency] protected ServerGameTicker ServerGameTicker = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundStartAttemptEvent>(OnStartAttempt);
    }

    private void OnStartAttempt(RoundStartAttemptEvent args)
    {
        if (args.Forced || args.Cancelled)
            return;

        var query = QueryAllRules();
        while (query.MoveNext(out var uid, out _, out var gameRule))
        {
            var minPlayers = gameRule.MinPlayers;
            var name = ToPrettyString(uid);

            if (args.Players.Length >= minPlayers)
                continue;

            if (gameRule.CancelPresetOnTooFewPlayers)
            {
                ChatManager.SendAdminAnnouncement(Loc.GetString("preset-not-enough-ready-players",
                    ("readyPlayersCount", args.Players.Length),
                    ("minimumPlayers", minPlayers),
                    ("presetName", name)));
                args.Cancel();
                Log.Info($"Rule '{name}' requires {minPlayers} players, but only {args.Players.Length} are ready.");
            }
            else
            {
                ForceEndSelf(uid, gameRule);
            }
        }
    }

    /// <summary>
    /// Local helper kept for event rules that still pick a station by uid.
    /// </summary>
    protected bool TryGetRandomStation([NotNullWhen(true)] out EntityUid? station, Func<EntityUid, bool>? filter = null)
    {
        var stations = new ValueList<EntityUid>(Count<StationEventEligibleComponent>());

        filter ??= _ => true;
        var query = AllEntityQuery<StationEventEligibleComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!filter(uid))
                continue;

            stations.Add(uid);
        }

        if (stations.Count == 0)
        {
            station = null;
            return false;
        }

        station = stations[RobustRandom.Next(stations.Count)];
        return true;
    }

    /// <summary>
    /// Mono: pick several event-eligible stations.
    /// </summary>
    protected bool TryGetRandomStations(int min, int max, [NotNullWhen(true)] out List<EntityUid>? station, Func<EntityUid, bool>? filter = null)
    {
        var stations = new ValueList<EntityUid>(Count<StationEventEligibleComponent>());

        filter ??= _ => true;
        var query = AllEntityQuery<StationEventEligibleComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!filter(uid))
                continue;

            stations.Add(uid);
        }

        if (stations.Count == 0)
        {
            station = null;
            return false;
        }

        var numStations = Math.Min(RobustRandom.Next(min, max + 1), stations.Count);
        station = new List<EntityUid>();
        while (numStations > 0)
        {
            var randomStation = stations[RobustRandom.Next(stations.Count)];
            station.Add(randomStation);
            stations.Remove(randomStation);
            numStations--;
        }

        return true;
    }

    protected bool TryFindRandomTile(out Vector2i tile,
        [NotNullWhen(true)] out EntityUid? targetStation,
        out EntityUid targetGrid,
        out EntityCoordinates targetCoords)
    {
        var found = ServerStationSystem.TryFindRandomTile(out tile, out var station, out var grid, out targetCoords);
        targetStation = station?.Owner;
        targetGrid = grid?.Owner ?? EntityUid.Invalid;
        return found && targetStation != null;
    }

    [Dependency] private ServerStationSystem ServerStationSystem = default!;
}
