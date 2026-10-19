using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Storage;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(RMCStorageSystem))]
public sealed partial class BlockEntityStorageComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityWhitelist? Whitelist;
}
