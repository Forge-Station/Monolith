using System.Linq;
using System.Numerics;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Elites;
using Content.Server._WF.Remade.Monolith;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._WF.Remade;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Examine;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Director;

/// <summary>
/// Plays each Monolith as an unseen opponent. It never shows its hand in a lit room: it worries at one crewman, puts out
/// the lights near them, lets them hear something coming, then sends the Remade through the vents out of sight, and backs
/// off before doing it again. It listens for the crew rather than knowing where they are, and it lets frightened crew
/// breathe. Asleep while no crew are near. Split into partials: tension, hearing, vents, behaviour, dread, playing dead.
/// </summary>
public sealed partial class WFRemadeDirectorSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFRisesSystem _rises = default!;
    [Dependency] private WFRemadeVoiceSystem _voice = default!;

    /// <summary>Blackboard key holding the coordinates the Remade make for.</summary>
    public const string HuntKey = "WFRemadeHunt";

    /// <summary>The most Remade the whole sector carries at once.</summary>
    public const int SectorCap = 60;

    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(1);
    private static readonly ProtoId<DamageGroupPrototype> Brute = "Brute";
    private const float SpawnMin = 6f;
    private const float SpawnMax = 24f;
    private const float CrewSight = 14f;
    private const float StarveShare = 0.012f;

    private readonly List<EntityUid> _crew = new();
    private TimeSpan _nextStarve;

    public override void Initialize()
    {
        base.Initialize();
        InitializeTension();
        InitializeHearing();
        InitializeVents();
        InitializeDread();
        InitializeDead();
        InitializeClosets();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFRemadeDirectorComponent, WFMonolithComponent>();
        while (query.MoveNext(out var uid, out var director, out var monolith))
        {
            if (now < director.NextTick)
                continue;

            director.NextTick = now + TickRate;
            Tick((uid, director, monolith), now);
            UpdateOutbreak((uid, director, monolith), now);
        }

        UpdateTrips(now);
        UpdateJobs(now);
        UpdateDead(now);
        UpdateClosets(now);
        UpdateOmens(now);
        UpdateNoise(now);
        UpdateLockdowns(now);

        if (now >= _nextStarve)
        {
            _nextStarve = now + TickRate;
            Starve();
        }
    }

    private void Tick(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, TimeSpan now)
    {
        var director = ent.Comp1;
        var stage = ent.Comp2.Stage;

        director.Horde.RemoveWhere(uid => TerminatingOrDeleted(uid) || _mobState.IsDead(uid) && !_rises.IsRising(uid));

        GatherCrew(ent, director.AwakeRadius);
        if (_crew.Count == 0)
            return;

        UpdateStress(director, now);

        if (stage >= WFMonolithStage.Growth && !director.Seeded)
        {
            director.Seeded = true;
            director.Biomass += director.FirstGrowthBiomass;
            if (director.BuildUp)
            {
                var quiet = _random.NextFloat(director.FirstQuietMin, director.FirstQuietMax);
                director.FirstWaveAt = now + TimeSpan.FromSeconds(quiet);
                director.SightingAt = now + TimeSpan.FromSeconds(quiet * 0.6f);
            }
        }

        director.Biomass += Income(stage);
        director.Will = Math.Min(director.Will + WillIncome(stage), WillCap);

        Steer(director, stage, now);
        Build(ent, now);

        if (now >= director.NextDread)
            Dread(ent, now);

        switch (director.Phase)
        {
            case WFRemadeDirectorPhase.Quiet:
                if (stage >= WFMonolithStage.Growth)
                {
                    // The first Growth builds up: signs first, then one glimpse of a Remade, then the first wave.
                    if (now < director.FirstWaveAt)
                    {
                        if (director.SightingAt is { } sighting && now >= sighting)
                            TrySendSighting(ent, now);

                        break;
                    }

                    LullTricks(ent, now);
                    if (now >= director.PhaseEnd)
                        TryBeginStalk(ent, now);
                }

                break;
            case WFRemadeDirectorPhase.Stalk:
                if (now >= director.PhaseEnd)
                    TryBeginPeak(ent, now);
                break;
            case WFRemadeDirectorPhase.Peak:
                if (ActiveHorde(director) == 0 || now >= director.PhaseEnd)
                    EndPeak(ent, now);
                break;
            case WFRemadeDirectorPhase.Relax:
                if (stage >= WFMonolithStage.Growth)
                    LullTricks(ent, now);
                if (now >= director.PhaseEnd)
                {
                    director.Phase = WFRemadeDirectorPhase.Quiet;
                    director.PhaseEnd = now + QuietTime(stage);
                }

                break;
        }
    }

    /// <summary>Living players near the Monolith, none of them Remade.</summary>
    private void GatherCrew(EntityUid monolith, float radius)
    {
        _crew.Clear();
        var origin = _transform.GetMapCoordinates(monolith);
        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (HasComp<WFRemadeComponent>(uid) || _mobState.IsDead(uid) || xform.MapID != origin.MapId || HiddenQuietly(uid))
                continue;

            if ((_transform.GetWorldPosition(xform) - origin.Position).Length() <= radius)
            {
                _crew.Add(uid);
                EnsureComp<WFRemadeStressComponent>(uid);
            }
        }
    }

    private static float Income(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Growth => 0.3f,
            WFMonolithStage.Harvest => 0.6f,
            WFMonolithStage.Rapture => 1f,
            WFMonolithStage.Crescendo => 2f,
            _ => 0f,
        };
    }

    /// <summary>How many Remade may stand at once at a stage.</summary>
    public static int HordeCap(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Growth => 3,
            WFMonolithStage.Harvest => 8,
            WFMonolithStage.Rapture => 16,
            WFMonolithStage.Crescendo => 30,
            _ => 0,
        };
    }

    private TimeSpan QuietTime(WFMonolithStage stage)
    {
        var (min, max) = stage switch
        {
            WFMonolithStage.Growth => (180f, 300f),
            WFMonolithStage.Harvest => (120f, 200f),
            WFMonolithStage.Rapture => (60f, 120f),
            _ => (30f, 60f),
        };
        return TimeSpan.FromSeconds(_random.NextFloat(min, max));
    }

    /// <summary>Remade that are on the loose: not lying dead, not in the vents.</summary>
    private int ActiveHorde(WFRemadeDirectorComponent director)
    {
        var count = 0;
        foreach (var uid in director.Horde)
        {
            if (!HasComp<WFRemadePlayingDeadComponent>(uid))
                count++;
        }

        return count;
    }

    /// <summary>Picks its prey, kills the nearest lights and lets them hear what's coming.</summary>
    private void TryBeginStalk(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, TimeSpan now)
    {
        var director = ent.Comp1;
        if (PickPrey(director, ent.Comp2.Stage, now) is not { } prey)
        {
            director.HeldSince ??= now;
            director.PhaseEnd = now + TimeSpan.FromSeconds(8);
            return;
        }

        director.Phase = WFRemadeDirectorPhase.Stalk;
        director.PhaseEnd = now + TimeSpan.FromSeconds(_random.NextFloat(8f, 15f));
        director.Prey = prey;

        BlowLightsNear(prey, 3);
        Brownout(prey, now);

        var offset = _random.NextAngle().ToVec() * _random.NextFloat(12f, 18f);
        _audio.PlayStatic(director.FarCry, Filter.Pvs(prey), Transform(prey).Coordinates.Offset(offset), true);
    }

    /// <summary>Starts the wave, unless its prey is still shaken, in which case it waits.</summary>
    private void TryBeginPeak(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, TimeSpan now)
    {
        var director = ent.Comp1;
        var stage = ent.Comp2.Stage;

        if (director.Prey is not { } prey || TerminatingOrDeleted(prey) || _mobState.IsDead(prey) || !_crew.Contains(prey))
        {
            if (PickPrey(director, stage, now) is not { } other)
            {
                director.Phase = WFRemadeDirectorPhase.Quiet;
                director.PhaseEnd = now + TimeSpan.FromSeconds(10);
                director.Prey = null;
                return;
            }

            prey = other;
        }

        if (IsHeld(director, prey, stage, now))
        {
            director.Prey = prey;
            director.PhaseEnd = now + TimeSpan.FromSeconds(5);
            return;
        }

        director.HeldSince = null;
        director.Prey = prey;
        director.Phase = WFRemadeDirectorPhase.Peak;
        director.PhaseEnd = now + TimeSpan.FromSeconds(150);
        SendWave(ent, prey, now);
    }

    private void EndPeak(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, TimeSpan now)
    {
        var director = ent.Comp1;
        director.Phase = WFRemadeDirectorPhase.Relax;
        director.PhaseEnd = now + TimeSpan.FromSeconds(_random.NextFloat(45f, 90f));

        if (director.Prey is { } prey && TryComp<WFRemadeStressComponent>(prey, out var stress))
            stress.NoWaveUntil = now + TimeSpan.FromSeconds(director.WaveRest);
    }

    /// <summary>Buys as many Remade as biomass and the caps allow, and brings them in out of sight of the prey, by different ways.</summary>
    private void SendWave(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, EntityUid prey, TimeSpan now)
    {
        var director = ent.Comp1;
        var stage = ent.Comp2.Stage;
        var room = Math.Min(HordeCap(stage) - director.Horde.Count, SectorCap - CountSector());
        if (room <= 0 || Transform(prey).GridUid is not { } grid)
            return;

        var points = ArrivalPoints(ent, prey, grid);
        if (points.Count == 0)
            return;

        var bought = new List<EntProtoId>();
        for (var i = 0; i < room; i++)
        {
            if (Buy(ent, prey, true) is not { } proto)
                break;

            bought.Add(proto);
        }

        if (bought.Count == 0)
            return;

        var squads = SpreadPoints(points, prey, Math.Min(bought.Count, MaxSquads));
        var origins = OriginVents(ent, prey, grid);
        var origin = origins.Count > 0 ? _random.Pick(origins) : (EntityUid?) null;

        for (var i = 0; i < bought.Count; i++)
        {
            if (i > 0 && _random.Prob(FakeShare(stage)) && TryPlaceFake(ent, bought[i], prey))
                continue;

            var point = squads[i % squads.Count];
            var role = i == 0 && bought.Count >= 3 ? WFRemadeMode.Stalk : WFRemadeMode.Hunt;
            Deliver(ent, prey, bought[i], point, role, origin, now);
        }
    }

    /// <summary>Whether the stone is still building up: below Growth, or in the quiet spell before its first wave.</summary>
    public bool HoldingBack(EntityUid stone, TimeSpan now)
    {
        if (!TryComp<WFMonolithComponent>(stone, out var monolith) || monolith.Stage < WFMonolithStage.Growth)
            return true;

        return TryComp<WFRemadeDirectorComponent>(stone, out var director) && now < director.FirstWaveAt;
    }

    /// <summary>The crew's first look at a Remade: one, out of sight, that follows someone and slips into a vent once seen.</summary>
    private void TrySendSighting(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, TimeSpan now)
    {
        var director = ent.Comp1;
        director.SightingAt = now + TimeSpan.FromSeconds(10);
        if (PickPrey(director, ent.Comp2.Stage, now) is not { } prey || Transform(prey).GridUid is not { } grid)
            return;

        var points = ArrivalPoints(ent, prey, grid);
        if (points.Count == 0 || Buy(ent, prey, false) is not { } proto)
            return;

        director.SightingAt = null;
        var remade = Deliver(ent, prey, proto, _random.Pick(points), WFRemadeMode.Stalk, null, now);
        EnsureComp<WFRemadeBehaviourComponent>(remade).Sighting = true;
    }

    /// <summary>Puts one new Remade into the world at a point, either straight in or by a trip through the vents.</summary>
    private EntityUid Deliver(
        Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent,
        EntityUid prey,
        EntProtoId proto,
        SpawnPoint point,
        WFRemadeMode role,
        EntityUid? origin,
        TimeSpan now)
    {
        var director = ent.Comp1;
        var from = origin is { } o && point.Vent != null && VentDistance(o, point.Vent.Value) > TripMin ? o : (EntityUid?) null;
        var remade = Spawn(proto, from != null ? Transform(from.Value).Coordinates : point.At);
        Enlist(remade, ent.Owner);
        var behaviour = EnsureComp<WFRemadeBehaviourComponent>(remade);
        behaviour.AfterVent = role;

        if (from != null && point.Vent != null)
        {
            if (TryVentTravel(remade, from.Value, point.Vent.Value, point.At, now, null))
                return remade;

            _transform.SetCoordinates(remade, point.At);
        }

        Emerge(remade, behaviour, point.At, prey, now);
        return remade;
    }

    /// <summary>A Remade coming into the world at a place: sets what it does next and lets the crew hear it.</summary>
    private void Emerge(EntityUid remade, WFRemadeBehaviourComponent behaviour, EntityCoordinates at, EntityUid? prey, TimeSpan now)
    {
        var source = CompOrNull<WFRemadeComponent>(remade)?.Source;
        var director = CompOrNull<WFRemadeDirectorComponent>(source);
        if (director != null)
            _audio.PlayPvs(director.BurstSound, remade);

        if (TryComp<WFRemadeVoiceComponent>(remade, out var voice))
            _voice.Shout((remade, voice));

        if (behaviour.AfterVent == WFRemadeMode.Stalk && prey != null)
        {
            BeginStalking(remade, behaviour, prey.Value, now);
            return;
        }

        behaviour.Mode = WFRemadeMode.Hunt;
        if (prey != null && !TerminatingOrDeleted(prey.Value))
            SetKnown(remade, behaviour, Transform(prey.Value).Coordinates, now);
    }

    /// <summary>Pays for one Remade the Director can have now, by weight. A mob that needs corpses goes first when it can.</summary>
    private EntProtoId? Buy(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, EntityUid prey, bool allowCorpseMobs)
    {
        var director = ent.Comp1;
        var options = new List<WFRemadeMobPrototype>();
        var corpses = allowCorpseMobs && AnyCorpseNear(prey);
        foreach (var mob in _proto.EnumeratePrototypes<WFRemadeMobPrototype>())
        {
            if (mob.SpawnWeight <= 0f
                || ent.Comp2.Stage < mob.MinStage
                || director.Biomass < mob.Cost
                || mob.NeedsCorpses && !corpses
                || mob.MaxAlive > 0 && CountAlive(director, mob) >= mob.MaxAlive)
            {
                continue;
            }

            options.Add(mob);
        }

        if (options.Count == 0)
            return null;

        var needing = options.Where(mob => mob.NeedsCorpses).ToList();
        var pick = PickWeighted(needing.Count > 0 ? needing : options);
        director.Biomass -= pick.Cost;
        return pick.Entity;
    }

    private WFRemadeMobPrototype PickWeighted(List<WFRemadeMobPrototype> options)
    {
        var roll = _random.NextFloat(0f, options.Sum(mob => mob.SpawnWeight));
        foreach (var mob in options)
        {
            roll -= mob.SpawnWeight;
            if (roll <= 0f)
                return mob;
        }

        return options[^1];
    }

    private int CountAlive(WFRemadeDirectorComponent director, WFRemadeMobPrototype mob)
    {
        return director.Horde.Count(uid => MetaData(uid).EntityPrototype?.ID == mob.Entity.Id);
    }

    private bool AnyCorpseNear(EntityUid prey)
    {
        var bodies = new HashSet<Entity<MobStateComponent>>();
        _lookup.GetEntitiesInRange(Transform(prey).Coordinates, SpawnMax, bodies);
        return bodies.Any(body => _mobState.IsDead(body) && !HasComp<WFRemadeComponent>(body));
    }

    private int CountSector()
    {
        var count = 0;
        var query = EntityQueryEnumerator<WFRemadeComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!_mobState.IsDead(uid))
                count++;
        }

        return count;
    }

    /// <summary>Marks a new Remade and puts it under a Monolith's Director.</summary>
    public void Enlist(EntityUid remade, EntityUid? monolith)
    {
        EnsureComp<WFRemadeComponent>(remade).Source = monolith;
        var behaviour = EnsureComp<WFRemadeBehaviourComponent>(remade);
        behaviour.NextDrift = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(8f, 25f));
        if (TryComp<WFRemadeDirectorComponent>(monolith, out var director))
        {
            director.Horde.Add(remade);

            // The Director sends its Remade at crew up to its own radius, so they stay awake that far out. At the
            // NPC default of 32 m a wave from a far vent, or one drawn by a distant shot, would stand asleep.
            if (TryComp<HTNComponent>(remade, out var htn))
                htn.SleepPlayerCheckRangeOverride = Math.Max(htn.SleepPlayerCheckRangeOverride ?? 0f, director.AwakeRadius);
        }
    }

    /// <summary>The Monolith on the same map nearest an entity, if any.</summary>
    public EntityUid? NearestMonolith(EntityUid uid)
    {
        var origin = _transform.GetMapCoordinates(uid);
        EntityUid? nearest = null;
        var best = float.MaxValue;
        var query = EntityQueryEnumerator<WFMonolithComponent, TransformComponent>();
        while (query.MoveNext(out var monolith, out _, out var xform))
        {
            if (xform.MapID != origin.MapId)
                continue;

            var distance = (_transform.GetWorldPosition(xform) - origin.Position).LengthSquared();
            if (distance < best)
            {
                best = distance;
                nearest = monolith;
            }
        }

        return nearest;
    }

    /// <summary>The Remade of a lost Monolith waste away.</summary>
    private void Starve()
    {
        var query = EntityQueryEnumerator<WFRemadeComponent>();
        while (query.MoveNext(out var uid, out var remade))
        {
            if (remade.Source is not { } source || !TerminatingOrDeleted(source) || _mobState.IsDead(uid))
                continue;

            // 2 a second, or enough to waste away in about two minutes for the big ones, so the Undying doesn't outlast the round.
            var amount = _thresholds.TryGetDeadThreshold(uid, out var dead) ? Math.Max(2f, dead.Value.Float() * StarveShare) : 2f;
            _damageable.TryChangeDamage(uid, new DamageSpecifier(_proto.Index(Brute), amount), ignoreResistances: true, interruptsDoAfters: false);
        }
    }

    /// <summary>True if a living crewman has a clear view of a place, within a range.</summary>
    private bool SeenByCrew(EntityCoordinates at, float range)
    {
        var actors = new HashSet<Entity<ActorComponent>>();
        _lookup.GetEntitiesInRange(at, range, actors);
        if (actors.Count == 0)
            return false;

        var map = _transform.ToMapCoordinates(at);
        foreach (var actor in actors)
        {
            if (HasComp<WFRemadeComponent>(actor) || _mobState.IsDead(actor))
                continue;

            if (_examine.InRangeUnOccluded(actor.Owner, map, range))
                return true;
        }

        return false;
    }

    private Vector2 WorldPos(EntityUid uid)
    {
        return _transform.GetWorldPosition(uid);
    }
}
