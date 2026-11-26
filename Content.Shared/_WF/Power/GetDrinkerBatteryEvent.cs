using Content.Shared.Power.Components;

namespace Content.Shared._WF.Power;

/// <summary>
/// Raised on a battery drinker to find the battery its charge is stored in.
/// Whichever system owns the drinker's power storage answers it; an unanswered
/// event falls back to the stock silicon cell slot lookup.
/// </summary>
[ByRefEvent]
public record struct GetDrinkerBatteryEvent
{
    public Entity<BatteryComponent>? Battery;
}
