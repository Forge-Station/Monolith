using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.MilitaryVehicles;

/// <summary>Jobs whose players start the shift with a licence, made out to them.</summary>
[Prototype("wfLicenceIssue")]
public sealed partial class WFLicenceIssuePrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public EntProtoId Licence;

    [DataField(required: true)]
    public List<ProtoId<JobPrototype>> Jobs = new();
}
