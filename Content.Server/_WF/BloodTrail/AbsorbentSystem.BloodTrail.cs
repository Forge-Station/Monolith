using System.Numerics;
using Content.Server._WF.Cleaning;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Timing.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.Fluids.EntitySystems;

public sealed partial class AbsorbentSystem
{
    [Dependency] private WFDecalCleaningSystem _decalCleaning = default!;

    /// <summary>
    /// A mop used on bare floor washes the cleanable decals off that tile: footprints, drag marks, blood and dirt
    /// lie where there is no puddle to mop. Returns true if the mop did anything.
    /// </summary>
    private bool TryMopFloor(EntityUid user, EntityUid used, AbsorbentComponent absorber, EntityCoordinates location)
    {
        if (_transform.GetGrid(location) is not { } gridUid || !TryComp(gridUid, out MapGridComponent? grid))
            return false;

        var tile = _mapSystem.GetTileRef(gridUid, grid, location);
        if (!_decalCleaning.HasCleanableDecals(gridUid, grid, tile.GridIndices))
            return false;

        if (!_solutionContainerSystem.TryGetSolution(used, AbsorbentComponent.SolutionName, out var absorberSoln))
            return false;

        if (TryComp<UseDelayComponent>(used, out var useDelay) && _useDelay.IsDelayed((used, useDelay)))
            return true;

        var solution = absorberSoln.Value.Comp.Solution;
        var absorbents = _puddleSystem.GetAbsorbentReagents(solution);
        var available = solution.GetTotalPrototypeQuantity(absorbents);
        if (available == FixedPoint2.Zero)
        {
            _popups.PopupEntity(Loc.GetString("mopping-system-no-water", ("used", used)), user, user);
            return true;
        }

        // The water goes over the tile as it does when mopping a puddle, and what the floor didn't take comes back.
        var water = solution.SplitSolutionWithOnly(FixedPoint2.Min(available, absorber.PickupAmount), absorbents);
        _puddleSystem.DoTileReactions(tile, water);
        // Water only washes blood off by itself; the scrubbing takes the rest.
        var scrubbed = _decalCleaning.CleanTile(gridUid, grid, tile.GridIndices, water.Volume.Float());
        water.RemoveSolution(FixedPoint2.New(scrubbed));
        _solutionContainerSystem.AddSolution(absorberSoln.Value, water);
        _solutionContainerSystem.UpdateChemicals(absorberSoln.Value);

        _audio.PlayPvs(absorber.PickupSound, location);
        if (useDelay != null)
            _useDelay.TryResetDelay((used, useDelay));

        var userXform = Transform(user);
        var localPos = Vector2.Transform(_transform.ToMapCoordinates(location).Position, _transform.GetInvWorldMatrix(userXform));
        _melee.DoLunge(user, used, Angle.Zero, userXform.LocalRotation.RotateVec(localPos), null, false);
        return true;
    }
}
