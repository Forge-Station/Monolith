using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared._Forge.Clothing.EntitySystems;
using Content.Shared.Inventory;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Audio;

namespace Content.Shared._Forge.Clothing.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(ClothingStyleCyclerSystem))]
public sealed partial class ClothingStyleCyclerComponent : Component
{
    /// <summary>
    /// Action for cycle any clothing (e.g., change the scarf left, right or untied)
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntProtoId Action = "ActionCycleClothingStyle";

    /// <summary>
    /// Action for cycle any clothing
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    /// Key slot in ClothingComponent.ClothingVisuals, switchable ("neck", "jumpsuit", and t.d. :D).
    [DataField(required: true)]
    public string Slot = string.Empty;

    /// Map-key in clothingVisuals[Slot]
    [DataField(required: true)]
    public string MapKey = string.Empty;

    /// Slots, in action required. Default — slots: Clothing.
    [DataField]
    public SlotFlags RequiredSlots = SlotFlags.NONE;

    /// List styles for cycle switch.
    [DataField]
    public List<ClothingStyleEntry> States = new();

    /// Allow cycle to button (action-button).
    [DataField]
    public bool CycleViaAction = true;

    /// Allow cycle with Alt-click (AlternativeVerb).
    [DataField]
    public bool CycleViaAltVerb = true;

    [DataField, AutoNetworkedField]
    public int CurrentIndex;

    [DataField, AutoNetworkedField]
    public TimeSpan CycleDelay = TimeSpan.FromSeconds(2);

    [DataField]
    public SoundSpecifier? CycleSound;
}

[DataDefinition]
public partial struct ClothingStyleEntry
{
    [DataField(required: true)]
    public string State;

    [DataField]
    public Color? Color;
}
public sealed partial class CycleClothingStyleEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class ClothingStyleCycleDoAfterEvent : SimpleDoAfterEvent;
