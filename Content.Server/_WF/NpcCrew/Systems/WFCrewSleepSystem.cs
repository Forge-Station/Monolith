using Content.Server._WF.Dormancy;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// Decides which crew must keep thinking with no player nearby. Flying, gunnery, radio, alerts and boarding
/// detection run in their own systems, so a crewman at his station with nothing to react to can sleep.
/// </summary>
public sealed partial class WFCrewSleepSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private WFCrewSecuritySystem _security = default!;
    [Dependency] private WFCrewWorkSystem _work = default!;
    [Dependency] private WFCrewEvaSystem _eva = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private WFDormancySystem _dormancy = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private bool _sleepIdle;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_config, NpcCrewCVars.SleepIdle, value => _sleepIdle = value, true);
    }

    /// <summary>Whether this NPC is crew that has to stay awake whatever the distance to the nearest player.</summary>
    public bool StaysAwake(EntityUid uid)
    {
        if (!TryComp<WFCrewComponent>(uid, out var crew) || !crew.KeepActive)
            return false;
        if (!_sleepIdle)
            return true;

        // A camp or wreck crew on planet ground no player is near has no one to work for.
        if (!_dormancy.IsActive(uid))
            return false;

        // Still on the way to the helm or the gunnery console.
        if (crew.Duty == WFCrewDuties.Pilot && TryComp<WFPilotDutyComponent>(uid, out var pilot) && !pilot.AtHelm
            || crew.Duty == WFCrewDuties.Gunnery && TryComp<WFGunnerDutyComponent>(uid, out var gunner) && !gunner.AtConsole)
            return true;

        var xform = Transform(uid);
        if (_crew.HomeGrid(uid, crew) is not { } home || xform.GridUid != home)
            return true;

        // Off the spot the HTN wants him at, he walks there before he rests. Pilots and gunners work from their console.
        if (crew.Duty != WFCrewDuties.Pilot && crew.Duty != WFCrewDuties.Gunnery && TryComp<HTNComponent>(uid, out var htn)
            && htn.Blackboard.TryGetValue<EntityCoordinates>(WFCrewSystem.PostKey, out var post, EntityManager)
            && !TerminatingOrDeleted(post.EntityId) && !_transform.InRange(xform.Coordinates, post, crew.PostRange + 1f))
            return true;

        if (_alerts.IsAlerted(home, crew.Group) || _security.HasThreat(uid) || _work.HomeGrid(uid) != null)
            return true;

        if (TryComp<NPCRetaliationComponent>(uid, out var retaliation) && retaliation.AttackMemories.Count > 0)
            return true;

        // Bad air has to be walked out of, except by a crewman kitted out or immune to stand in it.
        if (HasComp<WFCrewVoidKitComponent>(uid) || _eva.IsImmune(uid))
            return false;
        if (!TryComp<MapGridComponent>(home, out var grid))
            return true;
        var air = _atmos.GetTileMixture(home, xform.MapUid, _map.LocalToTile(home, grid, xform.Coordinates));
        return air == null || !WFCrewPlannerSystem.IsBreathable(air, air.Pressure);
    }
}
