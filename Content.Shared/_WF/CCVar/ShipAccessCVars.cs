using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>Settings for ship access (owner and allow list).</summary>
[CVarDefs]
public sealed class ShipAccessCVars
{
    /// <summary>Whether a freshly bought ship starts locked, so only its owner and allow list can open it.</summary>
    public static readonly CVarDef<bool> LockNewShips =
        CVarDef.Create("wf.shipaccess.lock_new_ships", true, CVar.SERVERONLY);
}
