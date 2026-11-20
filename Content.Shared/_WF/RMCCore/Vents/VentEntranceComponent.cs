using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Vents;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VentEntranceComponent : Component
{
    [DataField, AutoNetworkedField]
    public SoundSpecifier? EnterSound = new SoundCollectionSpecifier("XenoVentPass");
}
