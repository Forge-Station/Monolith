using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Robust.Shared.Map;

namespace Content.Server._WF.CavernEncounters;

/// <summary>
/// Steers the cavern people through their HTN's keys (<c>WFCavernWalkerCompound</c>): a spot to walk or run to, which
/// comes first, and someone to follow.
/// </summary>
public sealed partial class WFCavernWalkerSystem : EntitySystem
{
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private NPCSystem _npc = default!;

    /// <summary>The spot an NPC walks or runs to, ahead of anything else it would do.</summary>
    public const string MoveKey = "WFCavernMoveTarget";

    /// <summary>Sends an NPC to a spot, dropping whatever it was doing.</summary>
    public void WalkTo(EntityUid uid, EntityCoordinates target)
    {
        if (!TryComp<HTNComponent>(uid, out var htn))
            return;

        _npc.SetBlackboard(uid, MoveKey, target, htn);
        _htn.Replan(htn);
    }

    /// <summary>Has an NPC follow someone, keeping close.</summary>
    public void Follow(EntityUid uid, EntityUid leader)
    {
        if (!TryComp<HTNComponent>(uid, out var htn))
            return;

        _npc.SetBlackboard(uid, NPCBlackboard.FollowTarget, new EntityCoordinates(leader, Vector2.Zero), htn);
        _htn.Replan(htn);
    }

    /// <summary>Stops an NPC walking to its spot and following anyone.</summary>
    public void Stop(EntityUid uid)
    {
        if (!TryComp<HTNComponent>(uid, out var htn))
            return;

        htn.Blackboard.Remove<EntityCoordinates>(MoveKey);
        htn.Blackboard.Remove<EntityCoordinates>(NPCBlackboard.FollowTarget);
        _htn.Replan(htn);
    }

    /// <summary>The spot an NPC is walking to, if any.</summary>
    public EntityCoordinates? MoveTarget(EntityUid uid)
    {
        return TryComp<HTNComponent>(uid, out var htn) && htn.Blackboard.TryGetValue<EntityCoordinates>(MoveKey, out var target, EntityManager)
            ? target
            : null;
    }

    /// <summary>Whom an NPC is following, if anyone.</summary>
    public EntityUid? Leader(EntityUid uid)
    {
        return TryComp<HTNComponent>(uid, out var htn) && htn.Blackboard.TryGetValue<EntityCoordinates>(NPCBlackboard.FollowTarget, out var target, EntityManager)
            ? target.EntityId
            : null;
    }
}
