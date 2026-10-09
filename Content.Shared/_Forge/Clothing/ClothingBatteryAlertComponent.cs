using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Clothing;

/// <summary>
/// Shows a battery charge alert on the clothing wearer, using the slotted power cell.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ClothingBatteryAlertComponent : Component
{
    [DataField]
    public ProtoId<AlertPrototype> BatteryAlert = "HardsuitBattery";

    [DataField]
    public ProtoId<AlertPrototype> NoBatteryAlert = "HardsuitBatteryNone";

    [ViewVariables]
    public EntityUid? Wearer;
}
