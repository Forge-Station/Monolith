using Content.Shared._NF.Cloning;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Genetics.Components;

/// <summary>
/// Structured genome for organic humanoids. Forensic <c>DnaComponent</c> stays the unique identifier string.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GenomeComponent : Component, ITransferredByCloning
{
    /// <summary>
    /// Gene prototype id -> runtime state.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<string, GeneState> Genes = new();

    /// <summary>
    /// Current structural enzyme strand per branch, e.g. AA-FF-ET-HE-CG-AT-TA.
    /// Shared by every gene on that branch.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<GeneBranch, List<string>> Branches = new();

    /// <summary>
    /// Express readout for a branch. Filled when Express is pressed, cleared when a block on that branch is pulsed.
    /// </summary>
    [DataField]
    public Dictionary<GeneBranch, List<byte>> AssemblyHints = new();

    [DataField, AutoNetworkedField]
    public int Instability;

    /// <summary>
    /// Active good/neutral genes past this value start causing collapse rolls.
    /// </summary>
    [DataField]
    public int InstabilityThreshold = 70;

    [DataField]
    public float RadiationAccumulated;

    /// <summary>
    /// Rads that must accumulate before a random mutation attempt.
    /// </summary>
    [DataField]
    public float RadiationThreshold = 15f;

    [DataField]
    public TimeSpan NextInstabilityCheck;
}
