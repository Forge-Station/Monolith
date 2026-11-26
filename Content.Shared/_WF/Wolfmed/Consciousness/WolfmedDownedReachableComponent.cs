using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Consciousness;

/// <summary>
/// An exception to "self only" while Downed: a body on the floor may still reach this. The autodoc carries it
/// so that crawling into a surgical pod is possible from exactly the state that most needs one.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedDownedReachableComponent : Component;
