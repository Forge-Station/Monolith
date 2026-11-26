using System.Linq;
using System.Numerics;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Monolith;
using Content.Shared._WF.Remade;
using Content.Shared.Buckle.Components;
using Content.Shared.Examine;
using Content.Shared.Humanoid;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Betrayal;

/// <summary>
/// The dead near a stone from Growth shift a little or turn over, and never while anyone is looking. A body moves only when no
/// living player has a clear line to it. Slow, capped and cheap: a sweep every few seconds, a few bodies a sweep, a few moves
/// each, and a rest between them.
/// </summary>
public sealed partial class WFRestlessCorpseSystem : EntitySystem
{
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;

    public const float StoneRange = 40f;
    public const float SightRange = 30f;
    public const int MovesPerPass = 2;
    public const int MovesPerCorpse = 6;
    public const int Candidates = 24;
    public const float RestMin = 25f;
    public const float RestMax = 70f;

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(4);

    private readonly List<MapCoordinates> _stones = new();
    private readonly List<(EntityUid Uid, float Distance)> _corpses = new();
    private readonly List<EntityUid> _watchers = new();
    private TimeSpan _next;

    /// <summary>Moves made so far. Tests read it.</summary>
    public int Moved { get; private set; }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Tick;
        Pass();
    }

    /// <summary>One sweep: finds the dead near a stone and moves the few that nobody can see. <paramref name="force"/> ignores their rest.</summary>
    public int Pass(bool force = false)
    {
        var now = _timing.CurTime;
        _stones.Clear();
        var monoliths = EntityQueryEnumerator<WFMonolithComponent, TransformComponent>();
        while (monoliths.MoveNext(out var uid, out var monolith, out var xform))
        {
            if (monolith.Stage >= WFMonolithStage.Growth && xform.MapID != MapId.Nullspace)
                _stones.Add(_transform.GetMapCoordinates(uid, xform));
        }

        if (_stones.Count == 0)
            return 0;

        _corpses.Clear();
        var bodies = EntityQueryEnumerator<HumanoidAppearanceComponent, MobStateComponent, TransformComponent>();
        while (bodies.MoveNext(out var uid, out _, out var state, out var xform))
        {
            if (state.CurrentState != MobState.Dead || xform.MapID == MapId.Nullspace || !Free(uid, xform))
                continue;

            var here = _transform.GetWorldPosition(xform);
            var nearest = float.MaxValue;
            foreach (var stone in _stones)
            {
                if (stone.MapId == xform.MapID)
                    nearest = Math.Min(nearest, (stone.Position - here).Length());
            }

            if (nearest <= StoneRange)
                _corpses.Add((uid, nearest));
        }

        if (_corpses.Count == 0)
            return 0;

        _corpses.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        if (_corpses.Count > Candidates)
            _corpses.RemoveRange(Candidates, _corpses.Count - Candidates);

        Gather();
        var moved = 0;
        foreach (var (corpse, _) in _corpses.OrderBy(_ => _random.Next()))
        {
            if (moved >= MovesPerPass && !force)
                break;

            var restless = EnsureComp<WFRestlessCorpseComponent>(corpse);
            if (!force && (now < restless.Next || restless.Moves >= MovesPerCorpse))
                continue;

            if (Seen(corpse))
                continue;

            Shift(corpse);
            restless.Moves++;
            restless.Next = now + TimeSpan.FromSeconds(_random.NextFloat(RestMin, RestMax));
            moved++;
            Moved++;
        }

        return moved;
    }

    /// <summary>False for a body that is carried, strapped down, shut away, anchored or already turning.</summary>
    private bool Free(EntityUid uid, TransformComponent xform)
    {
        return !xform.Anchored
               && !_container.IsEntityInContainer(uid)
               && !HasComp<WFRemadeConvertingComponent>(uid)
               && !(TryComp<BuckleComponent>(uid, out var buckle) && buckle.Buckled)
               && !(TryComp<PullableComponent>(uid, out var pull) && pull.BeingPulled);
    }

    private void Gather()
    {
        _watchers.Clear();
        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent>();
        while (players.MoveNext(out var uid, out _, out _))
        {
            if (!HasComp<WFRemadeComponent>(uid) && !_mobState.IsDead(uid))
                _watchers.Add(uid);
        }
    }

    /// <summary>True if any living player has a clear line to the body within sight range.</summary>
    public bool Seen(EntityUid corpse)
    {
        var there = _transform.GetMapCoordinates(corpse);
        foreach (var player in _watchers)
        {
            var here = _transform.GetMapCoordinates(player);
            if (here.MapId == there.MapId && (here.Position - there.Position).LengthSquared() <= SightRange * SightRange
                && _examine.InRangeUnOccluded(player, corpse, SightRange))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Turns a body over or slides it a hand's width, whichever the floor allows. Callers check that nobody can see.</summary>
    public void Shift(EntityUid corpse)
    {
        var xform = Transform(corpse);
        if (_random.Prob(0.5f))
        {
            var step = new Vector2(_random.NextFloat(-1f, 1f), _random.NextFloat(-1f, 1f));
            var target = xform.Coordinates.Offset(step.LengthSquared() > 0.01f ? Vector2.Normalize(step) * _random.NextFloat(0.15f, 0.4f) : new Vector2(0.2f, 0f));
            if (_turf.TryGetTileRef(target, out var tile) && !_turf.IsSpace(tile.Value)
                && !_turf.IsTileBlocked(tile.Value, CollisionGroup.Impassable))
            {
                _transform.SetCoordinates(corpse, target);
                return;
            }
        }

        var turn = Angle.FromDegrees(_random.NextFloat(25f, 75f) * (_random.Prob(0.5f) ? 1f : -1f));
        _transform.SetLocalRotation(corpse, xform.LocalRotation + turn);
    }
}
