using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Item;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.RMCClothing.Patterns;

/// <summary>Switches camouflage clothing between its patterns by copying the visuals of a pattern prototype.</summary>
public sealed class WFCamoPatternSystem : EntitySystem
{
    [Dependency] private ClothingSystem _clothing = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private static readonly VerbCategory Category = new("wf-camo-verb-category", null);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCamoPatternComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnGetVerbs(Entity<WFCamoPatternComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || ent.Comp.Patterns.Count < 2)
            return;

        for (var i = 0; i < ent.Comp.Patterns.Count; i++)
        {
            var index = i;
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("wf-camo-pattern-" + ent.Comp.Patterns[i].Name),
                Category = Category,
                Disabled = i == ent.Comp.Current,
                Priority = -i,
                Act = () => SetPattern(ent, index),
            });
        }
    }

    /// <summary>Puts the item in the pattern at <paramref name="index"/>.</summary>
    public void SetPattern(Entity<WFCamoPatternComponent> ent, int index)
    {
        if (index < 0 || index >= ent.Comp.Patterns.Count || index == ent.Comp.Current)
            return;

        ent.Comp.Current = index;
        Dirty(ent);
        ApplyPattern(ent);
    }

    /// <summary>Copies the current pattern's clothing, held and icon visuals onto the item.</summary>
    public void ApplyPattern(Entity<WFCamoPatternComponent> ent)
    {
        if (ent.Comp.Current >= ent.Comp.Patterns.Count ||
            !_prototypes.TryIndex(ent.Comp.Patterns[ent.Comp.Current].Look, out var look))
            return;

        if (TryComp<ClothingComponent>(ent, out var clothing) &&
            look.TryGetComponent<ClothingComponent>(out var lookClothing, EntityManager.ComponentFactory))
            _clothing.CopyVisuals(ent, lookClothing, clothing);

        if (TryComp<ItemComponent>(ent, out var item) &&
            look.TryGetComponent<ItemComponent>(out var lookItem, EntityManager.ComponentFactory))
            _item.CopyVisuals(ent, lookItem, item);

        var ev = new WFCamoPatternChangedEvent(look);
        RaiseLocalEvent(ent, ref ev);
    }
}

/// <summary>Raised on the item after its pattern's visuals are copied, so the client can redraw its sprite.</summary>
[ByRefEvent]
public readonly record struct WFCamoPatternChangedEvent(EntityPrototype Look);
