using Content.Shared._Onyx.Wounds;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>Bookkeeping for objects stuck in wounds: what is in a part, and refusing treatment until it is out.</summary>
public sealed class WolfmedEmbeddedObjectSystem : EntitySystem
{
    [Dependency] private WoundSystem _wounds = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedEmbeddedObjectComponent, WoundTreatmentAttemptEvent>(OnTreatmentAttempt);
    }

    private void OnTreatmentAttempt(Entity<WolfmedEmbeddedObjectComponent> wound, ref WoundTreatmentAttemptEvent args)
    {
        if (wound.Comp.BlocksTreatment && wound.Comp.Count > 0)
            args.Cancelled = true;
    }

    /// <summary>Adds objects to a wound, up to its cap. Returns how many actually went in.</summary>
    public int Add(EntityUid wound, EntProtoId item, int count, int maxCount)
    {
        if (count <= 0)
            return 0;

        // Not EnsureComp: the component's own default is one object, which would make the first rule hit two.
        if (!TryComp(wound, out WolfmedEmbeddedObjectComponent? comp))
        {
            comp = AddComp<WolfmedEmbeddedObjectComponent>(wound);
            comp.Count = 0;
        }

        comp.Item = item;
        comp.MaxCount = Math.Max(comp.MaxCount, maxCount);
        var added = Math.Min(count, Math.Max(0, comp.MaxCount - comp.Count));
        comp.Count += added;
        Dirty(wound, comp);
        return added;
    }

    /// <summary>The first wound on a part that still has something in it.</summary>
    public Entity<WolfmedEmbeddedObjectComponent>? GetEmbeddedWound(Entity<WoundableComponent?> part)
    {
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (TryComp(wound, out WolfmedEmbeddedObjectComponent? embedded) && embedded.Count > 0)
                return (wound.Owner, embedded);
        }

        return null;
    }

    /// <summary>Total objects stuck in a part, for the analyzer.</summary>
    public int GetPartCount(Entity<WoundableComponent?> part)
    {
        var total = 0;
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (TryComp(wound, out WolfmedEmbeddedObjectComponent? embedded))
                total += Math.Max(0, embedded.Count);
        }

        return total;
    }

    /// <summary>Takes one object out. The caller spawns the item; the wound is left free to be treated once empty.</summary>
    public bool TryTakeOne(Entity<WolfmedEmbeddedObjectComponent?> wound, out EntProtoId item)
    {
        item = default;
        if (!Resolve(wound, ref wound.Comp, false) || wound.Comp.Count <= 0)
            return false;

        item = wound.Comp.Item;
        wound.Comp.Count--;
        Dirty(wound, wound.Comp);
        if (wound.Comp.Count <= 0)
            RemComp<WolfmedEmbeddedObjectComponent>(wound);

        return true;
    }
}
