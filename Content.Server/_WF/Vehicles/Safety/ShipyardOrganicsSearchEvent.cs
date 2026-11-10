namespace Content.Server._WF.Vehicles.Safety;

/// <summary>Raised on each entity aboard a hull by the shipyard's organics check, so a vehicle can name its occupants.</summary>
[ByRefEvent]
public record struct ShipyardOrganicsSearchEvent
{
    /// <summary>Set to the name of a living, sapient being found; stops the search.</summary>
    public string? Name;
}
