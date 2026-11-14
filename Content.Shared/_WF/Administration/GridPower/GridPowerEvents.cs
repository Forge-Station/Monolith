using Robust.Shared.Serialization;

namespace Content.Shared._WF.Administration.GridPower;

/// <summary>
/// Power summary for one grid in the admin Grid Power window.
/// </summary>
[Serializable, NetSerializable]
public sealed class GridPowerInfo
{
    public NetEntity Grid;
    public string Name = string.Empty;
    public string Map = string.Empty;
    public int Apcs;
    public int ApcsOn;
    public int Batteries;
    public float Charge;
    public float MaxCharge;
}

/// <summary>
/// Client asks the server for the grid power list. Admin-only; ignored otherwise.
/// </summary>
[Serializable, NetSerializable]
public sealed class GridPowerListRequestEvent : EntityEventArgs;

/// <summary>
/// Every grid that has APCs or network batteries.
/// </summary>
[Serializable, NetSerializable]
public sealed class GridPowerListEvent : EntityEventArgs
{
    public List<GridPowerInfo> Grids;

    public GridPowerListEvent(List<GridPowerInfo> grids)
    {
        Grids = grids;
    }
}
