using Content.Server.Ghost.Roles.Components;

namespace Content.Server.Ghost.Roles;

public sealed partial class GhostRoleSystem
{
    public void ReregisterGhostRole(Entity<GhostRoleComponent> role)
    {
        role.Comp.Taken = false;
        RegisterGhostRole(role);
    }
}
