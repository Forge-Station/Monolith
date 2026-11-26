namespace Content.Shared._Forge.Botany.PlantHealth;

/// <summary>
/// Marker for hydroponics trays/soil so botanical HUD glasses can draw health bars
/// without scanning every appearance entity.
/// </summary>
[RegisterComponent]
public sealed partial class PlantHealthBarTargetComponent : Component;
