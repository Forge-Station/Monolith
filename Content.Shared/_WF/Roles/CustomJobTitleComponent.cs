using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Roles;

/// <summary>On a mind: the custom title its job goes by. Set at spawn, ignored once the mind has another job.</summary>
[RegisterComponent]
public sealed partial class CustomJobTitleComponent : Component
{
    [DataField]
    public ProtoId<JobPrototype> Job;

    [DataField]
    public string Title = string.Empty;
}
