using Content.Shared.Humanoid;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Parasite;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]

public sealed partial class EggPlantTimeComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan PlantTime = TimeSpan.FromSeconds(3.5);
}
