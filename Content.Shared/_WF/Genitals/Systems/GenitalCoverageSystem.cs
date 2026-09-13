using Content.Shared._WF.Genitals.Components;
using Content.Shared.Foldable;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Genitals.Systems;

/// <summary>Single source of truth for whether a genital is exposed and which layer set draws it.</summary>
/// <remarks>Exposure is objective. Consent and presence are separate gates that each consumer applies.</remarks>
public sealed partial class GenitalCoverageSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    /// <summary>Inventory slots whose items can cover anatomy.</summary>
    private const SlotFlags CoveringSlots = SlotFlags.INNERCLOTHING | SlotFlags.OUTERCLOTHING;

    /// <summary>Clothing and undergarment coverage of a body. Callers compute it once per update.</summary>
    public GenitalCoverage GetCoverage(Entity<GenitalsComponent, HumanoidAppearanceComponent?> ent)
    {
        var clothing = GetClothingCoverage(ent.Owner, out var chestCover, out var groinCover);

        var undergarments = GenitalRegion.None;
        if (Resolve(ent.Owner, ref ent.Comp2, false))
        {
            var flags = ent.Comp1.Undergarments;
            if ((flags & UndergarmentFlags.TopRemoved) == 0 && HasVisibleUndergarment(ent.Comp2, UndergarmentSlot.Top))
                undergarments |= GenitalRegion.Chest;

            if ((flags & UndergarmentFlags.BottomRemoved) == 0 && HasVisibleUndergarment(ent.Comp2, UndergarmentSlot.Bottom))
                undergarments |= GenitalRegion.Groin;
        }

        return new GenitalCoverage(clothing, chestCover, groinCover, undergarments);
    }

    /// <summary>Body region of a slot. Breasts use their shape's region (udders are Groin); the womb has none.</summary>
    public GenitalRegion GetRegion(Entity<GenitalsComponent> ent, GenitalSlot slot)
    {
        switch (slot)
        {
            case GenitalSlot.Penis:
            case GenitalSlot.Testicles:
            case GenitalSlot.Vagina:
                return GenitalRegion.Groin;
            case GenitalSlot.Breasts:
                return ent.Comp.Breasts?.Shape is { } shape && _proto.TryIndex(shape, out var proto)
                    ? proto.Region
                    : GenitalRegion.Chest;
            default:
                return GenitalRegion.None;
        }
    }

    /// <summary>Exposure of one slot from coverage computed once per update.</summary>
    public GenitalExposure GetExposure(Entity<GenitalsComponent> ent, GenitalSlot slot, in GenitalCoverage coverage)
    {
        if (!ent.Comp.HasExternalOrgan(slot))
            return new GenitalExposure(false, false, GenitalLayerSet.Hidden, null, false);

        var region = GetRegion(ent, slot);
        var preview = ent.Comp.PreviewMode;
        var cloth = preview < GenitalPreviewMode.UnderwearOnly && (coverage.Clothing & region) != 0;
        var under = preview != GenitalPreviewMode.Nude
                    && ent.Comp.RevealMode == GenitalRevealMode.UndergarmentRemoval
                    && (coverage.Undergarments & region) != 0;
        var coveredBy = cloth ? coverage.CoverFor(region) : null;

        switch (ent.Comp.Visibility.Get(slot))
        {
            case GenitalVisibility.AlwaysHidden:
                return new GenitalExposure(true, false, GenitalLayerSet.Hidden, coveredBy, under);
            case GenitalVisibility.ShowThroughClothing:
                return new GenitalExposure(true, true, cloth ? GenitalLayerSet.Over : GenitalLayerSet.Under, coveredBy, under);
            default:
                var exposed = !cloth && !under;
                return new GenitalExposure(true, exposed, exposed ? GenitalLayerSet.Under : GenitalLayerSet.Hidden, coveredBy, under);
        }
    }

    /// <summary>For stripping: is the region covered by clothing (undergarments ignored)?</summary>
    public bool IsRegionCoveredByClothing(EntityUid wearer, GenitalRegion region)
    {
        return (GetClothingCoverage(wearer, out _, out _) & region) != 0;
    }

    /// <summary>Regions one worn item covers. Items without coverage data cover everything.</summary>
    public GenitalRegion GetItemRegions(EntityUid item)
    {
        if (!TryComp<GenitalCoverageComponent>(item, out var coverage))
            return GenitalRegion.All;

        if (coverage.FoldedRegions is { } folded
            && TryComp<FoldableComponent>(item, out var foldable)
            && foldable.IsFolded)
            return folded;

        return coverage.Regions;
    }

    /// <summary>Regions covered by the inner and outer clothing slots, with the outermost covering item per region.</summary>
    private GenitalRegion GetClothingCoverage(EntityUid wearer, out EntityUid? chestCover, out EntityUid? groinCover)
    {
        chestCover = null;
        groinCover = null;
        var covered = GenitalRegion.None;

        if (!_inventory.TryGetContainerSlotEnumerator(wearer, out var slots, CoveringSlots))
            return covered;

        while (slots.NextItem(out var item, out var slot))
        {
            var regions = GetItemRegions(item);
            if (regions == GenitalRegion.None)
                continue;

            covered |= regions;
            var outer = (slot.SlotFlags & SlotFlags.OUTERCLOTHING) != 0;

            if ((regions & GenitalRegion.Chest) != 0 && (chestCover == null || outer))
                chestCover = item;

            if ((regions & GenitalRegion.Groin) != 0 && (groinCover == null || outer))
                groinCover = item;
        }

        return covered;
    }

    /// <summary>The humanoid wears a visible marking of the slot's undergarment category.</summary>
    public static bool HasVisibleUndergarment(HumanoidAppearanceComponent? humanoid, UndergarmentSlot slot)
    {
        if (humanoid == null || !humanoid.MarkingSet.TryGetCategory(UndergarmentSlots.CategoryOf(slot), out var markings))
            return false;

        foreach (var marking in markings)
        {
            if (marking.Visible)
                return true;
        }

        return false;
    }
}

/// <summary>Coverage of one body: clothing (with the outermost covering item per region) and undergarments.</summary>
public readonly record struct GenitalCoverage(
    GenitalRegion Clothing,
    EntityUid? ChestCover,
    EntityUid? GroinCover,
    GenitalRegion Undergarments)
{
    /// <summary>Outermost clothing covering the region; the groin cover wins for a mask naming both regions.</summary>
    public EntityUid? CoverFor(GenitalRegion region)
    {
        if ((region & GenitalRegion.Groin) != 0 && GroinCover != null)
            return GroinCover;

        return (region & GenitalRegion.Chest) != 0 ? ChestCover : null;
    }
}

/// <summary>Result of the coverage check for one slot.</summary>
/// <param name="Present">An external organ occupies the slot.</param>
/// <param name="Exposed">The organ is drawn and described.</param>
/// <param name="Layer">The anchor set it draws against.</param>
/// <param name="CoveredBy">Outermost clothing covering its region, when clothing counts.</param>
/// <param name="CoveredByUndergarment">An undergarment covers its region under the reveal mode.</param>
public readonly record struct GenitalExposure(
    bool Present,
    bool Exposed,
    GenitalLayerSet Layer,
    EntityUid? CoveredBy,
    bool CoveredByUndergarment);
