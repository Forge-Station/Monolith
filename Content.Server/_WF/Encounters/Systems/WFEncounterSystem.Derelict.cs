using Content.Server._WF.Encounters.Components;
using Content.Shared._WF.Encounters;
using System.Linq;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._WF.Encounters.Systems;

public sealed partial class WFEncounterSystem
{
    /// <summary>
    /// Makes a hulk of a freshly spawned hull: no crew and no fuel, litter across its decks, squatters living in it
    /// and their chief, who carries the claim to the ship.
    /// </summary>
    private void Wreck(WFEncounterShipState state, WFEncounterDerelict derelict, string vessel)
    {
        state.Derelict = true;
        LeaveStranded(state.Grid, WFEncounterStranding.Fuel);

        // Every tile a mob can stand on, nearest the middle of the ship first.
        var tiles = _planner.HoldTiles(state.Grid, int.MaxValue);
        if (tiles.Count == 0)
            return;

        if (derelict.King is { } kingId)
        {
            var king = Spawn(kingId, tiles[0]);
            var drop = EnsureComp<WFSalvageClaimDropComponent>(king);
            drop.Ship = state.Grid;
            drop.Claim = derelict.Claim;
            drop.Vessel = vessel;
            drop.Resale = derelict.Resale;
        }

        for (var i = 0; i < derelict.Dwellers && derelict.DwellerPool.Count > 0; i++)
        {
            Spawn(_random.Pick(derelict.DwellerPool), _random.Pick(tiles));
        }

        for (var i = 0; i < derelict.Debris && derelict.DebrisPool.Count > 0; i++)
        {
            var at = _random.Pick(tiles);
            Spawn(_random.Pick(derelict.DebrisPool), new EntityCoordinates(at.EntityId, at.Position + _random.NextVector2(0.35f)));
        }
    }

    /// <summary>
    /// A ship that has strayed past the encounter's leash breaks off and flies back towards where the encounter was
    /// placed, then takes up the rest of its orders again. Ships chasing each other would otherwise carry their fight
    /// off into empty space.
    /// </summary>
    private void Recall(WFEncounterComponent encounter, WFEncounterShipState ship)
    {
        var here = _transform.GetMapCoordinates(ship.Grid);
        if (here.MapId != encounter.Origin.MapId)
            return;

        var distance = (here.Position - encounter.Origin.Position).Length();
        if (ship.Recalled)
        {
            // Back inside, or the leg is flown: it is on its own orders again.
            if (distance <= encounter.Leash * 0.5f || CurrentOrder(ship) != WFCrewObjectiveKind.GoTo)
                ship.Recalled = false;
            return;
        }

        if (distance <= encounter.Leash)
            return;

        // With nothing left to fly it is going nowhere further.
        if (!_objectives.TryGetQueue(ship.Grid, ship.Group, out var rest) || rest.Count == 0)
            return;

        var queue = new List<WFCrewObjective>(rest);
        queue.Insert(0, new WFCrewObjective
        {
            Kind = WFCrewObjectiveKind.GoTo,
            Position = encounter.Origin.Position,
            Range = encounter.Leash * 0.4f,
        });
        if (_objectives.SetQueue(ship.Grid, ship.Group, queue))
            ship.Recalled = true;
    }

    /// <summary>
    /// A ship somebody has claimed is theirs from then on: it leaves its encounter, which is over, and is never
    /// cleaned up with it.
    /// </summary>
    public void Release(EntityUid grid)
    {
        if (!TryComp<WFEncounterGridComponent>(grid, out var marker))
            return;

        var owner = marker.Encounter;
        var key = marker.Key;
        RemComp<WFEncounterGridComponent>(grid);
        if (!TryComp<WFEncounterComponent>(owner, out var encounter))
            return;

        encounter.Ships.Remove(key);
        Resolve((owner, encounter), WFEncounterResolution.Completed);
    }
}
