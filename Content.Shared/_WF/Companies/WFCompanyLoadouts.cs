using Content.Shared._WF.RoundSetup;
using Content.Shared.Clothing;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Companies;

/// <summary>The loadouts a company may issue its ranks: anything a ship crew job can pick in the character loadout.</summary>
public static class WFCompanyLoadouts
{
    /// <summary>Every loadout in a crew job's visible loadout groups, each once, with the first group it was found in.</summary>
    public static List<(LoadoutGroupPrototype Group, LoadoutPrototype Loadout)> Catalogue(IPrototypeManager prototypes)
    {
        var result = new List<(LoadoutGroupPrototype, LoadoutPrototype)>();
        var groups = new List<ProtoId<LoadoutGroupPrototype>>();
        foreach (var job in WFShipCrewJobs.Jobs)
        {
            if (prototypes.TryIndex<RoleLoadoutPrototype>(LoadoutSystem.GetJobPrototype(job), out var role))
                groups.AddRange(role.Groups);
        }
        var seenGroups = new HashSet<string>();
        var seen = new HashSet<string>();
        for (var i = 0; i < groups.Count; i++)
        {
            var groupId = groups[i];
            if (!seenGroups.Add(groupId) || !prototypes.TryIndex(groupId, out var group) || group.Hidden)
                continue;
            foreach (var loadout in group.Loadouts)
            {
                if (seen.Add(loadout) && prototypes.TryIndex(loadout, out var proto))
                    result.Add((group, proto));
            }
            groups.AddRange(group.Subgroups);
        }
        return result;
    }

    /// <summary>The ids of every loadout in the <see cref="Catalogue"/>.</summary>
    public static HashSet<string> AllowedIds(IPrototypeManager prototypes)
    {
        var ids = new HashSet<string>();
        foreach (var (_, loadout) in Catalogue(prototypes))
            ids.Add(loadout.ID);
        return ids;
    }

    /// <summary>Every entity a loadout hands out: its own gear and its starting gear's.</summary>
    public static List<EntProtoId> Items(IPrototypeManager prototypes, LoadoutPrototype loadout)
    {
        var items = new List<EntProtoId>();
        Add(loadout);
        if (loadout.StartingGear is { } gear && prototypes.TryIndex(gear, out var starting))
            Add(starting);
        return items;

        void Add(IEquipmentLoadout source)
        {
            items.AddRange(source.Equipment.Values);
            items.AddRange(source.Inhand);
            foreach (var stored in source.Storage.Values)
                items.AddRange(stored);
        }
    }
}
