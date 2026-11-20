using Robust.Shared.GameStates;

namespace Content.Shared._WF.MappingMode;

/// <summary>
/// A mapping-mode observer. The client reads it as the sign that its player is mapping in a session.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFMappingObserverComponent : Component;
