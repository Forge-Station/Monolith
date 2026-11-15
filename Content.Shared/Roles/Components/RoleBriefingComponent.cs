using Content.Shared.Roles;

namespace Content.Shared.Roles.Components;

/// <summary>
/// Adds a briefing to the character info menu, does nothing else.
/// </summary>
[RegisterComponent]
public sealed partial class RoleBriefingComponent : BaseMindRoleComponent
{
    [DataField]
    public string Briefing;
}
