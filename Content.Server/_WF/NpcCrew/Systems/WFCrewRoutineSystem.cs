using System.Linq;
using Content.Server._WF.Dormancy;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Keeps console operators facing their stations and spaces deck patrols apart.</summary>
public sealed partial class WFCrewRoutineSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCrewPlannerSystem _planner = default!;
    [Dependency] private WFCrewWeaponSystem _weapons = default!;
    [Dependency] private WFCrewWorkSystem _work = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private WFDormancySystem _dormancy = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        // Only awake crew: a sleeping one neither turns nor walks, and wakes when a player comes.
        var query = EntityQueryEnumerator<ActiveNPCComponent, WFCrewComponent, HTNComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var crew, out var htn, out var xform))
        {
            if (!htn.Enabled || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid) || !_dormancy.IsActive(uid, xform))
                continue;
            if (TryComp<WFPilotDutyComponent>(uid, out var pilot) && pilot.AtHelm
                && TryComp<PilotComponent>(uid, out var held) && held.Console is { } helm)
                Face(uid, helm);
            if (TryComp<WFGunnerDutyComponent>(uid, out var gunner) && gunner.AtConsole && gunner.Console is { } console)
                Face(uid, console);
            if (crew.NextPatrol == TimeSpan.Zero)
                crew.NextPatrol = _timing.CurTime + TimeSpan.FromSeconds(_random.Next(45, 91));
            if (_timing.CurTime < crew.NextPatrol || crew.Duty != WFCrewDuties.Guard
                || crew.Role != WFCrewRoles.Deckhand && crew.Role != WFCrewRoles.Marine
                || xform.GridUid is not { } grid || crew.Post is not { } home || home.EntityId != grid
                || _work.HomeGrid(uid) != null || _weapons.HasLiveThreat(uid))
                continue;
            crew.NextPatrol = _timing.CurTime + TimeSpan.FromSeconds(_random.Next(45, 91));
            var posts = _planner.DeckPosts(grid).Where(post => !_transform.InRange(post.Coordinates, xform.Coordinates, 3f)).ToList();
            if (posts.Count == 0)
                continue;
            htn.Blackboard.SetValue(WFCrewSystem.PostKey, _random.Pick(posts).Coordinates);
            EntityManager.System<WFCrewSpeechSystem>().Say(uid, "patrol");
            EntityManager.System<HTNSystem>().Replan(htn);
        }
    }

    /// <summary>Faces a console while it is occupied, including after movement systems rotate the operator.</summary>
    public void Face(EntityUid mob, EntityUid console)
    {
        if (TerminatingOrDeleted(console))
            return;
        var direction = _transform.GetWorldPosition(console) - _transform.GetWorldPosition(mob);
        if (direction.LengthSquared() > 0.001f)
            _transform.SetWorldRotation(mob, direction.ToWorldAngle());
    }
}
