using Content.Shared.Weapons.Ranged.Components;

namespace Content.Server._DV.Execution;

// The "are you sure?" seam of the gun Execute verb: the verb asks first, a yes comes back through here.
public sealed partial class ExecutionSystem
{
    private bool _wolfmedConfirmed;

    /// <summary>
    /// Starts the gun Execute do-after once the executor has said yes. False when the verb's own checks no longer pass.
    /// </summary>
    public bool StartConfirmedExecution(Entity<GunComponent> weapon, EntityUid victim, EntityUid attacker)
    {
        if (!CanExecuteWithGun(weapon, victim, attacker))
            return false;

        _wolfmedConfirmed = true;
        try
        {
            TryStartGunExecutionDoafter(weapon, victim, attacker);
        }
        finally
        {
            _wolfmedConfirmed = false;
        }

        return true;
    }
}
