using Content.Shared.Examine;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Warehouse;

public abstract class SharedWarehouseSiloSystem : EntitySystem
{
    [Dependency] protected EntityWhitelistSystem Whitelist = default!;
    [Dependency] protected IPrototypeManager Prototypes = default!;
    [Dependency] protected SharedPowerReceiverSystem Power = default!;
    [Dependency] protected SharedStackSystem Stacks = default!;
    [Dependency] protected SharedUserInterfaceSystem UI = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WarehouseSiloComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<WarehouseSiloComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var types = 0;
        var units = 0;
        foreach (var count in ent.Comp.Contents.Values)
        {
            if (count <= 0)
                continue;

            types++;
            units += count;
        }

        args.PushMarkup(types == 0
            ? Loc.GetString("warehouse-silo-examine-empty")
            : Loc.GetString("warehouse-silo-examine", ("types", types), ("units", units)));
    }

    protected bool CanInsert(Entity<WarehouseSiloComponent> ent, EntityUid item)
    {
        if (!TryComp<StackComponent>(item, out var stack) || stack.Count <= 0)
            return false;

        return Whitelist.IsWhitelistPassOrNull(ent.Comp.Whitelist, item);
    }

    protected void InsertAmount(Entity<WarehouseSiloComponent> ent, string stackType, int amount)
    {
        if (amount <= 0)
            return;

        ent.Comp.Contents.TryGetValue(stackType, out var existing);
        ent.Comp.Contents[stackType] = existing + amount;
        Dirty(ent);
    }

    protected int TakeAmount(Entity<WarehouseSiloComponent> ent, string stackType, int requested)
    {
        if (requested <= 0 || !ent.Comp.Contents.TryGetValue(stackType, out var stored) || stored <= 0)
            return 0;

        var taken = Math.Min(stored, requested);
        stored -= taken;
        if (stored <= 0)
            ent.Comp.Contents.Remove(stackType);
        else
            ent.Comp.Contents[stackType] = stored;

        Dirty(ent);
        return taken;
    }

    protected void UpdateUi(Entity<WarehouseSiloComponent> ent)
    {
        var entries = new List<WarehouseSiloEntry>();
        foreach (var (stackType, count) in ent.Comp.Contents)
        {
            if (count <= 0)
                continue;

            var name = stackType;
            if (Prototypes.TryIndex<StackPrototype>(stackType, out var proto))
                name = Loc.GetString(proto.Name);

            entries.Add(new WarehouseSiloEntry(stackType, name, count));
        }

        entries.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        UI.SetUiState(ent.Owner, WarehouseSiloUiKey.Key, new WarehouseSiloBuiState(entries));
    }
}
