using Content.Shared._WF.Shuttles;

namespace Content.Shared.RCD.Systems;

public partial class RCDSystem
{
    /// <summary>Records successful RCD removal separately from damage or arbitrary entity deletion.</summary>
    private void WfRecordHullDeconstruction(EntityUid target)
    {
        var ev = new WFHullDeconstructedEvent();
        RaiseLocalEvent(target, ref ev);
    }

    /// <summary>Records an RCD removing the last floor or lattice at a location, which is a design change, not damage.</summary>
    private void WfRecordHullTileDeconstruction(EntityUid grid, Vector2i index)
    {
        var ev = new WFHullTileDeconstructedEvent(index);
        RaiseLocalEvent(grid, ref ev);
    }
}
