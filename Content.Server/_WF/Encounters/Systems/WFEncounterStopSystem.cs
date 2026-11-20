using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// Turns NpcCrew's docking events, crewed or simulated, into <see cref="WFEncounterStopReachedEvent"/> when a run's ship
/// docks at one of its route's stops, so NpcCrew never needs to know about encounters. Docks that aren't stops are ignored.
/// </summary>
public sealed class WFEncounterStopSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPilotDockedEvent>(OnPilotDocked);
        SubscribeLocalEvent<WFCrewVirtualDockedEvent>(OnVirtualDocked);
    }

    private void OnPilotDocked(ref WFPilotDockedEvent args)
    {
        Reached(args.Grid, args.TargetGrid);
    }

    private void OnVirtualDocked(ref WFCrewVirtualDockedEvent args)
    {
        Reached(args.Grid, args.TargetGrid);
    }

    private void Reached(EntityUid grid, EntityUid station)
    {
        if (!TryComp<WFEncounterGridComponent>(grid, out var ship)
            || !TryComp<WFEncounterComponent>(ship.Encounter, out var encounter)
            || !encounter.Stops.Contains(station))
            return;

        var ev = new WFEncounterStopReachedEvent(ship.Encounter, grid, station);
        RaiseLocalEvent(ref ev);
    }
}
