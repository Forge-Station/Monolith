namespace Content.Shared._Forge.Drones;

/// <summary>
/// On a station grid: procedural drones that come this close flee and despawn, the same way they leave the inner zone.
/// </summary>
[RegisterComponent]
public sealed partial class DeleteNearbyDronesComponent : Component
{
    [DataField]
    public float Distance = 4000f;
}
