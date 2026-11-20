using Content.Server._Mono.Cargo;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Robust.Shared.Containers;

namespace Content.Server.Cargo.Systems;

public sealed partial class CargoSystem
{
    /// <summary>Warns at the console and returns true if the goods break a <see cref="SoldAlertComponent.MaxPerSale"/>.</summary>
    private bool WfRefuseOverLimit(EntityUid console, HashSet<EntityUid> goods)
    {
        Dictionary<string, (int Count, int Limit, string Name)>? groups = null;

        foreach (var ent in goods)
        {
            if (!TryComp<SoldAlertComponent>(ent, out var alert))
                continue;

            var key = alert.GroupId ?? MetaData(ent).EntityPrototype?.ID;
            if (key == null)
                continue;

            groups ??= new();
            var (count, limit, name) = groups.GetValueOrDefault(key, (0, 0, MetaData(ent).EntityName));
            count += TryComp<StackComponent>(ent, out var stack) ? stack.Count : 1;

            // Entities of a group should agree; if they don't, the lowest limit holds whatever the iteration order.
            if (alert.MaxPerSale > 0 && (limit == 0 || alert.MaxPerSale < limit))
                limit = alert.MaxPerSale;

            groups[key] = (count, limit, name);
        }

        if (groups == null)
            return false;

        foreach (var (count, limit, name) in groups.Values)
        {
            if (limit == 0 || count <= limit)
                continue;

            _popup.PopupEntity(Loc.GetString("cargo-sold-alert-console", ("item", name), ("limit", limit)), console, PopupType.MediumCaution);
            return true;
        }

        return false;
    }

    /// <summary>The goods plus everything inside them, as a sale counts and deletes them.</summary>
    private HashSet<EntityUid> WfWithContents(HashSet<EntityUid> goods)
    {
        var all = new HashSet<EntityUid>(goods);
        foreach (var ent in goods)
        {
            if (TryComp<ContainerManagerComponent>(ent, out var containers))
                GatherContainedEntities(ent, containers, all);
        }

        return all;
    }
}
