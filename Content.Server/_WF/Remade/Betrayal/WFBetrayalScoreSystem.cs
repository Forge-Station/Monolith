using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Counterplay;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Torment;
using Content.Server._WF.Remade.Monolith;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Betrayal;
using Content.Shared._WF.Remade.Dread;
using Content.Shared.Examine;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Betrayal;

/// <summary>
/// The music of the dread. When the Director goes quiet to stalk its prey the ambient sound and music fade out for everyone on the
/// stone's ship, and the silence lasts until a Remade first spots a player in the Peak, who gets a stinger. Under a stone the
/// score plays too, in the player's head alone: one more layer for each step of resonance.
/// </summary>
public sealed partial class WFBetrayalScoreSystem : EntitySystem
{
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public const float SpotRange = 14f;
    public const float SilenceMin = 12f;
    public const float SilenceMax = 35f;

    /// <summary>How long the Crescendo song runs, with a little room to fade.</summary>
    private static readonly TimeSpan CrescendoSongLength = TimeSpan.FromSeconds(125);
    public const float ScoreHold = 6f;

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ScoreGap = TimeSpan.FromSeconds(2);

    private readonly Dictionary<EntityUid, WFRemadeDirectorPhase> _phases = new();
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _seen = new();
    private readonly List<EntityUid> _stale = new();
    private TimeSpan _next;

    /// <summary>Silences and stingers sent. Tests read them.</summary>
    public int Silences { get; private set; }

    public int Stingers { get; private set; }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Tick;
        if (_config.GetCVar(WFBetrayalCVars.Rate) <= 0f)
            return;

        UpdateDirectors(now);
        UpdateScore(now);
    }

    private void UpdateDirectors(TimeSpan now)
    {
        var query = EntityQueryEnumerator<WFRemadeDirectorComponent, WFMonolithComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var director, out var monolith, out var xform))
        {
            var before = _phases.GetValueOrDefault(uid, WFRemadeDirectorPhase.Quiet);
            if (director.Phase != before)
            {
                _phases[uid] = director.Phase;
                if (director.Phase == WFRemadeDirectorPhase.Stalk && monolith.Stage >= WFMonolithStage.Growth)
                    Silence(director, xform, now);
                else if (director.Phase == WFRemadeDirectorPhase.Peak)
                    _seen[uid] = new HashSet<EntityUid>();
            }

            if (director.Phase == WFRemadeDirectorPhase.Peak)
                Spot(uid, director, xform);
            else
                _seen.Remove(uid);
        }

        _stale.Clear();
        foreach (var uid in _phases.Keys)
        {
            if (TerminatingOrDeleted(uid))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            _phases.Remove(uid);
            _seen.Remove(uid);
        }
    }

    /// <summary>Clears the ambient sound and music for everyone on the ship, for as long as the Stalk lasts and a little over.</summary>
    public void Silence(WFRemadeDirectorComponent director, TransformComponent monolith, TimeSpan now)
    {
        var seconds = Math.Clamp((float) (director.PhaseEnd - now).TotalSeconds + 10f, SilenceMin, SilenceMax);
        foreach (var (_, actor) in PlayersAboard(monolith, director.Prey))
        {
            RaiseNetworkEvent(new WFBetrayalSilenceEvent(seconds), actor.PlayerSession);
            Silences++;
        }
    }

    /// <summary>Plays a stinger to each player a Remade of the horde sees for the first time this Peak.</summary>
    private void Spot(EntityUid director, WFRemadeDirectorComponent comp, TransformComponent monolith)
    {
        if (!_seen.TryGetValue(director, out var seen))
            seen = _seen[director] = new HashSet<EntityUid>();

        foreach (var (player, actor) in PlayersAboard(monolith, comp.Prey))
        {
            if (seen.Contains(player))
                continue;

            foreach (var remade in comp.Horde)
            {
                if (TerminatingOrDeleted(remade) || _mobState.IsDead(remade) || HasComp<WFRemadePlayingDeadComponent>(remade)
                    || TryComp<WFRemadeBehaviourComponent>(remade, out var behaviour) && behaviour.Mode == WFRemadeMode.Hidden)
                {
                    continue;
                }

                if (!_examine.InRangeUnOccluded(player, remade, SpotRange))
                    continue;

                seen.Add(player);
                Stinger(actor.PlayerSession);
                break;
            }
        }
    }

    /// <summary>Plays one player a stinger and ends their silence.</summary>
    public void Stinger(ICommonSession session)
    {
        RaiseNetworkEvent(new WFBetrayalStingerEvent(_random.Next(WFBetrayalSounds.Stingers.Length)), session);
        Stingers++;
    }

    /// <summary>The living players on the stone's grid and its prey's, with their sessions.</summary>
    private List<(EntityUid Uid, ActorComponent Actor)> PlayersAboard(TransformComponent monolith, EntityUid? prey)
    {
        var result = new List<(EntityUid, ActorComponent)>();
        var preyGrid = prey is { } p && !TerminatingOrDeleted(p) ? Transform(p).GridUid : null;
        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out var actor, out _, out var xform))
        {
            if (xform.MapID != monolith.MapID || xform.GridUid == null || HasComp<WFRemadeComponent>(uid) || _mobState.IsDead(uid))
                continue;

            if (xform.GridUid == monolith.GridUid || xform.GridUid == preyGrid)
                result.Add((uid, actor));
        }

        return result;
    }

    private void UpdateScore(TimeSpan now)
    {
        var sectorWide = _config.GetCVar(WFDreadCVars.Sector);
        var stones = new List<(WFMonolithStage Stage, float Resonance, MapCoordinates At, EntityUid? LocalGrid)>();
        var monoliths = EntityQueryEnumerator<WFMonolithComponent, TransformComponent>();
        while (monoliths.MoveNext(out var uid, out var monolith, out var xform))
        {
            if (monolith.Stage != WFMonolithStage.Quiet && xform.MapID != MapId.Nullspace)
                stones.Add((monolith.Stage, monolith.Resonance, _transform.GetMapCoordinates(uid, xform), monolith.Local ? xform.GridUid ?? uid : null));
        }

        // The Crescendo song plays to everyone once a stone roots; the score stays out of its way until it ends.
        var primes = EntityQueryEnumerator<WFPrimeComponent>();
        while (primes.MoveNext(out var prime))
        {
            if (now - prime.RootedAt < CrescendoSongLength)
                stones.Clear();
        }

        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out var actor, out _, out var xform))
        {
            if (HasComp<WFRemadeComponent>(uid) || _mobState.IsDead(uid) || xform.MapID == MapId.Nullspace)
                continue;

            var subject = EnsureComp<WFBetrayalSubjectComponent>(uid);
            if (now < subject.NextScore)
                continue;

            subject.NextScore = now + ScoreGap;
            var here = _transform.GetMapCoordinates(uid, xform);
            var layers = 0;
            var gain = 0f;
            foreach (var (stage, resonance, at, localGrid) in stones)
            {
                if (localGrid != null && xform.GridUid != localGrid)
                    continue;

                var same = at.MapId == here.MapId;
                var reach = WFDreadMath.Influence(stage, same ? (at.Position - here.Position).Length() : 0f, same, sectorWide && localGrid == null);
                var count = reach > 0f ? WFBetrayalMath.ScoreLayers(resonance) : 0;
                if (count > layers || count == layers && reach > gain)
                {
                    layers = count;
                    gain = reach;
                }
            }

            if (HasComp<WFTormentedComponent>(uid))
            {
                layers = WFBetrayalMath.ScoreLayers(WFMonolithMath.MaxResonance);
                gain = 1f;
            }

            // Only what changes is worth a packet, but a playing score needs renewing before its hold runs out.
            if (layers == 0 && subject.ScoreLayers == 0)
                continue;

            subject.ScoreLayers = layers;
            RaiseNetworkEvent(new WFBetrayalScoreEvent(layers, 0.35f + 0.65f * Math.Clamp(gain, 0f, 1f), ScoreHold), actor.PlayerSession);
        }
    }
}
