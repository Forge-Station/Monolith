namespace Content.Server._Forge.Drones.Components;

/// <summary>
/// Marks a procedural drone AI core that is fleeing the inner worldgen zone toward bluespace deletion.
/// </summary>
[RegisterComponent]
public sealed partial class DroneInnerZoneFleeComponent : Component
{
    /// <summary>
    /// When the drone grid should be deleted after its BSS jump.
    /// </summary>
    [ViewVariables]
    public TimeSpan DeleteAt;

    /// <summary>
    /// How far ahead of the current position to place the flee navigation target.
    /// </summary>
    [ViewVariables]
    public float FleeTargetDistance = 50_000f;

    /// <summary>
    /// Whether flee steering has been configured for this entity.
    /// </summary>
    [ViewVariables]
    public bool SteeringConfigured;
}
