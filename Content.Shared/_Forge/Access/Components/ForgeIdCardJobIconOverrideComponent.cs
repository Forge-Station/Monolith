using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Access.Components;

/// <summary>
/// Overrides the job icon on an ID card without changing its preset job (access, title, playtime role).
/// </summary>
[RegisterComponent]
public sealed partial class ForgeIdCardJobIconOverrideComponent : Component
{
    [DataField(required: true)]
    public ProtoId<JobIconPrototype> JobIcon;
}
