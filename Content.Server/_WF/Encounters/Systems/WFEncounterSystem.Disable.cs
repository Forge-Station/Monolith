using Content.Server._WF.Encounters.Components;
using Content.Shared._WF.NpcCrew;

namespace Content.Server._WF.Encounters.Systems;

public sealed partial class WFEncounterSystem
{
    [Dependency] private WFShipDisablerSystem _disablers = default!;

    /// <summary>Lays a raider's disabling guns on the ship it is closing on to board, and stands them down otherwise.</summary>
    private void AimDisablers(WFEncounterShipState ship, bool begun)
    {
        _disablers.SetTarget(ship.Grid, begun ? DisableTarget(ship) : null);
    }

    /// <summary>
    /// The ship a raider is closing on or docked with for its run: an abductor's mark until the grab is over, a
    /// boarder's or boarding extorter's prey while it docks and loots. Never a ship berthed at a station.
    /// </summary>
    private EntityUid? DisableTarget(WFEncounterShipState ship)
    {
        EntityUid? target = null;
        if (ship.Abduct is { Phase: WFAbductPhase.Close or WFAbductPhase.Dock or WFAbductPhase.Grab } abduct)
            target = abduct.Mark;
        else if (ship.Abduct == null && ship.Extort is null or { Phase: WFExtortPhase.Board }
            && CurrentOrder(ship) is null or WFCrewObjectiveKind.Loot or WFCrewObjectiveKind.Hold)
            target = ship.Prey;

        if (target is not { } mark || TerminatingOrDeleted(mark))
            return null;

        return !_docking.AreGridsDocked(ship.Grid, mark) && DockedAtStation(mark) ? null : mark;
    }
}
