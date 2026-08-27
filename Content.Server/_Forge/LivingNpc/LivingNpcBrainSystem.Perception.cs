using Content.Server.Nutrition.Components;
using Content.Shared._Forge.LivingNpc;
using Content.Shared._Forge.LivingNpc.Components;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Player;

namespace Content.Server._Forge.LivingNpc;

public sealed partial class LivingNpcBrainSystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private NpcFactionSystem _factions = default!;

    private LivingNpcWorld Perceive(EntityUid uid, LivingNpcComponent npc, TimeSpan now)
    {
        var world = new LivingNpcWorld();
        if (!TryComp(uid, out TransformComponent? xform))
            return world;

        TryComp<LivingNpcMemoryComponent>(uid, out var memory);
        var origin = _transform.GetWorldPosition(xform);
        var range = npc.VisionRange;

        foreach (var other in _lookup.GetEntitiesInRange(uid, range, LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (other == uid || TerminatingOrDeleted(other) || HasComp<GhostComponent>(other))
                continue;

            var dist = (_transform.GetWorldPosition(other) - origin).Length();
            ConsiderPerson(uid, npc, memory, ref world, other, dist, now);
            ConsiderSpot(npc, ref world, other, dist);
            ConsiderFood(ref world, other, dist);
        }

        foreach (var hostile in _factions.GetNearbyHostiles(uid, range))
        {
            if (hostile == uid || !_mobState.IsAlive(hostile))
                continue;

            var dist = _movement.Distance(uid, hostile);
            if (world.Threat == null || dist < world.ThreatDist)
            {
                world.Threat = hostile;
                world.ThreatDist = dist;
            }
        }

        return world;
    }

    private void ConsiderPerson(
        EntityUid uid,
        LivingNpcComponent npc,
        LivingNpcMemoryComponent? memory,
        ref LivingNpcWorld world,
        EntityUid other,
        float dist,
        TimeSpan now)
    {
        var isPlayer = HasComp<ActorComponent>(other);
        var isNpc = HasComp<LivingNpcComponent>(other);
        var isMob = TryComp<MobStateComponent>(other, out var mob);

        if (!isPlayer && !isNpc && !isMob)
            return;

        if (isPlayer)
            world.HasPlayersNearby = true;

        if (isMob && mob!.CurrentState == MobState.Critical)
        {
            if (world.Injured == null || dist < world.InjuredDist)
            {
                world.Injured = other;
                world.InjuredDist = dist;
            }
        }

        if ((!isPlayer && !isNpc) || !_mobState.IsAlive(other))
            return;

        var known = memory != null && _memory.IsKnown(memory, other);
        if (memory != null)
            _memory.NoteSeen(uid, memory, other);

        if (!known && isPlayer)
        {
            world.Stranger = other;
            world.StrangerDist = dist;
        }

        var reputation = memory != null ? _memory.GetReputation(memory, other) : 0f;
        if (reputation <= -0.4f && (world.Threat == null || dist < world.ThreatDist))
        {
            world.Threat = other;
            world.ThreatDist = dist;
        }

        var prefer = isPlayer || other == npc.ConversationPartner;
        if (world.NearbyPerson == null ||
            (prefer && !world.NearbyPersonIsPlayer) ||
            dist < world.NearbyPersonDist)
        {
            world.NearbyPerson = other;
            world.NearbyPersonDist = dist;
            world.NearbyPersonIsPlayer = isPlayer;
        }

        if (isNpc && (world.NearbyNpc == null || dist < world.NearbyNpcDist))
        {
            world.NearbyNpc = other;
            world.NearbyNpcDist = dist;
        }
    }

    private void ConsiderSpot(LivingNpcComponent npc, ref LivingNpcWorld world, EntityUid other, float dist)
    {
        if (!TryComp<LivingNpcSpotComponent>(other, out var spot))
            return;

        if (spot.Group != null && spot.Group != npc.Group)
            return;

        switch (spot.Kind)
        {
            case LivingNpcSpotKind.Work when world.WorkSpot == null || dist < world.WorkDist:
                world.WorkSpot = other;
                world.WorkDist = dist;
                break;
            case LivingNpcSpotKind.Rest when world.RestSpot == null || dist < world.RestDist:
                world.RestSpot = other;
                world.RestDist = dist;
                break;
            case LivingNpcSpotKind.Social when world.SocialSpot == null || dist < world.SocialDist:
                world.SocialSpot = other;
                world.SocialDist = dist;
                break;
            case LivingNpcSpotKind.Patrol when world.PatrolSpot == null || dist < world.PatrolDist:
                world.PatrolSpot = other;
                world.PatrolDist = dist;
                break;
        }
    }

    private void ConsiderFood(ref LivingNpcWorld world, EntityUid other, float dist)
    {
        if (!HasComp<FoodComponent>(other))
            return;

        if (world.Food == null || dist < world.FoodDist)
        {
            world.Food = other;
            world.FoodDist = dist;
        }
    }
}

public struct LivingNpcWorld
{
    public EntityUid? NearbyPerson;
    public float NearbyPersonDist;
    public bool NearbyPersonIsPlayer;
    public EntityUid? NearbyNpc;
    public float NearbyNpcDist;
    public EntityUid? Threat;
    public float ThreatDist;
    public EntityUid? Injured;
    public float InjuredDist;
    public EntityUid? WorkSpot;
    public float WorkDist;
    public EntityUid? RestSpot;
    public float RestDist;
    public EntityUid? SocialSpot;
    public float SocialDist;
    public EntityUid? PatrolSpot;
    public float PatrolDist;
    public EntityUid? Food;
    public float FoodDist;
    public EntityUid? Stranger;
    public float StrangerDist;
    public bool HasPlayersNearby;
}
