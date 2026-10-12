using Content.Server._WF.ShipPa;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Shared._WF.SectorControl;
using Content.Shared.GameTicking;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.SectorControl.Systems;

/// <summary>Tells those aboard a ship, in chat and over its PA, when it has entered or left a faction's space.</summary>
public sealed partial class WFSectorNoticeSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private ShipPaSystem _shipPa = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;
    [Dependency] private WFSectorActivitySystem _activity = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>How long a ship stays inside a faction's space before it is told.</summary>
    public static readonly TimeSpan EnteredDwell = TimeSpan.FromSeconds(10);

    /// <summary>How long a ship stays outside before it is told it has left.</summary>
    public static readonly TimeSpan LeftDwell = TimeSpan.FromSeconds(30);

    /// <summary>The least time between two entered notices to one ship; a left notice is never held back by it.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    private readonly Dictionary<EntityUid, NoticeState> _ships = new();
    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent args)
    {
        _ships.Clear();
        _nextCheck = TimeSpan.Zero;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_territory.Enabled || _ticker.RunLevel != GameRunLevel.InRound || _timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;
        Check();
    }

    /// <summary>One pass over the crewed ships of the sector map, as the timer runs it.</summary>
    public void Check()
    {
        var now = _timing.CurTime;
        var map = _territory.SectorMap;
        var ships = _activity.CrewedShips(map);
        var gone = new List<EntityUid>();
        foreach (var (grid, tracked) in _ships)
        {
            if (ships.Contains(grid))
            {
                tracked.LastSeen = now;
                continue;
            }

            // Kept through a short absence of the crew, so the cooldown holds.
            if (TerminatingOrDeleted(grid) || now - tracked.LastSeen > Cooldown)
                gone.Add(grid);
        }

        foreach (var grid in gone)
        {
            _ships.Remove(grid);
        }

        foreach (var grid in ships)
        {
            var cell = _territory.CellOf(_transform.GetMapCoordinates(grid));
            var owner = _territory.Owner(map, cell);
            if (owner is { } holder && _activity.IsNeutralTo(holder, grid))
                owner = null;

            if (!_ships.TryGetValue(grid, out var state))
            {
                state = new NoticeState { Since = now, LastSeen = now };
                _ships[grid] = state;
            }

            if (owner != state.Current)
            {
                state.Current = owner;
                state.Since = now;
            }

            var entering = state.Current != null;
            var dwell = entering ? EnteredDwell : LeftDwell;
            if (state.Current == state.Announced || now - state.Since < dwell || (entering && now < state.NextAllowed))
                continue;

            Notify(grid, state.Announced, state.Current, cell);
            state.Announced = state.Current;
            if (entering)
                state.NextAllowed = now + Cooldown;
        }
    }

    private void Notify(EntityUid grid, ProtoId<WFSectorFactionPrototype>? left, ProtoId<WFSectorFactionPrototype>? entered, WFSectorCell cell)
    {
        var shown = entered ?? left;
        if (shown == null || !_prototypes.TryIndex(shown.Value, out var faction))
            return;

        var key = entered != null
            ? faction.EnteredNotice ?? "sector-control-notice-entered"
            : faction.LeftNotice ?? "sector-control-notice-left";
        var message = Loc.GetString(key,
            ("ship", Name(grid)),
            ("faction", Loc.GetString(faction.Name)),
            ("callsign", WFSectorHex.Callsign(cell)));
        Tell(grid, message, Loc.GetString("sector-control-notice-sender"), faction.Color, entered != null);
    }

    /// <summary>Says something over a ship's PA, if it has a working speaker, and in chat to everyone aboard the PA did not reach.</summary>
    public void Tell(EntityUid grid, string message, string sender, Color color, bool playSound)
    {
        var aboard = Filter.Empty();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out _, out var actor, out var xform))
        {
            if (xform.GridUid == grid)
                aboard.AddPlayer(actor.PlayerSession);
        }

        // The PA puts its own line in the chat of those in a speaker's range; the rest aboard get it here, once.
        if (_shipPa.Announce(grid, message, sender: sender, color: color))
            aboard.RemovePlayers(_shipPa.GetListeners(grid).Recipients);

        if (aboard.Count > 0)
            _chat.DispatchFilteredAnnouncement(aboard, message, grid, sender, playSound: playSound, colorOverride: color);
    }

    /// <summary>Whose space a ship is in, since when, and what it was last told.</summary>
    private sealed class NoticeState
    {
        public ProtoId<WFSectorFactionPrototype>? Current;
        public ProtoId<WFSectorFactionPrototype>? Announced;
        public TimeSpan Since;
        public TimeSpan NextAllowed;
        public TimeSpan LastSeen;
    }
}
