using Content.Server.GameTicking;
using Content.Server._Forge.Persistence; // Forge-Change

namespace Content.Server.Station.Components;

/// <summary>
///     Added to grids saved in maps to designate that they are the 'main station' grid.
/// </summary>
[RegisterComponent]
[Access(typeof(GameTicker), typeof(WorldPersistFilterSystem))] // Forge-Change
public sealed partial class BecomesStationComponent : Component
{
    /// <summary>
    ///     Mapping only. Should use StationIds in all other
    ///     scenarios.
    /// </summary>
    [DataField("id", required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public string Id = default!;
}
