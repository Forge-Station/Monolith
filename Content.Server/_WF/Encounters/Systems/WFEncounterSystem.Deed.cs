using Content.Server._Mono.Cleanup;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Encounters.Systems;

public sealed partial class WFEncounterSystem
{
    /// <summary>Crew roles from the highest in command down.</summary>
    private static readonly ProtoId<WFCrewRolePrototype>[] ChainOfCommand =
    {
        WFCrewRoles.Captain, WFCrewRoles.Pilot, WFCrewRoles.RadioOperator, WFCrewRoles.Gunner,
        WFCrewRoles.Engineer, WFCrewRoles.Marine, WFCrewRoles.Deckhand,
    };

    /// <summary>Gives a ship's deed to the highest-ranking of its crew, to drop when he dies.</summary>
    private void GiveDeed(EntityUid grid, List<EntityUid> crewmen, EntProtoId deed)
    {
        EntityUid? chief = null;
        var best = int.MaxValue;
        foreach (var uid in crewmen)
        {
            if (!TryComp<WFCrewComponent>(uid, out var crew))
                continue;

            var rank = crew.Role is { } role ? Array.IndexOf(ChainOfCommand, role) : -1;
            if (rank < 0)
                rank = ChainOfCommand.Length;
            if (rank >= best)
                continue;

            best = rank;
            chief = uid;
        }

        if (chief == null)
            return;

        var drop = EnsureComp<WFEncounterDeedDropComponent>(chief.Value);
        drop.Ship = grid;
        drop.Deed = deed;
    }

    /// <summary>
    /// Cuts a ship loose from its encounter for good: the encounter forgets it and its passengers, its crew drop their
    /// orders, and no cleanup ever removes it. False if it belongs to no encounter.
    /// </summary>
    public bool Free(EntityUid grid)
    {
        if (!TryComp<WFEncounterGridComponent>(grid, out var marker))
            return false;

        var owner = marker.Encounter;
        var key = marker.Key;
        RemComp<WFEncounterGridComponent>(grid);
        RemComp<WFHoldFireComponent>(grid);
        if (TryComp<WFDockingClampComponent>(grid, out var clamps))
        {
            EntityManager.System<WFDockingClampSystem>().Release((grid, clamps), null);
            RemComp(grid, clamps);
        }
        EnsureComp<CleanupImmuneComponent>(grid);

        var passengers = EntityQueryEnumerator<WFEncounterPassengerComponent>();
        var freed = new List<EntityUid>();
        while (passengers.MoveNext(out var uid, out var passenger))
        {
            if (passenger.Encounter == owner && passenger.Key == key)
                freed.Add(uid);
        }

        foreach (var uid in freed)
        {
            RemComp<WFEncounterPassengerComponent>(uid);
        }

        if (!TryComp<WFEncounterComponent>(owner, out var encounter) || !encounter.Ships.Remove(key, out var ship))
            return true;

        _objectives.Cancel(grid, ship.Group);
        if (encounter.Ships.Count == 0)
            Resolve((owner, encounter), WFEncounterResolution.Completed);
        return true;
    }
}
