using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Vents;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VentExitComponent : Component
{
    [DataField, AutoNetworkedField]
    public SoundSpecifier? ExitSound = new SoundCollectionSpecifier("XenoVentPass");
}
