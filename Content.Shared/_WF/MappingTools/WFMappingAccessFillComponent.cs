using Robust.Shared.GameStates;

namespace Content.Shared._WF.MappingTools;

/// <summary>
/// Marks a reader whose access was set before its map was initialised while it takes its access from a container
/// that map init fills (an airlock's electronics). At map init its own access is copied into that reader.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFMappingAccessFillComponent : Component;
