namespace Content.Server._WF.Planets.Flight;

/// <summary>Raised on a grid when its pooled lift load is weighed; add any virtual cargo mass to Mass.</summary>
[ByRefEvent]
public record struct WFGridLiftLoadEvent(float Mass);

/// <summary>
/// Raised broadcast when the descent warning names low lift; a module that knows how to restore it, such as by
/// dropping cargo, sets <c>Hint</c>. <c>Hull</c> and <c>Load</c> are the rigid set's hull mass and virtual cargo mass,
/// as lift weighs them.
/// </summary>
[ByRefEvent]
public record struct WFLiftWarningEvent(EntityUid Grid, float Ratio, float FullRatio, float Hull, float Load)
{
    public string? Hint;
}
