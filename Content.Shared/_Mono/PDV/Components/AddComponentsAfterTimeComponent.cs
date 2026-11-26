using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Shared._Mono.PDV.Components;

/// <summary>
/// After a delay, and only on a coin flip, adds the listed components.
/// Members of an excluded company are left alone.
/// </summary>
[RegisterComponent]
public sealed partial class AddComponentsAfterTimeComponent : Component
{
    /// <summary>
    /// The probability that this component will remove itself immediately.
    /// </summary>
    [DataField]
    public float SelectionProbability = 0.5f;

    [DataField]
    public TimeSpan MinSelectionTime = TimeSpan.FromMinutes(30);

    [DataField]
    public TimeSpan MaxSelectionTime = TimeSpan.FromMinutes(270);

    [DataField]
    public TimeSpan SelectionTime;

    [DataField(required: true)]
    public ComponentRegistry AddComponentsOnSelection = default!;

    [DataField]
    public List<ProtoId<CompanyPrototype>> ExcludedCompanies = new();
}
