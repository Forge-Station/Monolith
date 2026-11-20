using System.Linq;
using Content.Server._WF.Encounters.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// Battles between sides: a combatant that has flown its attack while the other side still fights is sent at the
/// nearest enemy left, so no ship is left holding station over a wreck with the fight going on beyond it.
/// </summary>
public sealed partial class WFEncounterSystem
{
    /// <summary>Gives every idle combatant of an undecided battle a fresh attack on the nearest ship of another side.</summary>
    private void Retarget(Entity<WFEncounterComponent> encounter)
    {
        var comp = encounter.Comp;
        if (comp.Resolution != null || !_prototypes.TryIndex(comp.Prototype, out var prototype))
            return;

        foreach (var (key, ship) in comp.Ships)
        {
            // Flown to the end, or skipped to it after its target was lost with all hands.
            if (!ship.HasOrders || ship.Derelict || TerminatingOrDeleted(ship.Grid) || IsStranded(ship) || !InFight(ship)
                || !ship.Flown && _objectives.QueueStatus(ship.Grid, ship.Group) != "complete")
                continue;

            // Only a ship whose orders were to attack takes the fight up again; an escort or a trader holds its station.
            var orders = prototype.Ships.FirstOrDefault(other => other.Key == key)?.Objectives
                .FirstOrDefault(objective => objective.Kind == WFCrewObjectiveKind.Attack);
            if (orders == null || NearestEnemy(comp, ship) is not { } enemy)
                continue;

            // Orders the crew won't take leave the ship as it was, to be looked at again next poll.
            if (!_objectives.SetQueue(ship.Grid, ship.Group, new List<WFCrewObjective>
                {
                    new() { Kind = WFCrewObjectiveKind.Attack, Target = GetNetEntity(enemy.Grid), Range = orders.Range },
                }))
                continue;

            Log.Info($"Encounter ship {ToPrettyString(ship.Grid)} has flown its attack and turns on {ToPrettyString(enemy.Grid)}.");
            ship.Flown = false;
        }
    }

    /// <summary>The nearest ship of another side still in the fight, on the same map.</summary>
    private WFEncounterShipState? NearestEnemy(WFEncounterComponent comp, WFEncounterShipState ship)
    {
        var map = Transform(ship.Grid).MapID;
        var here = _transform.GetWorldPosition(ship.Grid);
        WFEncounterShipState? nearest = null;
        var best = float.MaxValue;
        foreach (var other in comp.Ships.Values)
        {
            if (other == ship || other.Side == ship.Side || other.Derelict || TerminatingOrDeleted(other.Grid) || !InFight(other)
                || Transform(other.Grid).MapID != map)
                continue;

            var distance = (_transform.GetWorldPosition(other.Grid) - here).LengthSquared();
            if (distance >= best)
                continue;

            best = distance;
            nearest = other;
        }

        return nearest;
    }
}
