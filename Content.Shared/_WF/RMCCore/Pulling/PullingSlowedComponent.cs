using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Pulling;

[RegisterComponent, NetworkedComponent]
[Access(typeof(RMCPullingSystem))]
public sealed partial class PullingSlowedComponent : Component;
