using Content.Shared.Actions;
using Content.Shared._Forge.Clothing.EntitySystems;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Clothing.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(ScarfStyleCyclerSystem))]
public sealed partial class ScarfStyleCyclerComponent : Component
{
    /// <summary>
    /// Action for cycle a scarf (e.g., change the scarf left, right or untied)
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntProtoId Action = "ActionCycleScarfStyle";

    /// <summary>
    /// Action for cycle a scarf
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    /// <summary>
    /// Prefixes cycles by action; "" = default "equipped-NECK" state.
    /// </summary>
    [DataField]
    public List<string> Prefixes = new() { "" };

    [DataField, AutoNetworkedField]
    public int CurrentIndex;
}

/// <summary>
/// Event raised by ActionCycleScarfStyle (InstantAction).
/// </summary>
public sealed partial class CycleScarfEvent : InstantActionEvent;
