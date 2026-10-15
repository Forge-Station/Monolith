using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Genetics.Components;

/// <summary>
/// One-use syringe that writes genes into a genome (activator) or strips them (cleaner).
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DnaInjectorComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<ProtoId<GenePrototype>> Genes = new();

    /// <summary>
    /// If true, genes are activated. If false, matching genes are removed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Activate = true;

    [DataField]
    public bool SingleUse = true;

    [DataField]
    public TimeSpan InjectDelay = TimeSpan.FromSeconds(2.5);

    [DataField]
    public bool Used;
}
