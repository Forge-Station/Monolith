using Content.Server.Ghost.Roles.Components;
using Content.Shared.Ghost.Roles.Components;

namespace Content.Server.Ghost.Roles;

/// <summary>RMC's spawner takeover counts, used by the hive core's lesser drone slots.</summary>
public sealed partial class GhostRoleSystem
{
    /// <summary>Sets how many takeovers a spawner offers and re-lists it if slots opened.</summary>
    public void SetAvailable(Entity<GhostRoleMobSpawnerComponent> spawner, int available)
    {
        if (spawner.Comp.AvailableTakeovers == available)
            return;

        spawner.Comp.AvailableTakeovers = available;
        UpdateSpawner(spawner);
    }

    /// <summary>Sets how many of a spawner's takeovers are in use.</summary>
    public void SetCurrent(Entity<GhostRoleMobSpawnerComponent> spawner, int current)
    {
        if (spawner.Comp.CurrentTakeovers == current)
            return;

        spawner.Comp.CurrentTakeovers = current;
        UpdateSpawner(spawner);
    }

    private void UpdateSpawner(Entity<GhostRoleMobSpawnerComponent> spawner)
    {
        if (TryComp(spawner, out GhostRoleComponent? ghostRole))
        {
            ghostRole.Taken = spawner.Comp.CurrentTakeovers >= spawner.Comp.AvailableTakeovers;
            if (ghostRole.Taken)
                UnregisterGhostRole((spawner, ghostRole));
            else
                RegisterGhostRole((spawner, ghostRole));
        }

        UpdateAllEui();
    }
}
