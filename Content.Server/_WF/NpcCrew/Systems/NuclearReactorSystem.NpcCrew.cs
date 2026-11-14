using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;

namespace Content.Server._FarHorizons.Power.Generation.FissionGenerator;

/// <summary>
/// The reactor grid for an NPC engineer: parts go in and come out of slots as they would through the reactor's own
/// panel, without the part slot or the panel in between.
/// </summary>
public sealed partial class NuclearReactorSystem
{
    /// <summary>The part in a slot of the grid, or null for an empty or out-of-range slot.</summary>
    public ReactorPartComponent? GetPart(NuclearReactorComponent reactor, Vector2i slot)
    {
        if (reactor.ComponentGrid == null || slot.X < 0 || slot.Y < 0 || slot.X >= reactor.ReactorGridWidth || slot.Y >= reactor.ReactorGridHeight)
            return null;

        return reactor.ComponentGrid[slot.X, slot.Y];
    }

    /// <summary>Puts a reactor part item into an empty slot of the grid; the item is used up. False and nothing changed otherwise.</summary>
    public bool TryLoadPart(Entity<NuclearReactorComponent> ent, Vector2i slot, EntityUid item)
    {
        var reactor = ent.Comp;
        if (reactor.Melted || reactor.ComponentGrid == null || GetPart(reactor, slot) != null
            || slot.X < 0 || slot.Y < 0 || slot.X >= reactor.ReactorGridWidth || slot.Y >= reactor.ReactorGridHeight
            || !TryComp<ReactorPartComponent>(item, out var part))
            return false;

        var loaded = new ReactorPartComponent(part);
        if (MetaData(item).EntityPrototype is { } prototype)
            loaded.ProtoId = prototype.ID;
        reactor.ComponentGrid[slot.X, slot.Y] = loaded;
        Del(item);

        UpdateGridVisual(ent);
        UpdateGasVolume(reactor);
        UpdateUI(ent.Owner, reactor);
        return true;
    }

    /// <summary>Takes the part out of a slot as an item dropped at the reactor. Null when the slot is empty or its part has melted in.</summary>
    public EntityUid? TryUnloadPart(Entity<NuclearReactorComponent> ent, Vector2i slot)
    {
        var reactor = ent.Comp;
        if (GetPart(reactor, slot) is not { } part || part.Melted)
            return null;

        var item = Spawn(part.ProtoId, Transform(ent).Coordinates);
        RemComp<ReactorPartComponent>(item);
        AddComp(item, new ReactorPartComponent(part));
        reactor.ComponentGrid![slot.X, slot.Y] = null;

        UpdateGridVisual(ent);
        UpdateGasVolume(reactor);
        UpdateUI(ent.Owner, reactor);
        return item;
    }
}
