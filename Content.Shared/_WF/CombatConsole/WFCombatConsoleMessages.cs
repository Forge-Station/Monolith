using Content.Shared._Mono.ShipGuns;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.CombatConsole;

/// <summary>Authoritative fire-group and countermeasure telemetry for a gunnery console.</summary>
[Serializable, NetSerializable]
public sealed class WFCombatConsoleState
{
    /// <summary>Saved weapon memberships available on the current server.</summary>
    public List<NetEntity>[] Groups = new List<NetEntity>[WFWeaponGroups.Count];
    /// <summary>Weapon categories resolved on the server, including weapons outside client visibility.</summary>
    public Dictionary<NetEntity, ShipGunType> WeaponTypes = new();
    /// <summary>Supply types and capacities resolved outside client visibility.</summary>
    public Dictionary<NetEntity, WFWeaponSupply> WeaponSupplies = new();
    /// <summary>Countermeasure launchers excluded from offensive weapon selections.</summary>
    public HashSet<NetEntity> FlareLaunchers = new();
    /// <summary>Whether missile-reactive countermeasures are armed.</summary>
    public bool Automatic;
    /// <summary>Total stored rounds in connected launchers.</summary>
    public int Ammunition;
    /// <summary>Whether a connected launcher has an automatic infinite ammunition provider.</summary>
    public bool UnlimitedSupply;
    /// <summary>Number of eligible nearby missile locks on this ship.</summary>
    public int Threats;
    /// <summary>Seconds until the earliest supplied launcher can dispense.</summary>
    public float Cooldown;

    /// <summary>Initializes every group slot for serialization and rendering.</summary>
    public WFCombatConsoleState()
    {
        for (var i = 0; i < Groups.Length; i++)
            Groups[i] = new List<NetEntity>();
    }
}

/// <summary>Saves the current weapon selection into a console's group slot.</summary>
[Serializable, NetSerializable]
public sealed class WFSaveWeaponGroupMessage(int slot, List<NetEntity> weapons) : BoundUserInterfaceMessage
{
    /// <summary>Zero-based group slot to replace.</summary>
    public int Slot = slot;
    /// <summary>Requested selection, filtered against the server before resolving entities.</summary>
    public List<NetEntity> Weapons = weapons;
}

/// <summary>Arms or safes the console's automatic countermeasures.</summary>
[Serializable, NetSerializable]
public sealed class WFAutomaticFlaresMessage(bool enabled) : BoundUserInterfaceMessage
{
    /// <summary>Requested armed state.</summary>
    public bool Enabled = enabled;
}

/// <summary>Requests one countermeasure burst from the connected flare launchers.</summary>
[Serializable, NetSerializable]
public sealed class WFDispenseFlaresMessage : BoundUserInterfaceMessage;

/// <summary>Bounds and filters weapon-group requests against the server's available weapons.</summary>
public static class WFWeaponGroups
{
    /// <summary>Number of console-local weapon group slots.</summary>
    public const int Count = 4;
    /// <summary>Maximum selection size accepted from one request.</summary>
    public const int MaximumWeapons = 256;

    /// <summary>Rejects invalid slots and oversized requests before resolving any entities.</summary>
    public static bool IsValidRequest(int slot, int count) =>
        slot >= 0 && slot < Count && count >= 0 && count <= MaximumWeapons;

    /// <summary>Removes duplicates, disconnected weapons and countermeasures from a saved group.</summary>
    public static HashSet<T> Filter<T>(IEnumerable<T> requested, HashSet<T> available, HashSet<T> flares)
        where T : notnull
    {
        var result = new HashSet<T>();
        foreach (var weapon in requested)
        {
            if (available.Contains(weapon) && !flares.Contains(weapon))
                result.Add(weapon);
        }
        return result;
    }
}
