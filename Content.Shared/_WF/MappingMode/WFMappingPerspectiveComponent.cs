namespace Content.Shared._WF.MappingMode;

/// <summary>
/// Client-side marker on a mapping level: the z-level renderer draws it with the planet perspective, each level below
/// smaller. Added and removed by the mapping screen's Depth perspective layer; never networked.
/// </summary>
[RegisterComponent]
public sealed partial class WFMappingPerspectiveComponent : Component;
