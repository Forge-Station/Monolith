using System.Numerics;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Monolith;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._WF.Remade;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Random;

namespace Content.Server._WF.Remade.Director;

/// <summary>How each Remade acts between orders: hunting by ear, stalking at a distance, running when hurt.</summary>
public sealed partial class WFRemadeDirectorSystem
{
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;

    private const float StalkMin = 9f;
    private const float StalkMax = 16f;
    private const float StalkSight = 18f;
    private const float StalkEyes = 1.5f;
    private const float HurtFraction = 0.55f;
    private const float RetreatRange = 30f;
    private const float RetreatHeal = 0.3f;
    private const int MaxRetreats = 1;

    private void Steer(WFRemadeDirectorComponent director, WFMonolithStage stage, TimeSpan now)
    {
        foreach (var remade in director.Horde)
        {
            // A hunter or the Undying lying down until it rises stays in the horde, but it can't be steered.
            if (_mobState.IsDead(remade))
                continue;

            if (!TryComp<WFRemadeBehaviourComponent>(remade, out var behaviour) || HasComp<WFRemadePlayingDeadComponent>(remade))
                continue;

            // A path that failed once stays failed while the same target is registered; drop it so the next plan tries again.
            if (TryComp<NPCSteeringComponent>(remade, out var steering) && steering.Status == SteeringStatus.NoPath)
                _steering.Unregister(remade, steering);

            switch (behaviour.Mode)
            {
                case WFRemadeMode.Hunt:
                    SteerHunt(remade, behaviour, stage, now);
                    break;
                case WFRemadeMode.Stalk:
                    SteerStalk(remade, behaviour, now);
                    break;
                case WFRemadeMode.Retreat:
                    SteerRetreat(remade, behaviour, now);
                    break;
            }
        }
    }

    private void SteerHunt(EntityUid remade, WFRemadeBehaviourComponent behaviour, WFMonolithStage stage, TimeSpan now)
    {
        if (behaviour.Known is { } known && (now - behaviour.KnownAt).TotalSeconds > 2.0 && Arrived(remade, known, 2.5f))
            ClearKnown(remade, behaviour);

        if (InFight(remade))
        {
            behaviour.Misses = 0;
            behaviour.NextDrift = now + TimeSpan.FromSeconds(DriftSeconds(stage));
            return;
        }

        if (behaviour.Retreats < MaxRetreats && IsHurt(remade) && BeginRetreat(remade, behaviour, now))
            return;

        if (now >= behaviour.NextDrift)
            Drift(remade, behaviour, stage, now);
    }

    private bool IsHurt(EntityUid remade)
    {
        return TryComp<DamageableComponent>(remade, out var damageable)
            && _thresholds.TryGetDeadThreshold(remade, out var dead)
            && (float) damageable.TotalDamage >= (float) dead.Value * HurtFraction;
    }

    /// <summary>A hurt Remade that no crewman can see makes for the nearest vent.</summary>
    private bool BeginRetreat(EntityUid remade, WFRemadeBehaviourComponent behaviour, TimeSpan now)
    {
        var xform = Transform(remade);
        if (xform.GridUid is not { } grid
            || CompOrNull<WFRemadeComponent>(remade)?.Source == null
            || SeenByCrew(xform.Coordinates, StalkSight))
        {
            return false;
        }

        var vent = NearestVent(grid, WorldPos(remade), RetreatRange);
        if (vent == null)
            return false;

        behaviour.Mode = WFRemadeMode.Retreat;
        behaviour.Target = vent;
        behaviour.ModeUntil = now + TimeSpan.FromSeconds(40);
        behaviour.Retreats++;
        SetKnown(remade, behaviour, Transform(vent.Value).Coordinates, now);
        return true;
    }

    private void SteerRetreat(EntityUid remade, WFRemadeBehaviourComponent behaviour, TimeSpan now)
    {
        if (behaviour.Target is not { } vent || TerminatingOrDeleted(vent) || now >= behaviour.ModeUntil)
        {
            behaviour.Mode = WFRemadeMode.Hunt;
            return;
        }

        var at = Transform(vent).Coordinates;
        if (!Arrived(remade, at, 2.2f))
        {
            SetKnown(remade, behaviour, at, now);
            return;
        }

        // The sighting goes in even with eyes on it; that is the point of it.
        if (!behaviour.Sighting && SeenByCrew(at, CrewSight))
            return;

        var grid = Transform(remade).GridUid;
        var destination = grid == null ? null : FarVent(grid.Value, vent);
        if (destination == null)
        {
            behaviour.Mode = WFRemadeMode.Hunt;
            return;
        }

        if (TryComp<DamageableComponent>(remade, out var damageable))
            _damageable.TryChangeDamage(remade, damageable.Damage * -RetreatHeal, ignoreResistances: true);

        behaviour.AfterVent = WFRemadeMode.Hunt;
        var hold = CompOrNull<WFRemadeDirectorComponent>(CompOrNull<WFRemadeComponent>(remade)?.Source);
        var returnAt = now + TimeSpan.FromSeconds(_random.NextFloat(hold?.RetreatMin ?? 30f, hold?.RetreatMax ?? 60f));
        if (behaviour.Sighting && hold != null && hold.FirstWaveAt > returnAt)
            returnAt = hold.FirstWaveAt;

        behaviour.Sighting = false;
        if (!TryVentTravel(remade, vent, destination.Value, Transform(destination.Value).Coordinates, now, returnAt))
            behaviour.Mode = WFRemadeMode.Hunt;
    }

    /// <summary>A stalker eats its sight down to nothing so it never starts the fight, and watches the crewman instead.</summary>
    private void BeginStalking(EntityUid remade, WFRemadeBehaviourComponent behaviour, EntityUid target, TimeSpan now)
    {
        behaviour.Mode = WFRemadeMode.Stalk;
        behaviour.Target = target;
        behaviour.ModeUntil = now + TimeSpan.FromSeconds(_random.NextFloat(50f, 90f));
        ClearKnown(remade, behaviour);

        if (behaviour.Sight == null
            && TryComp<HTNComponent>(remade, out var htn)
            && htn.Blackboard.TryGetValue<float>("VisionRadius", out var vision, EntityManager)
            && htn.Blackboard.TryGetValue<float>("AggroVisionRadius", out var aggro, EntityManager))
        {
            behaviour.Sight = (vision, aggro);
        }

        _npc.SetBlackboard(remade, "VisionRadius", StalkEyes);
        _npc.SetBlackboard(remade, "AggroVisionRadius", StalkEyes);
    }

    /// <summary>Sets a Remade to follow a crewman at a distance for a while before it commits.</summary>
    public void Stalk(EntityUid remade, EntityUid target, float seconds)
    {
        var now = _timing.CurTime;
        var behaviour = EnsureComp<WFRemadeBehaviourComponent>(remade);
        BeginStalking(remade, behaviour, target, now);
        behaviour.ModeUntil = now + TimeSpan.FromSeconds(seconds);
    }

    /// <summary>The sighting, seen, makes for the nearest vent to vanish into.</summary>
    private bool SlipAway(EntityUid remade, WFRemadeBehaviourComponent behaviour, TransformComponent xform, TimeSpan now)
    {
        if (xform.GridUid is not { } grid || NearestVent(grid, WorldPos(remade), RetreatRange) is not { } vent)
            return false;

        if (behaviour.Sight is { } sight)
        {
            _npc.SetBlackboard(remade, "VisionRadius", sight.Vision);
            _npc.SetBlackboard(remade, "AggroVisionRadius", sight.Aggro);
            behaviour.Sight = null;
        }

        behaviour.Mode = WFRemadeMode.Retreat;
        behaviour.Target = vent;
        behaviour.ModeUntil = now + TimeSpan.FromSeconds(40);
        SetKnown(remade, behaviour, Transform(vent).Coordinates, now);
        return true;
    }

    /// <summary>The stalker's patience is up, or its prey is gone: it fights.</summary>
    private void Commit(EntityUid remade, WFRemadeBehaviourComponent behaviour, TimeSpan now)
    {
        behaviour.Mode = WFRemadeMode.Hunt;
        if (behaviour.Sight is { } sight)
        {
            _npc.SetBlackboard(remade, "VisionRadius", sight.Vision);
            _npc.SetBlackboard(remade, "AggroVisionRadius", sight.Aggro);
            behaviour.Sight = null;
        }

        if (behaviour.Target is { } target && !TerminatingOrDeleted(target) && !_mobState.IsDead(target))
            SetKnown(remade, behaviour, Transform(target).Coordinates, now);

        behaviour.Target = null;
    }

    private void SteerStalk(EntityUid remade, WFRemadeBehaviourComponent behaviour, TimeSpan now)
    {
        if (behaviour.Target is not { } target || TerminatingOrDeleted(target) || _mobState.IsDead(target))
        {
            Commit(remade, behaviour, now);
            return;
        }

        if (now >= behaviour.ModeUntil || InFight(remade))
        {
            Commit(remade, behaviour, now);
            return;
        }

        var here = WorldPos(remade);
        var there = WorldPos(target);
        var gap = there - here;
        var distance = gap.Length();
        var xform = Transform(remade);

        if (behaviour.Sighting && SeenByCrew(xform.Coordinates, StalkSight) && SlipAway(remade, behaviour, xform, now))
            return;

        if (SeenByCrew(xform.Coordinates, StalkSight) || distance < StalkMin)
        {
            var away = distance > 0.1f ? -gap / distance : _random.NextAngle().ToVec();
            SetKnown(remade, behaviour, BackOffSpot(here, xform.MapID, away) ?? xform.Coordinates, now);
        }
        else if (distance > StalkMax)
        {
            SetKnown(remade, behaviour, Transform(target).Coordinates, now);
        }
        else
        {
            SetKnown(remade, behaviour, xform.Coordinates, now);
        }
    }

    /// <summary>A place a few steps off in the direction given, or turned aside from it if that is a wall or the void.</summary>
    private EntityCoordinates? BackOffSpot(Vector2 here, MapId map, Vector2 away)
    {
        foreach (var turn in new[] { 0f, 1f, -1f, 2f, -2f })
        {
            var spot = OnGrid(new MapCoordinates(here + new Angle(MathF.PI / 4f * turn).RotateVec(away) * 6f, map));
            if (OpenFloor(spot))
                return spot;
        }

        return null;
    }

    /// <summary>The quiet stretches: it puts bodies in the dark for the crew to find and lets a lone stalker follow someone.</summary>
    private void LullTricks(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, TimeSpan now)
    {
        var director = ent.Comp1;
        if (director.NextAmbush == TimeSpan.Zero)
            director.NextAmbush = now + TimeSpan.FromSeconds(_random.NextFloat(30f, 90f));

        if (director.NextStalker == TimeSpan.Zero)
            director.NextStalker = now + TimeSpan.FromSeconds(_random.NextFloat(90f, 200f));

        if (now >= director.NextAmbush)
        {
            director.NextAmbush = now + TimeSpan.FromSeconds(_random.NextFloat(60f, 150f));
            TryAmbush(ent, now);
        }

        if (now >= director.NextStalker)
        {
            director.NextStalker = now + TimeSpan.FromSeconds(_random.NextFloat(120f, 240f));
            TrySendStalker(ent, now);
        }

        if (director.NextCloset == TimeSpan.Zero)
            director.NextCloset = now + TimeSpan.FromSeconds(_random.NextFloat(40f, 100f));

        if (now >= director.NextCloset)
        {
            director.NextCloset = now + TimeSpan.FromSeconds(_random.NextFloat(80f, 180f));
            TryLurkInCloset(ent, now);
        }
    }

    private void TrySendStalker(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, TimeSpan now)
    {
        var director = ent.Comp1;
        if (director.Horde.Count >= HordeCap(ent.Comp2.Stage) || HasStalker(director))
            return;

        if (PickPrey(director, ent.Comp2.Stage, now) is not { } prey || Transform(prey).GridUid is not { } grid)
            return;

        var points = ArrivalPoints(ent, prey, grid);
        if (points.Count == 0 || Buy(ent, prey, false) is not { } proto)
            return;

        Deliver(ent, prey, proto, _random.Pick(points), WFRemadeMode.Stalk, null, now);
    }

    private bool HasStalker(WFRemadeDirectorComponent director)
    {
        foreach (var uid in director.Horde)
        {
            if (TryComp<WFRemadeBehaviourComponent>(uid, out var behaviour)
                && (behaviour.Mode == WFRemadeMode.Stalk || behaviour.AfterVent == WFRemadeMode.Stalk && behaviour.Mode == WFRemadeMode.Hidden))
            {
                return true;
            }
        }

        return false;
    }
}
