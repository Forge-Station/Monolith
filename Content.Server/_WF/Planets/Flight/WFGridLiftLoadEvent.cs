namespace Content.Server._WF.Planets.Flight;

/// <summary>Raised on a grid when its pooled lift load is weighed; add any virtual cargo mass to Mass.</summary>
[ByRefEvent]
public record struct WFGridLiftLoadEvent(float Mass);
