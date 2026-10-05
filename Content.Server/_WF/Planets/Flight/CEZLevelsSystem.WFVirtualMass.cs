using Content.Server._WF.Planets.Flight;

namespace Content.Server._CE.ZLevels.Core;

public sealed partial class CEZLevelsSystem
{
    /// <summary>Mass other modules add to a grid's pooled lift load, collected through WFGridLiftLoadEvent.</summary>
    /// <param name="grid">Grid to weigh when no network is supplied.</param>
    /// <param name="networkGrids">Every member of the grid's z-network, when the caller pooled one.</param>
    public float GetWFVirtualMass(EntityUid grid, IReadOnlyCollection<EntityUid>? networkGrids = null)
    {
        if (networkGrids == null)
            return WfLiftLoad(grid);

        var mass = 0f;

        foreach (var member in networkGrids)
        {
            mass += WfLiftLoad(member);
        }

        return mass;
    }

    /// <summary>Virtual cargo mass other modules report for one grid.</summary>
    private float WfLiftLoad(EntityUid grid)
    {
        var ev = new WFGridLiftLoadEvent(0f);
        RaiseLocalEvent(grid, ref ev);
        return ev.Mass;
    }
}
