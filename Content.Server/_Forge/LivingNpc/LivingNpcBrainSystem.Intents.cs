using System.Numerics;
using Content.Shared._Forge.LivingNpc;
using Content.Shared._Forge.LivingNpc.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Buckle.Components;
using Content.Shared.Interaction;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._Forge.LivingNpc;

public sealed partial class LivingNpcBrainSystem
{
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private RotateToFaceSystem _rotate = default!;

    private void ExecuteIntent(EntityUid uid, LivingNpcComponent npc, in LivingNpcWorld world, TimeSpan now)
    {
        if (TryComp<PullableComponent>(uid, out var pullable) && pullable.BeingPulled)
        {
            _movement.Stop(uid);
            return;
        }

        if (TryComp<BuckleComponent>(uid, out var buckle) && buckle.Buckled &&
            npc.CurrentIntent is LivingNpcIntent.Flee or LivingNpcIntent.Fight)
        {
            // Stay buckled unless threatened — movement system will fail closed.
            _movement.Stop(uid);
        }

        switch (npc.CurrentIntent)
        {
            case LivingNpcIntent.Idle:
                ExecuteIdle(uid, npc);
                break;
            case LivingNpcIntent.Fidget:
                ExecuteFidget(uid, npc);
                break;
            case LivingNpcIntent.Wander:
                ExecuteWander(uid, npc);
                break;
            case LivingNpcIntent.Work:
                ExecuteGoToSpot(uid, npc, world.WorkSpot ?? world.PatrolSpot, LivingNpcSpeechKind.Work);
                break;
            case LivingNpcIntent.Rest:
                ExecuteGoToSpot(uid, npc, world.RestSpot, LivingNpcSpeechKind.Idle);
                npc.Mood.Energy = Math.Clamp(npc.Mood.Energy + 0.04f, 0f, 1f);
                break;
            case LivingNpcIntent.Socialize:
                ExecuteSocialize(uid, npc, world);
                break;
            case LivingNpcIntent.Converse:
                ExecuteConverse(uid, npc);
                break;
            case LivingNpcIntent.Eat:
                ExecuteEat(uid, npc, world);
                break;
            case LivingNpcIntent.Investigate:
                ExecuteInvestigate(uid, npc, world);
                break;
            case LivingNpcIntent.Help:
                ExecuteHelp(uid, npc, world);
                break;
            case LivingNpcIntent.Flee:
                ExecuteFlee(uid, npc, world);
                break;
            case LivingNpcIntent.Fight:
                ExecuteFight(uid, npc, world);
                break;
        }

        MaybeIdleChatter(uid, npc, world, now);
    }

    private void ExecuteIdle(EntityUid uid, LivingNpcComponent npc)
    {
        _movement.Stop(uid);
        if (_random.Prob(0.12f))
            _rotate.TryFaceAngle(uid, _random.NextAngle());
    }

    private void ExecuteFidget(EntityUid uid, LivingNpcComponent npc)
    {
        _movement.Stop(uid);
        if (_random.Prob(0.35f))
            _rotate.TryFaceAngle(uid, _random.NextAngle());

        if (_random.Prob(0.2f))
        {
            var emote = _random.Pick(new[] { "Sigh", "Yawn", "Cough", "Laugh" });
            if (emote == "Laugh" && npc.Personality.Humor < 0.35f)
                emote = "Sigh";
            _social.TryEmote(uid, npc, emote);
        }
        else if (_random.Prob(0.15f))
        {
            _social.TryCustomEmote(uid, npc, "living-npc-emote-fidget");
        }
    }

    private void ExecuteWander(EntityUid uid, LivingNpcComponent npc)
    {
        if (!_blocker.CanMove(uid))
            return;

        if (npc.Destination != null && !_movement.NoPath(uid) && !_movement.Arrived(uid))
            return;

        if (_movement.Arrived(uid) || _movement.NoPath(uid) || npc.Destination == null)
        {
            if (_movement.NoPath(uid))
                npc.FailedMoves++;

            if (npc.FailedMoves > 4)
            {
                npc.CurrentIntent = LivingNpcIntent.Idle;
                _movement.Stop(uid);
                npc.Destination = null;
                return;
            }

            var dest = PickWanderDestination(uid, npc);
            npc.Destination = dest;
            _movement.MoveTo(uid, dest, 0.9f);
        }
    }

    private EntityCoordinates PickWanderDestination(EntityUid uid, LivingNpcComponent npc)
    {
        var xform = Transform(uid);
        if (npc.HomeSet &&
            xform.Coordinates.TryDistance(EntityManager, npc.Home, out var fromHome) &&
            fromHome > npc.HomeRadius * 0.85f)
        {
            return npc.Home.Offset(_random.NextVector2(0.5f, Math.Min(2.5f, npc.HomeRadius * 0.3f)));
        }

        var offset = _random.NextVector2(1.2f, npc.WanderRange);
        var dest = xform.Coordinates.Offset(offset);
        if (npc.HomeSet &&
            npc.Home.TryDistance(EntityManager, dest, out var dist) &&
            dist > npc.HomeRadius)
        {
            dest = npc.Home.Offset(_random.NextVector2(1f, Math.Max(1.5f, npc.HomeRadius * 0.4f)));
        }

        return dest;
    }

    private void ExecuteGoToSpot(EntityUid uid, LivingNpcComponent npc, EntityUid? spot, LivingNpcSpeechKind chatter)
    {
        if (spot is not { Valid: true })
        {
            ExecuteWander(uid, npc);
            return;
        }

        npc.CurrentTarget = spot;
        if (_movement.InRange(uid, spot.Value, 1.4f))
        {
            _movement.Stop(uid);
            if (_random.Prob(0.08f))
                TryChatter(uid, npc, chatter);
            return;
        }

        _movement.MoveToEntity(uid, spot.Value, 1.1f);
    }

    private void ExecuteSocialize(EntityUid uid, LivingNpcComponent npc, in LivingNpcWorld world)
    {
        var target = npc.ConversationPartner ?? world.NearbyPerson;
        if (target is not { Valid: true } person || TerminatingOrDeleted(person))
        {
            npc.CurrentIntent = LivingNpcIntent.Idle;
            return;
        }

        npc.CurrentTarget = person;
        Face(uid, person);

        if (!_movement.InRange(uid, person, 2.2f))
        {
            _movement.MoveToEntity(uid, person, 1.6f);
            return;
        }

        _movement.Stop(uid);
        if (TryComp<LivingNpcSocialComponent>(uid, out var social) && !social.Greeted.Contains(person))
        {
            social.Greeted.Add(person);
            var name = Content.Shared.IdentityManagement.Identity.Name(person, EntityManager, uid);
            npc.QueuedSpeech = _social.PickLine(npc, LivingNpcSpeechKind.Greeting, name);
            npc.CurrentIntent = LivingNpcIntent.Converse;
            return;
        }

        if (_random.Prob(0.25f * npc.Personality.Extraversion))
        {
            var kind = HasComp<LivingNpcComponent>(person)
                ? LivingNpcSpeechKind.NpcChat
                : LivingNpcSpeechKind.SmallTalk;
            TryChatter(uid, npc, kind, person);
        }
        else if (_random.Prob(0.2f))
        {
            _social.TryCustomEmote(uid, npc, "living-npc-emote-wave");
        }
    }

    private void ExecuteConverse(EntityUid uid, LivingNpcComponent npc)
    {
        if (npc.CurrentTarget is { Valid: true } target)
            Face(uid, target);

        _movement.Stop(uid);

        if (!string.IsNullOrEmpty(npc.QueuedSpeech))
        {
            if (TryComp<LivingNpcMemoryComponent>(uid, out var memory) && npc.CurrentTarget is { } partner)
                _memory.NoteTalk(uid, memory, partner);
            _social.TrySay(uid, npc, npc.QueuedSpeech);
        }

        if (npc.QueuedSpeech == null)
            npc.CurrentIntent = LivingNpcIntent.Idle;
    }

    private void ExecuteEat(EntityUid uid, LivingNpcComponent npc, in LivingNpcWorld world)
    {
        TryChatter(uid, npc, LivingNpcSpeechKind.Hunger);
        if (world.Food is { Valid: true } food)
        {
            npc.CurrentTarget = food;
            if (_movement.InRange(uid, food, 1.3f))
            {
                _movement.Stop(uid);
                Face(uid, food);
                return;
            }

            _movement.MoveToEntity(uid, food, 0.9f);
            return;
        }

        ExecuteWander(uid, npc);
    }

    private void ExecuteInvestigate(EntityUid uid, LivingNpcComponent npc, in LivingNpcWorld world)
    {
        var target = world.Stranger ?? world.NearbyPerson;
        if (target is not { Valid: true })
        {
            ExecuteWander(uid, npc);
            return;
        }

        npc.CurrentTarget = target;
        Face(uid, target.Value);
        if (_movement.InRange(uid, target.Value, 2.4f))
        {
            _movement.Stop(uid);
            if (_random.Prob(0.2f))
                _social.TryCustomEmote(uid, npc, "living-npc-emote-look");
            return;
        }

        _movement.MoveToEntity(uid, target.Value, 1.8f);
    }

    private void ExecuteHelp(EntityUid uid, LivingNpcComponent npc, in LivingNpcWorld world)
    {
        if (world.Injured is not { Valid: true } injured)
        {
            npc.CurrentIntent = LivingNpcIntent.Idle;
            return;
        }

        npc.CurrentTarget = injured;
        TryChatter(uid, npc, LivingNpcSpeechKind.Help);
        if (_movement.InRange(uid, injured, 1.6f))
        {
            _movement.Stop(uid);
            Face(uid, injured);
            return;
        }

        _movement.MoveToEntity(uid, injured, 1.2f);
    }

    private void ExecuteFlee(EntityUid uid, LivingNpcComponent npc, in LivingNpcWorld world)
    {
        var threat = world.Threat ?? npc.CurrentTarget;
        if (threat is not { Valid: true } || TerminatingOrDeleted(threat.Value))
        {
            npc.Mood.Fear = Math.Clamp(npc.Mood.Fear - 0.08f, 0f, 1f);
            npc.CurrentIntent = LivingNpcIntent.Idle;
            _movement.Stop(uid);
            return;
        }

        TryChatter(uid, npc, LivingNpcSpeechKind.Fear);
        var origin = _transform.GetWorldPosition(uid);
        var danger = _transform.GetWorldPosition(threat.Value);
        var away = origin - danger;
        if (away.LengthSquared() < 0.05f)
            away = _random.NextVector2();
        away = away.Normalized() * Math.Max(4f, npc.WanderRange);

        var dest = Transform(uid).Coordinates.Offset(away);
        npc.Destination = dest;
        npc.CurrentTarget = threat;
        _movement.MoveTo(uid, dest, 0.8f);
    }

    private void ExecuteFight(EntityUid uid, LivingNpcComponent npc, in LivingNpcWorld world)
    {
        var target = world.Threat ?? npc.CurrentTarget;
        if (target is not { Valid: true } || TerminatingOrDeleted(target.Value) || !_mobState.IsAlive(target.Value))
        {
            npc.CurrentTarget = null;
            npc.CurrentIntent = LivingNpcIntent.Idle;
            _combat.LeaveCombat(uid);
            return;
        }

        npc.CurrentTarget = target;
        TryChatter(uid, npc, LivingNpcSpeechKind.Anger);
        Face(uid, target.Value);
        _combat.TickMelee(uid, npc);
    }

    private void MaybeIdleChatter(EntityUid uid, LivingNpcComponent npc, in LivingNpcWorld world, TimeSpan now)
    {
        if (!TryComp<LivingNpcSocialComponent>(uid, out var social))
            return;

        if (now < social.NextIdleChat || npc.CurrentIntent is LivingNpcIntent.Fight or LivingNpcIntent.Flee)
            return;

        social.NextIdleChat = now + TimeSpan.FromSeconds(
            _random.NextFloat((float) social.MinSpeechGap.TotalSeconds, (float) social.MaxSpeechGap.TotalSeconds)
            * (1.4f - npc.Personality.Extraversion));

        if (!_random.Prob(0.35f + npc.Personality.Extraversion * 0.4f))
            return;

        var kind = npc.CurrentIntent switch
        {
            LivingNpcIntent.Work => LivingNpcSpeechKind.Work,
            LivingNpcIntent.Socialize => world.NearbyNpc != null ? LivingNpcSpeechKind.NpcChat : LivingNpcSpeechKind.SmallTalk,
            _ => npc.Mood.DominantKey() switch
            {
                "fear" => LivingNpcSpeechKind.Fear,
                "anger" => LivingNpcSpeechKind.Anger,
                _ => LivingNpcSpeechKind.Idle,
            },
        };

        TryChatter(uid, npc, kind, world.NearbyPerson);
    }

    private void TryChatter(EntityUid uid, LivingNpcComponent npc, LivingNpcSpeechKind kind, EntityUid? toward = null)
    {
        var name = toward is { Valid: true } other
            ? Content.Shared.IdentityManagement.Identity.Name(other, EntityManager, uid)
            : null;
        var line = _social.PickLine(npc, kind, name);
        if (line != null)
            _social.TrySay(uid, npc, line);
    }

    private void Face(EntityUid uid, EntityUid target)
    {
        _rotate.TryFaceCoordinates(uid, _transform.GetWorldPosition(target));
    }
}
