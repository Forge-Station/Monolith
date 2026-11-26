using Robust.Shared.GameStates;

namespace Content.Goobstation.Shared.StationRadio.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RadioRigComponent : Component
{
    /// <summary>
    /// Frequency used when this rig broadcasts music to station radios.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Frequency = 1285;
}
