using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Pulling;

/// <summary>
/// If set, this entity will be immune to ParalyzeOnPullAttempt from entities matching the whitelist.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(RMCPullingSystem))]
public sealed partial class ParalyzeImmuneFromComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityWhitelist? Whitelist;
}
