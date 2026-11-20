using Robust.Shared.Serialization;

namespace Content.Shared._WF.SectorControl;

/// <summary>One hex of the sector grid, in axial coordinates around the sector origin.</summary>
[Serializable, NetSerializable]
public readonly record struct WFSectorCell(int Q, int R)
{
    /// <summary>The cell at the sector origin.</summary>
    public static readonly WFSectorCell Origin = new(0, 0);

    /// <summary>The cell one step away in a direction, 0 to 5; see <see cref="WFSectorHex.Directions"/>.</summary>
    public WFSectorCell Neighbour(int direction)
    {
        var step = WFSectorHex.Directions[((direction % 6) + 6) % 6];
        return new WFSectorCell(Q + step.Q, R + step.R);
    }

    public override string ToString()
    {
        return $"({Q}, {R})";
    }
}
