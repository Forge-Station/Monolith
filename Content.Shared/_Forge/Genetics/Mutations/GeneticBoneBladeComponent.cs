using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Genetics.Mutations;

/// <summary>
/// Marker for a genetically grown bone blade that must stay attached to its owner.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GeneticBoneBladeComponent : Component;
