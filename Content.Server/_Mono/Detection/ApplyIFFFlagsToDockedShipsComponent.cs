using Content.Shared.Shuttles.Components;

namespace Content.Server._Mono.Detection;

/// <summary>While a ship is docked to this grid, the grid's IFF flags are added to the ship.</summary>
[RegisterComponent]
public sealed partial class ApplyIFFFlagsToDockedShipsComponent : Component
{
    [DataField(required: true)]
    public IFFFlags Flags;
}
