using Robust.Shared.Map;

namespace Content.Server._WF.DroneRings;

/// <summary>Raised, by ref and broadcast, before world generation spawns a debris feature; set Cancelled to skip it.</summary>
[ByRefEvent]
public record struct WFDebrisPlacingEvent(string Proto, MapCoordinates At)
{
    public bool Cancelled;
}
