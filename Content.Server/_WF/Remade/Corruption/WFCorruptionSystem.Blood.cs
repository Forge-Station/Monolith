using Content.Server._WF.Remade.Director;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Remade.Corruption;

/// <summary>Blood on corruption is slowly drunk, so the puddle shrinks, and each unit feeds the Director of the tile's Monolith a little biomass.</summary>
public sealed partial class WFCorruptionSystem
{
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    private readonly List<(EntityUid Puddle, Entity<WFCorruptionComponent> Tile)> _drinking = new();

    private void TickBlood()
    {
        if (_gridTiles.Count == 0)
            return;

        _drinking.Clear();
        var puddles = EntityQueryEnumerator<PuddleComponent, TransformComponent>();
        while (puddles.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } gridUid
                || !_gridTiles.ContainsKey(gridUid)
                || !TryComp<MapGridComponent>(gridUid, out var grid)
                || FindCorruption((gridUid, grid), _map.TileIndicesFor(gridUid, grid, xform.Coordinates)) is not { } tile
                || tile.Comp.DecayAt != null
                || tile.Comp.Drinks.Count == 0)
            {
                continue;
            }

            _drinking.Add((uid, tile));
        }

        foreach (var (puddle, tile) in _drinking)
        {
            if (!Deleted(puddle))
                Drink(puddle, tile);
        }
    }

    /// <summary>Takes up to a tick's drink of blood from a puddle and pays the Monolith's Director for it.</summary>
    private void Drink(EntityUid puddle, Entity<WFCorruptionComponent> tile)
    {
        if (!TryComp<PuddleComponent>(puddle, out var puddleComp)
            || !_solutions.TryGetSolution(puddle, puddleComp.SolutionName, out var soln, out var solution))
        {
            return;
        }

        var budget = FixedPoint2.New(tile.Comp.DrinkRate * (float) TickRate.TotalSeconds);
        var drunk = FixedPoint2.Zero;
        foreach (var reagent in solution.Contents.ToArray())
        {
            if (budget <= drunk)
                break;

            if (!tile.Comp.Drinks.Contains(reagent.Reagent.Prototype))
                continue;

            var take = FixedPoint2.Min(reagent.Quantity, budget - drunk);
            if (take > FixedPoint2.Zero && _solutions.RemoveReagent(soln.Value, reagent.Reagent, take) > FixedPoint2.Zero)
                drunk += take;
        }

        if (drunk > FixedPoint2.Zero && TryComp<WFRemadeDirectorComponent>(tile.Comp.Source, out var director))
            director.Biomass += drunk.Float() * tile.Comp.BiomassPerUnit;
    }
}
