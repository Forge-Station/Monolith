using System.Linq;
using System.Numerics;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Counterplay;
using Content.Server._WF.Remade.Garrison;
using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Remade.Structures;
using Content.Server.NPC.HTN;
using Content.Shared._WF.Remade;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Remade.Director;

/// <summary>
/// The garrison. Apart from the hunting horde, a Monolith keeps a standing guard that holds within a dozen metres of it, patrols
/// and lurks round it, and fights whoever comes near. Losses are made good quickly, at a share of a hunter's price, from the floor
/// of the corruption round the stone or out of a vent near it. When the stone is hurt or worked on, everything within forty metres
/// drops what it is doing and goes for whoever did it.
/// </summary>
public sealed partial class WFRemadeDirectorSystem
{
    [Dependency] private IConfigurationManager _garrisonCfg = default!;

    /// <summary>Guards pay this share of a Remade's price.</summary>
    public const float GuardPrice = 0.4f;

    /// <summary>How far a Monolith's cry carries.</summary>
    public const float ConvergeRange = 40f;

    /// <summary>The stone raises guards from its own store of flesh, so they never take from what the Director spends on waves.</summary>
    public const float CreditRate = 3f;

    public const float CreditCap = 200f;
    public const float CreditStart = 120f;

    private const float GuardMin = 5f;
    private const float GuardMax = 11f;
    private const float GuardLeash = 18f;
    private const float GuardSight = 10f;
    private const float RiseMin = 5f;
    private const float RiseMax = 12f;
    private const float VentReach = 25f;

    private static readonly SoundSpecifier RiseSound = new SoundCollectionSpecifier("WFEnvSquelch");
    private static readonly EntProtoId RiseGore = "WFEnvGoreScraps";

    /// <summary>What a guard can be, from which stage, by weight. Elites are the ones the stone keeps a quota of; <c>Max</c> caps one kind.</summary>
    private static readonly (EntProtoId Proto, WFMonolithStage Min, float Weight, bool Elite, int Max)[] GuardTable =
    {
        ("WFRemadeHopper", WFMonolithStage.Growth, 3f, false, 0),
        ("WFRemadeSlasher", WFMonolithStage.Growth, 4f, false, 0),
        ("WFRemadeSpitter", WFMonolithStage.Growth, 2f, false, 0),
        ("WFRemadeLeaper", WFMonolithStage.Growth, 2f, false, 0),
        ("WFRemadeLurker", WFMonolithStage.Harvest, 2f, false, 0),
        ("WFRemadeTwitcher", WFMonolithStage.Harvest, 2f, true, 0),
        ("WFRemadeDivider", WFMonolithStage.Harvest, 1.5f, false, 0),
        ("WFRemadeSlasherEnhanced", WFMonolithStage.Rapture, 2f, true, 0),
        ("WFRemadeLeaperEnhanced", WFMonolithStage.Rapture, 1.5f, true, 0),
        ("WFRemadePuker", WFMonolithStage.Rapture, 1f, false, 0),
        ("WFRemadeBrute", WFMonolithStage.Rapture, 3f, true, 2),
        ("WFRemadeBruteFlesh", WFMonolithStage.Rapture, 1f, true, 1),
        ("WFRemadeHunter", WFMonolithStage.Rapture, 1.5f, true, 1),
    };

    /// <summary>How many guards a Monolith keeps at a stage, and more once it has rooted as the Prime.</summary>
    public static int GarrisonSize(WFMonolithStage stage, bool prime)
    {
        var size = stage switch
        {
            WFMonolithStage.Growth => 2,
            WFMonolithStage.Harvest => 4,
            WFMonolithStage.Rapture => 7,
            WFMonolithStage.Crescendo => 10,
            _ => 0,
        };
        return size > 0 && prime ? size + 3 : size;
    }

    /// <summary>How many of the garrison must be elites, the brutes and their kind.</summary>
    public static int GarrisonElites(WFMonolithStage stage, bool prime)
    {
        var elites = stage switch
        {
            WFMonolithStage.Rapture => 1,
            WFMonolithStage.Crescendo => 3,
            _ => 0,
        };
        return elites > 0 && prime ? elites + 1 : elites;
    }

    /// <summary>Seconds before a lost guard's place is filled.</summary>
    private static float ReplaceEvery(WFMonolithStage stage) => stage >= WFMonolithStage.Rapture ? 3f : 5f;

    /// <summary>Keeps the garrison up and sends each guard on its rounds. Called once a second for each Monolith at Growth or above.</summary>
    public void TickGarrison(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, TimeSpan now)
    {
        if (!_garrisonCfg.GetCVar(WFGarrisonCVars.Enabled) || now < ent.Comp1.FirstWaveAt)
            return;

        var garrison = EnsureComp<WFGarrisonComponent>(ent.Owner);
        garrison.Guards.RemoveWhere(guard => TerminatingOrDeleted(guard) || _mobState.IsDead(guard));

        GatherCrew(ent, ent.Comp1.AwakeRadius);
        if (_crew.Count == 0)
            return;

        if (garrison.LastCredit == TimeSpan.Zero)
        {
            garrison.LastCredit = now;
            garrison.Credit = CreditStart;
        }

        var seconds = Math.Min((float) (now - garrison.LastCredit).TotalSeconds, 5f);
        garrison.LastCredit = now;
        garrison.Credit = Math.Min(garrison.Credit + seconds * CreditRate, CreditCap);

        var prime = HasComp<WFPrimeComponent>(ent.Owner);
        if (garrison.Guards.Count < GarrisonSize(ent.Comp2.Stage, prime) && now >= garrison.NextReplace)
        {
            var raised = RaiseGuard(ent, garrison, prime, now);
            garrison.NextReplace = now + TimeSpan.FromSeconds(raised ? ReplaceEvery(ent.Comp2.Stage) : 3f);
        }

        foreach (var guard in garrison.Guards)
        {
            Patrol(ent.Owner, guard, now);
        }
    }

    /// <summary>One guard walks a new round near the stone now and then, and is led back if it chases too far.</summary>
    private void Patrol(EntityUid stone, EntityUid guard, TimeSpan now)
    {
        if (!TryComp<WFRemadeGuardComponent>(guard, out var comp)
            || !TryComp<WFRemadeBehaviourComponent>(guard, out var behaviour)
            || _mobState.IsIncapacitated(guard)
            || HasComp<WFRemadeVentTripComponent>(guard))
        {
            return;
        }

        var distance = (WorldPos(guard) - WorldPos(stone)).Length();
        if (InFight(guard))
        {
            if (distance > GuardLeash && now >= comp.ConvergeUntil && TryComp<HTNComponent>(guard, out var htn))
            {
                htn.Blackboard.Remove<EntityUid>("Target");
                SetKnown(guard, behaviour, PatrolSpot(stone), now);
                comp.NextPatrol = now + TimeSpan.FromSeconds(_random.NextFloat(5f, 12f));
            }

            return;
        }

        if (now < comp.NextPatrol && distance <= GuardMax + 3f)
            return;

        comp.NextPatrol = now + TimeSpan.FromSeconds(_random.NextFloat(5f, 12f));
        SetKnown(guard, behaviour, PatrolSpot(stone), now);
    }

    /// <summary>An open spot a little way from the stone.</summary>
    private EntityCoordinates PatrolSpot(EntityUid stone)
    {
        var at = Transform(stone).Coordinates;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var spot = at.Offset(_random.NextAngle().ToVec() * _random.NextFloat(GuardMin, GuardMax));
            if (OpenFloor(spot))
                return spot;
        }

        return OpenSpotNear(at, GuardMax);
    }

    private bool RaiseGuard(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, WFGarrisonComponent garrison, bool prime, TimeSpan now)
    {
        if (CountSector() >= SectorCap || PickGuard(ent, garrison, prime) is not { } pick)
            return false;

        var stone = ent.Owner;
        var (at, tile) = GuardSpot(ent);
        garrison.Credit -= pick.Price;
        var guard = Spawn(pick.Proto, at);

        EnsureComp<WFRemadeComponent>(guard).Source = stone;
        var behaviour = EnsureComp<WFRemadeBehaviourComponent>(guard);
        behaviour.Mode = WFRemadeMode.Guard;
        behaviour.NextDrift = now + TimeSpan.FromHours(1);
        EnsureComp<WFRemadeGuardComponent>(guard).Stone = stone;
        garrison.Guards.Add(guard);
        garrison.Raised++;

        if (TryComp<HTNComponent>(guard, out var htn))
        {
            // Awake out to the Director's radius like the rest, but it only sees a little way, so it holds near the stone.
            htn.SleepPlayerCheckRangeOverride = Math.Max(htn.SleepPlayerCheckRangeOverride ?? 0f, ent.Comp1.AwakeRadius);
            _npc.SetBlackboard(guard, "VisionRadius", GuardSight);
            _npc.SetBlackboard(guard, "AggroVisionRadius", GuardSight + 2f);
        }

        _audio.PlayPvs(tile != null ? RiseSound : ent.Comp1.BurstSound, guard);
        if (tile != null)
            Spawn(RiseGore, new EntityCoordinates(tile.Value, Vector2.Zero));

        SetKnown(guard, behaviour, PatrolSpot(stone), now);
        return true;
    }

    /// <summary>A guard the stone can afford, picked by weight, keeping its quota of elites and the cap on each kind.</summary>
    private (EntProtoId Proto, float Price)? PickGuard(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, WFGarrisonComponent garrison, bool prime)
    {
        var stage = ent.Comp2.Stage;
        var costs = _proto.EnumeratePrototypes<WFRemadeMobPrototype>().ToDictionary(mob => mob.Entity.Id, mob => mob.Cost);
        var kinds = new Dictionary<string, int>();
        foreach (var guard in garrison.Guards)
        {
            var id = MetaData(guard).EntityPrototype?.ID ?? string.Empty;
            kinds[id] = kinds.GetValueOrDefault(id) + 1;
        }

        var elites = GuardTable.Where(row => row.Elite).Sum(row => kinds.GetValueOrDefault(row.Proto.Id));
        var needElite = elites < GarrisonElites(stage, prime);

        var options = new List<(EntProtoId Proto, float Weight, float Price, bool Elite)>();
        foreach (var row in GuardTable)
        {
            var price = costs.GetValueOrDefault(row.Proto.Id, 50f) * GuardPrice;
            if (stage < row.Min
                || garrison.Credit < price
                || row.Max > 0 && kinds.GetValueOrDefault(row.Proto.Id) >= row.Max)
            {
                continue;
            }

            options.Add((row.Proto, row.Weight, price, row.Elite));
        }

        // It keeps a quota of elites. If it can afford one it raises it; if not it saves up once it is half manned, rather than fill the place with runts.
        if (needElite)
        {
            if (options.Any(option => option.Elite))
                options.RemoveAll(option => !option.Elite);
            else if (garrison.Guards.Count * 2 >= GarrisonSize(stage, prime))
                return null;
        }

        if (options.Count == 0)
            return null;

        var roll = _random.NextFloat(0f, options.Sum(option => option.Weight));
        foreach (var option in options)
        {
            roll -= option.Weight;
            if (roll <= 0f)
                return (option.Proto, option.Price);
        }

        return (options[^1].Proto, options[^1].Price);
    }

    /// <summary>Where a new guard comes up: mostly out of the corruption round the stone, else out of a vent near it, else the floor beside it.</summary>
    private (EntityCoordinates At, EntityUid? Tile) GuardSpot(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent)
    {
        var stonePosition = WorldPos(ent.Owner);
        var grid = Transform(ent.Owner).GridUid;
        if (grid != null && !_random.Prob(0.3f))
        {
            var tiles = new List<EntityUid>();
            foreach (var uid in ent.Comp2.Corruption)
            {
                if (tiles.Count >= 40)
                    break;

                if (!TryComp(uid, out TransformComponent? xform) || xform.GridUid != grid)
                    continue;

                var distance = (_transform.GetWorldPosition(xform) - stonePosition).Length();
                if (distance is >= RiseMin and <= RiseMax && OpenFloor(xform.Coordinates))
                    tiles.Add(uid);
            }

            if (tiles.Count > 0)
            {
                var tile = _random.Pick(tiles);
                return (Transform(tile).Coordinates, tile);
            }
        }

        if (grid != null)
        {
            var near = NearestVent(grid.Value, stonePosition, VentReach);
            if (near is { } vent)
                return (Transform(vent).Coordinates, null);
        }

        return (OpenSpotNear(Transform(ent.Owner).Coordinates, 8f), null);
    }

    /// <summary>
    /// The stone's cry: every Remade of a Monolith within range drops what it is doing and goes for the attacker. A stalker commits,
    /// a retreat or a build is given up, and a Remade playing dead gets up. Returns how many answered.
    /// </summary>
    public int Converge(EntityUid stone, EntityUid attacker, TimeSpan now)
    {
        if (TerminatingOrDeleted(stone) || TerminatingOrDeleted(attacker) || _mobState.IsDead(attacker))
            return 0;

        var garrison = EnsureComp<WFGarrisonComponent>(stone);
        if (now < garrison.NextConverge)
            return 0;

        garrison.NextConverge = now + TimeSpan.FromSeconds(2);
        garrison.Calls++;

        if (now >= garrison.NextCry)
        {
            garrison.NextCry = now + TimeSpan.FromSeconds(10);
            if (TryComp<WFRemadeDirectorComponent>(stone, out var director))
                _audio.PlayPvs(director.FarCry, stone);

            _popup.PopupEntity(Loc.GetString("wf-remade-guard-cry"), stone, PopupType.LargeCaution);
        }

        var at = Transform(attacker).Coordinates;
        var map = _transform.GetMapId(stone);
        var origin = WorldPos(stone);
        var answered = 0;
        var query = EntityQueryEnumerator<WFRemadeBehaviourComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var behaviour, out var xform))
        {
            if (xform.MapID != map
                || (_transform.GetWorldPosition(xform) - origin).Length() > ConvergeRange
                || behaviour.Mode == WFRemadeMode.Hidden
                || _mobState.IsIncapacitated(uid)
                || HasComp<WFRemadeVentTripComponent>(uid))
            {
                continue;
            }

            if (HasComp<WFRemadePlayingDeadComponent>(uid))
            {
                Rise(uid, attacker);
                answered++;
                continue;
            }

            if (TryComp<WFRemadeBuildJobComponent>(uid, out var job))
                EndJob(uid, job);

            if (behaviour.Mode == WFRemadeMode.Stalk)
                Commit(uid, behaviour, now);
            else if (behaviour.Mode is WFRemadeMode.Retreat or WFRemadeMode.Build)
                behaviour.Mode = WFRemadeMode.Hunt;

            behaviour.Misses = 0;
            SetKnown(uid, behaviour, at, now);
            if (TryComp<HTNComponent>(uid, out var htn))
                htn.Blackboard.SetValue("Target", attacker);

            if (TryComp<WFRemadeGuardComponent>(uid, out var guard))
                guard.ConvergeUntil = now + TimeSpan.FromSeconds(30);

            answered++;
        }

        return answered;
    }

    /// <summary>The nearest living player to a place, within range. For when the stone is hurt by something that has no name.</summary>
    public EntityUid? NearestPlayer(EntityUid near, float range)
    {
        EntityUid? best = null;
        var bestDistance = range * range;
        var origin = _transform.GetMapCoordinates(near);
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID != origin.MapId || HasComp<WFRemadeComponent>(uid) || _mobState.IsDead(uid))
                continue;

            var distance = (_transform.GetWorldPosition(xform) - origin.Position).LengthSquared();
            if (distance < bestDistance)
            {
                best = uid;
                bestDistance = distance;
            }
        }

        return best;
    }
}
