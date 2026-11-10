namespace Content.Shared._WF.NpcCrew;

/// <summary>Size and range bounds enforced by the setup window and re-checked by the server.</summary>
public static class WFCrewLimits
{
    public const int MaxGroup = 32;
    public const int MaxCallsign = 100;
    public const int MaxBattlegroup = 32;
    public const int MaxListItems = 64;
    public const float MaxCoordinate = 100000f;
    public const float MaxRange = 5000f;
    /// <summary>Grids with a larger bounding area in square metres are refused for planning and spawning.</summary>
    public const float MaxPlanArea = 40000f;
}
