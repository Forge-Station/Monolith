using System.Numerics;
using Content.Server._WF.Remade.Abilities;
using Content.Server.Chat.Systems;
using Content.Server.NPC.HTN;
using Content.Shared._WF.Explosion;
using Content.Shared._WF.Remade;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;

namespace Content.Server._WF.Remade.Director;

/// <summary>
/// Hearing. The Remade don't know where the crew are: gunfire, blasts, talking and radios draw them to the source, and
/// otherwise they only drift towards the crew now and then, vaguely at first and closer each time they find nobody.
/// </summary>
public sealed partial class WFRemadeDirectorSystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private WFAbilitySystem _abilities = default!;

    private const float GunRange = 30f;
    private const float QuietGunRange = 8f;
    private const float BlastRange = 25f;
    private const float BlastRangePerStep = 2f;
    private const float BlastRangeMax = 80f;
    private const float NoiseMerge = 3f;
    private const int NoiseMax = 32;
    private const float HearScatter = 2.5f;

    /// <summary>A new place to go that is this far from the old one makes the Remade drop its plan and set off afresh.</summary>
    private const float RetargetDistance = 1f;
    private static readonly TimeSpan NoiseRate = TimeSpan.FromSeconds(0.5);

    private readonly List<HeardNoise> _noises = new();
    private TimeSpan _nextNoise;

    private sealed class HeardNoise(MapCoordinates at, float range)
    {
        public readonly MapCoordinates At = at;
        public float Range = range;
    }

    private void InitializeHearing()
    {
        SubscribeLocalEvent<GunComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<ExplosionShockwaveEvent>(OnBlast);
        SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke);
    }

    private void OnGunShot(Entity<GunComponent> ent, ref GunShotEvent args)
    {
        Noise(_transform.GetMapCoordinates(ent), ent.Comp.SoundGunshot == null ? QuietGunRange : GunRange);

        // Remade that dodge get to react to a shot going by close, which is no event of their own.
        if (args.ToCoordinates.IsValid(EntityManager))
            _abilities.NearMiss(args.User, _transform.ToMapCoordinates(args.ToCoordinates));
    }

    private void OnBlast(ref ExplosionShockwaveEvent args)
    {
        Noise(args.Epicenter, Math.Min(BlastRange + args.Iterations * BlastRangePerStep, BlastRangeMax));
    }

    /// <summary>Something loud happened here. The Remade in earshot come to look when the Director next listens.</summary>
    public void Noise(MapCoordinates at, float range)
    {
        foreach (var noise in _noises)
        {
            if (noise.At.MapId == at.MapId && (noise.At.Position - at.Position).LengthSquared() < NoiseMerge * NoiseMerge)
            {
                noise.Range = Math.Max(noise.Range, range);
                return;
            }
        }

        if (_noises.Count < NoiseMax)
            _noises.Add(new HeardNoise(at, range));
    }

    private void UpdateNoise(TimeSpan now)
    {
        if (now < _nextNoise)
            return;

        _nextNoise = now + NoiseRate;
        if (_noises.Count == 0)
            return;

        var query = EntityQueryEnumerator<WFRemadeBehaviourComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var behaviour, out var xform))
        {
            if (behaviour.Mode != WFRemadeMode.Hunt || HasComp<WFRemadePlayingDeadComponent>(uid) || InFight(uid))
                continue;

            var position = _transform.GetWorldPosition(xform);
            HeardNoise? best = null;
            var bestDistance = float.MaxValue;
            foreach (var noise in _noises)
            {
                if (noise.At.MapId != xform.MapID)
                    continue;

                var distance = (noise.At.Position - position).Length();
                if (distance <= noise.Range && distance < bestDistance)
                {
                    best = noise;
                    bestDistance = distance;
                }
            }

            if (best != null)
                Hear(uid, behaviour, best, bestDistance, now);
        }

        _noises.Clear();
    }

    private void Hear(EntityUid remade, WFRemadeBehaviourComponent behaviour, HeardNoise noise, float distance, TimeSpan now)
    {
        if (behaviour.Known is { } known && (now - behaviour.KnownAt).TotalSeconds < 6.0)
        {
            var current = _transform.ToMapCoordinates(known);
            if (current.MapId == noise.At.MapId && (current.Position - noise.At.Position).Length() < distance)
                return;
        }

        SetKnown(remade, behaviour, OpenSpotNear(OnGrid(noise.At), HearScatter), now);
        behaviour.Misses = 0;
    }

    /// <summary>True while a Remade has a target to fight; it ignores noises then.</summary>
    private bool InFight(EntityUid remade)
    {
        return TryComp<HTNComponent>(remade, out var htn) && htn.Blackboard.ContainsKey("Target");
    }

    /// <summary>Points a Remade at a place. It goes there and nowhere else until it learns something new.</summary>
    private void SetKnown(EntityUid remade, WFRemadeBehaviourComponent behaviour, EntityCoordinates at, TimeSpan now)
    {
        // A move already under way goes on to its old target until it gets there, so a new target needs a fresh plan.
        if (TryComp<HTNComponent>(remade, out var htn)
            && htn.Plan != null
            && htn.Blackboard.TryGetValue<EntityCoordinates>(HuntKey, out var old, EntityManager)
            && (!old.TryDistance(EntityManager, at, out var moved) || moved > RetargetDistance))
        {
            _htn.ShutdownTask(htn.Plan.CurrentOperator, htn.Blackboard, HTNOperatorStatus.Failed);
            _htn.ShutdownPlan(htn);
            htn.Plan = null;
        }

        behaviour.Known = at;
        behaviour.KnownAt = now;
        _npc.SetBlackboard(remade, HuntKey, at);
    }

    private void ClearKnown(EntityUid remade, WFRemadeBehaviourComponent behaviour)
    {
        behaviour.Known = null;
        if (TryComp<HTNComponent>(remade, out var htn))
            htn.Blackboard.Remove<EntityCoordinates>(HuntKey);
    }

    /// <summary>How long a Remade goes between faint senses of where the crew are.</summary>
    private float DriftSeconds(WFMonolithStage stage)
    {
        var seconds = stage switch
        {
            WFMonolithStage.Growth => 90f,
            WFMonolithStage.Harvest => 70f,
            WFMonolithStage.Rapture => 50f,
            WFMonolithStage.Crescendo => 30f,
            _ => 120f,
        };
        return seconds * _random.NextFloat(0.75f, 1.3f);
    }

    /// <summary>It senses the nearest crewman, but only roughly. Each time it finds nobody it senses more exactly.</summary>
    private void Drift(EntityUid remade, WFRemadeBehaviourComponent behaviour, WFMonolithStage stage, TimeSpan now)
    {
        behaviour.NextDrift = now + TimeSpan.FromSeconds(DriftSeconds(stage));
        var grid = Transform(remade).GridUid;
        var position = WorldPos(remade);
        EntityUid? nearest = null;
        var best = float.MaxValue;
        foreach (var crewman in _crew)
        {
            if (Transform(crewman).GridUid != grid)
                continue;

            var distance = (WorldPos(crewman) - position).LengthSquared();
            if (distance < best)
            {
                best = distance;
                nearest = crewman;
            }
        }

        if (nearest is not { } target)
            return;

        var jitter = Math.Max(1.5f, 10f - 2.5f * behaviour.Misses);
        SetKnown(remade, behaviour, OpenSpotNear(Transform(target).Coordinates, jitter), now);
        behaviour.Misses++;
    }

    /// <summary>A place as coordinates on the grid under it, which is what the NPCs' pathing wants, or on the map if there is none.</summary>
    private EntityCoordinates OnGrid(MapCoordinates at)
    {
        return _map.TryFindGridAt(at, out var grid, out _) ? _map.MapToGrid(grid, at) : _transform.ToCoordinates(at);
    }

    /// <summary>True if a place is open floor a Remade could stand on.</summary>
    private bool OpenFloor(EntityCoordinates at)
    {
        return _turf.TryGetTileRef(at, out var tile)
            && !_turf.IsSpace(tile.Value)
            && !_turf.IsTileBlocked(tile.Value, CollisionGroup.Impassable);
    }

    /// <summary>A random open floor tile near a place, or the place itself when none turns up.</summary>
    private EntityCoordinates OpenSpotNear(EntityCoordinates at, float radius)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = at.Offset(_random.NextAngle().ToVec() * _random.NextFloat(0f, radius));
            if (OpenFloor(candidate))
                return candidate;

            radius *= 0.7f;
        }

        return at;
    }

    private bool Arrived(EntityUid remade, EntityCoordinates at, float range)
    {
        var map = _transform.ToMapCoordinates(at);
        var here = _transform.GetMapCoordinates(remade);
        return map.MapId == here.MapId && (map.Position - here.Position).LengthSquared() <= range * range;
    }
}
