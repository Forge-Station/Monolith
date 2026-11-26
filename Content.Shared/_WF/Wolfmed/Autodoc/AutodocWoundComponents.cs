using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Autodoc;

/// <summary>
/// A wound the pod made itself: the incision it opened, the suture that closed it, the residue a tend pass
/// leaves behind. The planner does not count these as work, so a pod that has just operated does not read
/// its own handiwork as a new problem and start again.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedPodWoundComponent : Component;
