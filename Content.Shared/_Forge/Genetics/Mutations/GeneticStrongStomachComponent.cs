using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Genetics.Mutations;

/// <summary>
/// Metabolizes GastroToxin (rotten meat) safely. Does not block acid / xeno toxins.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GeneticStrongStomachComponent : Component;
