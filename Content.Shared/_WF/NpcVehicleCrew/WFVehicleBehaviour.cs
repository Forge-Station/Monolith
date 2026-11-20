using Robust.Shared.Serialization;

namespace Content.Shared._WF.NpcVehicleCrew;

/// <summary>What an NPC vehicle crew is doing: its standing orders, or what damage and ammo have driven it to.</summary>
[Serializable, NetSerializable]
public enum WFVehicleBehaviour : byte
{
    /// <summary>Parked at home; the turret engages, the vehicle only moves to get back home.</summary>
    Hold,

    /// <summary>Loops through patrol points round home, pausing at each.</summary>
    Patrol,

    /// <summary>Closes on the target to its standoff distance and fights it.</summary>
    Hunt,

    /// <summary>Follows a lead entity and defends it.</summary>
    Escort,

    /// <summary>Unarmed: drives away from whoever is shooting at it.</summary>
    Flee,

    /// <summary>Badly damaged or out of ammo: drives home, returning fire.</summary>
    Retreat,

    /// <summary>The crew has left the vehicle and fights on foot.</summary>
    Bail,
}
