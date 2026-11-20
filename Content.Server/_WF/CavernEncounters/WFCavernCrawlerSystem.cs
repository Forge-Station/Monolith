using System.Linq;
using System.Numerics;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.CavernEncounters;

/// <summary>
/// Blind crawlers hunt by sound: a running mob within earshot becomes the quarry of the crawler that heard it and of
/// its nest-mates nearby, and stays so until it has gone a few seconds without running.
/// </summary>
public sealed partial class WFCavernCrawlerSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private static readonly TimeSpan ListenInterval = TimeSpan.FromSeconds(0.25);

    private TimeSpan _nextListen;
    private readonly HashSet<Entity<MobStateComponent>> _heard = new();
    private readonly HashSet<Entity<WFCavernCrawlerComponent>> _nest = new();

    /// <summary>
    /// Whether a mob is loud enough to be heard: moving faster than halfway between its walking and running speeds.
    /// </summary>
    public bool IsRunning(EntityUid uid)
    {
        if (!TryComp<PhysicsComponent>(uid, out var physics))
            return false;

        var walk = 2.5f;
        var sprint = 4.5f;
        if (TryComp<MovementSpeedModifierComponent>(uid, out var move))
        {
            walk = move.CurrentWalkSpeed;
            sprint = move.CurrentSprintSpeed;
        }

        // A mob that can't run any faster than it walks is never loud.
        if (sprint <= walk + 0.1f)
            return false;

        return physics.LinearVelocity.Length() > (walk + sprint) / 2f;
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextListen)
            return;

        _nextListen = now + ListenInterval;

        var query = EntityQueryEnumerator<WFCavernCrawlerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crawler, out var xform))
        {
            if (!_mobState.IsAlive(uid))
                continue;

            Listen((uid, crawler), xform, now);
        }
    }

    /// <summary>One crawler listens: whoever runs within earshot becomes its quarry, and its nest's; quiet quarry is forgotten.</summary>
    public void Listen(Entity<WFCavernCrawlerComponent> ent, TransformComponent xform, TimeSpan now)
    {
        _heard.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, ent.Comp.HearingRange, _heard);

        foreach (var mob in _heard)
        {
            if (mob.Owner == ent.Owner || HasComp<WFCavernCrawlerComponent>(mob) || !_mobState.IsAlive(mob)
                || !HasComp<InputMoverComponent>(mob) || !IsRunning(mob))
                continue;

            Hunt(ent, mob, now);

            _nest.Clear();
            _lookup.GetEntitiesInRange(xform.Coordinates, ent.Comp.NestRange, _nest);
            foreach (var mate in _nest)
            {
                if (mate.Owner != ent.Owner && _mobState.IsAlive(mate))
                    Hunt(mate, mob, now);
            }
        }

        foreach (var (quarry, heard) in ent.Comp.Quarry.ToList())
        {
            if (!TerminatingOrDeleted(quarry) && now - heard < ent.Comp.ForgetAfter)
                continue;

            ent.Comp.Quarry.Remove(quarry);
            if (!TerminatingOrDeleted(quarry))
                _faction.DeAggroEntity(ent.Owner, quarry);
        }
    }

    /// <summary>Makes a mob a crawler's quarry, or keeps it so.</summary>
    private void Hunt(Entity<WFCavernCrawlerComponent> ent, EntityUid quarry, TimeSpan now)
    {
        if (!ent.Comp.Quarry.ContainsKey(quarry))
        {
            _faction.AggroEntity(ent.Owner, quarry);
            _audio.PlayPvs(ent.Comp.AlertSound, ent);
        }

        ent.Comp.Quarry[quarry] = now;
    }

    /// <summary>Whether a crawler is after a mob.</summary>
    public bool IsHunting(Entity<WFCavernCrawlerComponent?> ent, EntityUid quarry)
    {
        return Resolve(ent, ref ent.Comp, false) && ent.Comp.Quarry.ContainsKey(quarry);
    }
}
