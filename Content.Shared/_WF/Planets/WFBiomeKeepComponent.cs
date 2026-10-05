namespace Content.Shared._WF.Planets;

/// <summary>
/// A biome entity that is never unloaded with its chunk: it keeps a clock or a count that a fresh copy would lose.
/// </summary>
[RegisterComponent]
public sealed partial class WFBiomeKeepComponent : Component;
