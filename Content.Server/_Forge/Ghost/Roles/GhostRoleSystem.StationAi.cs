using Content.Server.Ghost.Roles.Components;

namespace Content.Server.Ghost.Roles;

public sealed partial class GhostRoleSystem
{
    public void ReregisterGhostRole(Entity<GhostRoleComponent> role)
    {
        if (!role.Comp.ReregisterOnGhost)
            return;

        role.Comp.Taken = false;
        RegisterGhostRole(role);
    }
}
