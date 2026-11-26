using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Botany.PlantGrab;

/// <summary>
/// Applied to a mob that has been seized by a carnivorous hydroponics plant.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PlantGrabbedComponent : Component
{
    [DataField]
    public EntProtoId BreakFreeAction = "ActionPlantGrabBreakFree";

    public EntityUid? BreakFreeActionEntity;

    /// <summary>
    /// The hydroponics tray / plant holder that is grabbing this entity.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid Grabber;

    [DataField]
    public float WalkModifier = 0f;

    [DataField]
    public float SprintModifier = 0f;
}
