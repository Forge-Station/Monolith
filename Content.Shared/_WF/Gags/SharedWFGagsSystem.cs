using Content.Shared.Inventory;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Gags;

/// <summary>Sets the colours of recolourable items, including the ones a loadout spawns.</summary>
public abstract partial class SharedWFGagsSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    /// <summary>Recolours an item. Missing colours keep the prototype's; extra ones are ignored.</summary>
    public void SetColors(Entity<WFGagsComponent?> ent, List<Color> colors)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        var defaults = DefaultColors(MetaData(ent).EntityPrototype);
        var count = defaults?.Count ?? ent.Comp.Colors.Count;
        var result = new List<Color>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(i < colors.Count ? colors[i].WithAlpha(1f) : defaults?[i] ?? ent.Comp.Colors[i]);
        }

        ent.Comp.Colors = result;
        Dirty(ent);
        OnColorsChanged((ent, ent.Comp));
    }

    /// <summary>The colours an item prototype starts with, or null when it isn't recolourable.</summary>
    public List<Color>? DefaultColors(EntityPrototype? proto)
    {
        if (proto == null || !proto.TryComp<WFGagsComponent>(out var gags, Factory))
            return null;

        return gags.Colors;
    }

    /// <summary>The colours an item prototype starts with, or null when it isn't recolourable.</summary>
    public List<Color>? DefaultColors(EntProtoId id)
    {
        return _prototype.TryIndex(id, out var proto) ? DefaultColors(proto) : null;
    }

    /// <summary>The recolourable item a loadout puts on, if it has one: the first in slot order.</summary>
    public EntProtoId? ColorableItem(LoadoutPrototype loadout)
    {
        foreach (var (_, proto) in loadout.Equipment)
        {
            if (DefaultColors(proto) != null)
                return proto;
        }

        return null;
    }

    /// <summary>Gives the item a loadout just put on the colours the player picked for it.</summary>
    public void ApplyLoadout(EntityUid wearer, LoadoutPrototype proto, Loadout loadout)
    {
        if (loadout.WFGagsColors is not { Count: > 0 } colors || ColorableItem(proto) is not { } item)
            return;

        foreach (var (slot, id) in proto.Equipment)
        {
            if (id != item ||
                !_inventory.TryGetSlotEntity(wearer, slot, out var worn) ||
                MetaData(worn.Value).EntityPrototype is not { } wornProto ||
                wornProto.ID != id)
                continue;

            SetColors(worn.Value, colors);
            return;
        }
    }

    protected virtual void OnColorsChanged(Entity<WFGagsComponent> ent)
    {
    }
}
