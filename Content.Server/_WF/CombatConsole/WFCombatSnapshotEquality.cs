using System.Linq;
using Content.Shared._Mono.FireControl;
using Content.Shared._WF.CombatConsole;
using Content.Shared.Shuttles.BUIStates;

namespace Content.Server._WF.CombatConsole;

/// <summary>Compares published values without mutating snapshots already owned by a UI.</summary>
internal static class WFCombatSnapshotEquality
{
    public static bool Same(FireControlConsoleBoundInterfaceState first, FireControlConsoleBoundInterfaceState second) =>
        first.Connected == second.Connected && Same(first.FireControllables, second.FireControllables) &&
        Same(first.NavState, second.NavState) && Same(first.Combat, second.Combat);

    private static bool Same(FireControllableEntry[] first, FireControllableEntry[] second)
    {
        if (first.Length != second.Length)
            return false;
        for (var i = 0; i < first.Length; i++)
        {
            var a = first[i];
            var b = second[i];
            if (a.NetEntity != b.NetEntity || !a.Coordinates.Equals(b.Coordinates) || a.Name != b.Name ||
                a.AmmoCount != b.AmmoCount || a.HasManualReload != b.HasManualReload || a.IgnoresLos != b.IgnoresLos)
                return false;
        }
        return true;
    }

    private static bool Same(WFCombatConsoleState first, WFCombatConsoleState second)
    {
        if (first.Automatic != second.Automatic || first.Ammunition != second.Ammunition ||
            first.UnlimitedSupply != second.UnlimitedSupply || first.Threats != second.Threats ||
            first.Cooldown != second.Cooldown || first.Groups.Length != second.Groups.Length ||
            !first.FlareLaunchers.SetEquals(second.FlareLaunchers) ||
            !Same(first.WeaponTypes, second.WeaponTypes) || !Same(first.WeaponSupplies, second.WeaponSupplies))
            return false;
        for (var i = 0; i < first.Groups.Length; i++)
        {
            if (!first.Groups[i].SequenceEqual(second.Groups[i]))
                return false;
        }
        return true;
    }

    private static bool Same(NavInterfaceState first, NavInterfaceState second)
    {
        if (ReferenceEquals(first, second))
            return true;
        if (first.MaxRange != second.MaxRange || !Equals(first.Coordinates, second.Coordinates) ||
            !Equals(first.Angle, second.Angle) || first.DampeningMode != second.DampeningMode ||
            first.MaxIffRange != second.MaxIffRange || first.HideCoords != second.HideCoords ||
            !Same(first.NetworkPortNames, second.NetworkPortNames) || first.Docks.Count != second.Docks.Count)
            return false;
        foreach (var (grid, docks) in first.Docks)
        {
            if (!second.Docks.TryGetValue(grid, out var other) || docks.Count != other.Count)
                return false;
            for (var i = 0; i < docks.Count; i++)
            {
                var a = docks[i];
                var b = other[i];
                if (a.Name != b.Name || !a.Coordinates.Equals(b.Coordinates) || !a.Angle.Equals(b.Angle) ||
                    a.Entity != b.Entity || a.GridDockedWith != b.GridDockedWith || a.LabelName != b.LabelName ||
                    a.RadarColor != b.RadarColor || a.HighlightedRadarColor != b.HighlightedRadarColor ||
                    a.ReceiveOnly != b.ReceiveOnly || a.DockType != b.DockType)
                    return false;
            }
        }
        return true;
    }

    private static bool Same<TKey, TValue>(Dictionary<TKey, TValue> first, Dictionary<TKey, TValue> second) where TKey : notnull
    {
        if (first.Count != second.Count)
            return false;
        foreach (var (key, value) in first)
        {
            if (!second.TryGetValue(key, out var other) || !EqualityComparer<TValue>.Default.Equals(value, other))
                return false;
        }
        return true;
    }
}
