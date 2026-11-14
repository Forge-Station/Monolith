using Content.Server._WF.NpcCrew.Components;
using Content.Shared._White.Standing;
using Content.Shared._WF.NpcCrew;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// Crew who never fight, cowards among them, drop their work when their ship is alerted, boarded or one of them is
/// attacked. They run from any stranger who comes near, crying out, and cower on the deck wherever they are left
/// alone; a minute after the last alarm they go back to their posts.
/// </summary>
public sealed partial class WFCrewShelterSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedLayingDownSystem _laying = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private Content.Server.Chat.Systems.ChatSystem _chat = default!;
    [Dependency] private Robust.Shared.Random.IRobustRandom _random = default!;

    /// <summary>The pleas a sheltering crewman cries out, aloud and never over the radio.</summary>
    private const int Cries = 5;
    private readonly Dictionary<EntityUid, TimeSpan> _nextCry = new();

    private static readonly TimeSpan Calm = TimeSpan.FromSeconds(60);
    private readonly Dictionary<(EntityUid Grid, string Group), TimeSpan> _alarms = new();
    private readonly Dictionary<EntityUid, EntityCoordinates?> _posts = new();
    private TimeSpan _nextCheck;

    /// <summary>How near, in tiles, a stranger sends a sheltering crewman running, and how far off is far enough.</summary>
    private const float FleeRange = 7f;
    private const float SafeRange = 9f;

    private static readonly TimeSpan TilesKept = TimeSpan.FromSeconds(10);
    private readonly Dictionary<EntityUid, (TimeSpan Until, List<EntityCoordinates> Tiles)> _tiles = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCrewAlertEvent>(OnAlert);
        SubscribeLocalEvent<WFCrewSecurityIncidentEvent>(OnIncident);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _alarms.Clear();
            _posts.Clear();
            _nextCry.Clear();
            _tiles.Clear();
        });
    }

    private void OnAlert(ref WFCrewAlertEvent args)
    {
        Shelter(args.Grid, args.Group);
    }

    private void OnIncident(ref WFCrewSecurityIncidentEvent args)
    {
        Shelter(args.Grid, args.Group);
    }

    /// <summary>Whether a crewman is sheltering from an alarm.</summary>
    public bool IsSheltering(EntityUid uid) => _posts.ContainsKey(uid);

    /// <summary>Sends a ship's non-combatants to the bridge, or keeps the alarm they shelter from going.</summary>
    public void Shelter(EntityUid grid, string group)
    {
        var sheltered = false;
        var passive = new List<Entity<WFCrewComponent>>();
        var query = EntityQueryEnumerator<WFCrewComponent>();
        while (query.MoveNext(out var uid, out var crew))
        {
            if (crew.Group != group || _crew.HomeGrid(uid, crew) != grid || !_mobs.IsAlive(uid))
                continue;

            if (crew.Engagement == WFCrewEngagement.Never && !_posts.ContainsKey(uid))
                passive.Add((uid, crew));
            else if (crew.Engagement == WFCrewEngagement.Never)
                sheltered = true;
        }

        // An alarm only matters while someone is, or is about to be, sheltering from it.
        if (passive.Count == 0 && !sheltered)
            return;

        _alarms[(grid, group)] = _timing.CurTime + Calm;
        var work = EntityManager.System<WFCrewWorkSystem>();
        foreach (var member in passive)
        {
            _posts[member] = member.Comp.Post;
            work.CancelWorker(member);
            // He gets down where he stands, until somebody comes near enough to run from.
            _crew.SetPost((member, member.Comp), Transform(member).Coordinates);
            _nextCry[member] = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(0.5f, 2f));
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(1);
        foreach (var (key, until) in _alarms)
        {
            if (_timing.CurTime >= until || TerminatingOrDeleted(key.Grid))
                _alarms.Remove(key);
        }

        if (_posts.Count == 0)
            return;

        foreach (var (uid, post) in new Dictionary<EntityUid, EntityCoordinates?>(_posts))
        {
            if (TerminatingOrDeleted(uid) || !TryComp<WFCrewComponent>(uid, out var crew))
            {
                _posts.Remove(uid);
                _nextCry.Remove(uid);
                continue;
            }

            if (_crew.HomeGrid(uid, crew) is not { } grid)
                continue;

            var key = (grid, crew.Group);
            if (_alerts.IsAlerted(grid, crew.Group) || _alarms.TryGetValue(key, out var until) && _timing.CurTime < until)
            {
                if (Threat(uid, grid) is not { } threat || !Flee(uid, crew, grid, threat))
                    Cower(uid, crew);
                Cry(uid);
                continue;
            }

            _alarms.Remove(key);
            _posts.Remove(uid);
            _nextCry.Remove(uid);
            _laying.TryStandUp(uid);
            if (post is not { } original)
                _crew.SetPost((uid, crew), null);
            else if (!TerminatingOrDeleted(original.EntityId))
                _crew.SetPost((uid, crew), original);
        }
    }

    /// <summary>Where the nearest stranger aboard is, in the ship's own frame, if one is near enough to run from.</summary>
    private System.Numerics.Vector2? Threat(EntityUid uid, EntityUid grid)
    {
        var xform = Transform(uid);
        if (xform.GridUid != grid)
            return null;

        var security = EntityManager.System<WFCrewSecuritySystem>();
        var here = _transform.GetWorldPosition(xform);
        System.Numerics.Vector2? nearest = null;
        var best = FleeRange * FleeRange;
        var mobs = EntityQueryEnumerator<Content.Shared.Mobs.Components.MobStateComponent, TransformComponent>();
        while (mobs.MoveNext(out var other, out _, out var there))
        {
            if (other == uid || there.GridUid != grid || _crew.SameCrew(uid, other)
                || !HasComp<ActorComponent>(other) && !HasComp<Content.Shared.Humanoid.HumanoidAppearanceComponent>(other)
                || !security.IsBoardingCandidate(other))
                continue;

            var distance = (_transform.GetWorldPosition(there) - here).LengthSquared();
            if (distance >= best)
                continue;

            best = distance;
            nearest = _transform.ToCoordinates(grid, _transform.GetMapCoordinates(other, there)).Position;
        }

        return nearest;
    }

    /// <summary>
    /// Sends a sheltering crewman running for the tile of his ship farthest from a stranger, by preference one that
    /// doesn't take him past the stranger. False when he has nowhere better to go than where he is.
    /// </summary>
    private bool Flee(EntityUid uid, WFCrewComponent crew, EntityUid grid, System.Numerics.Vector2 threat)
    {
        var here = _transform.ToCoordinates(grid, _transform.GetMapCoordinates(uid)).Position;
        // Already running for somewhere far enough off.
        if (crew.Post is { } going && going.EntityId == grid && (going.Position - threat).Length() >= SafeRange
            && (going.Position - here).Length() > crew.PostRange + 0.5f)
            return true;

        if (!_tiles.TryGetValue(grid, out var known) || _timing.CurTime >= known.Until)
            _tiles[grid] = known = (_timing.CurTime + TilesKept, EntityManager.System<WFCrewPlannerSystem>().HoldTiles(grid, int.MaxValue));

        EntityCoordinates? best = null;
        var bestScore = (here - threat).Length() + 1f;
        var away = here - threat;
        foreach (var tile in known.Tiles)
        {
            var score = (tile.Position - threat).Length();
            // Running towards him to get past is the last thing a frightened man does.
            if (System.Numerics.Vector2.Dot(tile.Position - here, away) < 0f)
                score *= 0.5f;
            if (score <= bestScore)
                continue;

            bestScore = score;
            best = tile;
        }

        if (best is not { } refuge)
            return false;

        _laying.TryStandUp(uid);
        _crew.SetPost((uid, crew), refuge);
        return true;
    }

    /// <summary>A frightened crewman screams and begs aloud every so often while the alarm lasts.</summary>
    private void Cry(EntityUid uid)
    {
        var now = _timing.CurTime;
        if (!_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid) || _nextCry.TryGetValue(uid, out var next) && now < next)
            return;

        _nextCry[uid] = now + TimeSpan.FromSeconds(_random.NextFloat(7f, 14f));
        if (_random.NextFloat() < 0.5f)
            _chat.TryEmoteWithChat(uid, "Scream", Content.Shared.Chat.ChatTransmitRange.HideChat, hideLog: true);
        else
            _chat.TrySendInGameICMessage(uid, Loc.GetString($"wf-crew-coward-cry-{_random.Next(1, Cries + 1)}"),
                Content.Shared.Chat.InGameICChatType.Speak, hideChat: true, hideLog: true, checkRadioPrefix: false);
    }

    /// <summary>Once at the bridge, a sheltering crewman gets down on the deck until the alarm is over.</summary>
    private void Cower(EntityUid uid, WFCrewComponent crew)
    {
        if (crew.Post is not { } shelter || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid) || _standing.IsDown(uid)
            || !_transform.InRange(Transform(uid).Coordinates, shelter, crew.PostRange + 0.5f))
            return;

        _laying.TryLieDown(uid);
    }
}
