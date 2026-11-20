using System.Linq;
using System.Text;
using Content.Shared._WF.LegStyle;
using Content.Shared.Body.Components;
using Content.Shared.Body.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Augments;

/// <summary>Where each slot sits on a body, which augments and styles a character may pick, and how they are stored.</summary>
public static class WFAugmentRules
{
    private const char EntrySeparator = ';';
    private const char FieldSeparator = ':';

    /// <summary>The body slot holding the part a limb augment replaces, or the organ an organ augment replaces.</summary>
    public static string PartSlot(WFAugmentSlot slot) => slot switch
    {
        WFAugmentSlot.Head or WFAugmentSlot.Eyes => "head",
        WFAugmentSlot.LeftArm => "left arm",
        WFAugmentSlot.RightArm => "right arm",
        WFAugmentSlot.LeftLeg => "left leg",
        WFAugmentSlot.RightLeg => "right leg",
        _ => "torso",
    };

    /// <summary>The organ slot an organ augment fills, or null for a part.</summary>
    public static string? OrganSlot(WFAugmentSlot slot) => slot switch
    {
        WFAugmentSlot.Eyes => "eyes",
        WFAugmentSlot.Heart => "heart",
        WFAugmentSlot.Lungs => "lungs",
        WFAugmentSlot.Liver => "liver",
        WFAugmentSlot.Stomach => "stomach",
        _ => null,
    };

    /// <summary>The humanoid layers a limb or plating in this slot draws over.</summary>
    public static HumanoidVisualLayers[] Layers(WFAugmentSlot slot) => slot switch
    {
        WFAugmentSlot.Head => [HumanoidVisualLayers.Head],
        WFAugmentSlot.Chest => [HumanoidVisualLayers.Chest],
        WFAugmentSlot.LeftArm => [HumanoidVisualLayers.LArm, HumanoidVisualLayers.LHand],
        WFAugmentSlot.RightArm => [HumanoidVisualLayers.RArm, HumanoidVisualLayers.RHand],
        WFAugmentSlot.LeftLeg => [HumanoidVisualLayers.LLeg, HumanoidVisualLayers.LFoot],
        WFAugmentSlot.RightLeg => [HumanoidVisualLayers.RLeg, HumanoidVisualLayers.RFoot],
        _ => [],
    };

    /// <summary>Whether the species' body has the part or organ this slot works on.</summary>
    public static bool SlotAvailable(string species, WFAugmentSlot slot, IPrototypeManager proto, IComponentFactory factory)
    {
        if (!proto.TryIndex<SpeciesPrototype>(species, out var speciesProto)
            || !proto.TryIndex(speciesProto.Prototype, out var mob)
            || !mob.TryComp(out BodyComponent? body, factory)
            || body.Prototype is not { } bodyId
            || !proto.TryIndex<BodyPrototype>(bodyId, out var bodyProto)
            || !bodyProto.Slots.TryGetValue(PartSlot(slot), out var part))
            return false;

        return OrganSlot(slot) is not { } organ || part.Organs.ContainsKey(organ);
    }

    /// <summary>The sprite a style draws on a layer, the digitigrade one on digitigrade legs.</summary>
    public static ProtoId<HumanoidSpeciesSpriteLayer>? Sprite(WFAugmentStylePrototype style, HumanoidVisualLayers layer, bool digitigrade)
    {
        if (digitigrade && style.Digitigrade.TryGetValue(layer, out var digi))
            return digi;
        return style.Sprites.TryGetValue(layer, out var sprite) ? sprite : (ProtoId<HumanoidSpeciesSpriteLayer>?) null;
    }

    /// <summary>
    /// The sprite a style draws on a layer over a body whose own limb there is <paramref name="under"/>: the style
    /// fitted to that limb's shape when it differs from the human's, else the human-shaped one.
    /// </summary>
    public static ProtoId<HumanoidSpeciesSpriteLayer>? Sprite(WFAugmentStylePrototype style,
        HumanoidVisualLayers layer,
        bool digitigrade,
        string? under,
        IPrototypeManager proto)
    {
        if (under != null && style.Fitted.Count > 0)
        {
            foreach (var shape in proto.EnumeratePrototypes<WFAugmentShapePrototype>())
            {
                if (style.Fitted.TryGetValue(shape.ID, out var fitted) && shape.BaseSprites.Contains(under))
                    return fitted;
            }
        }

        return Sprite(style, layer, digitigrade);
    }

    /// <summary>The base sprite a body draws on a layer without augments: its leg style's, else its species'.</summary>
    public static string? Underlying(HumanoidAppearanceComponent humanoid, HumanoidVisualLayers layer, IPrototypeManager proto)
    {
        if (humanoid.LegStyle is { } legStyleId
            && proto.TryIndex(legStyleId, out var legStyle)
            && legStyle.Sprites.TryGetValue(layer, out var legSprite))
            return legSprite;

        if (proto.TryIndex<SpeciesPrototype>(humanoid.Species, out var species)
            && proto.TryIndex<HumanoidSpeciesBaseSpritesPrototype>(species.SpriteSet, out var set)
            && set.Sprites.TryGetValue(layer, out var sprite))
            return sprite;

        return null;
    }

    /// <summary>Whether a style draws every layer of a slot.</summary>
    public static bool StyleFits(WFAugmentStylePrototype style, WFAugmentSlot slot)
    {
        return Layers(slot).All(style.Sprites.ContainsKey);
    }

    /// <summary>The styles that fit a slot, ordered by prototype id.</summary>
    public static List<WFAugmentStylePrototype> StylesFor(WFAugmentSlot slot, IPrototypeManager proto)
    {
        return proto.EnumeratePrototypes<WFAugmentStylePrototype>()
            .Where(s => StyleFits(s, slot))
            .OrderBy(s => s.ID)
            .ToList();
    }

    /// <summary>The augments a species may take in a slot, in order.</summary>
    public static List<WFAugmentPrototype> AugmentsFor(string species, WFAugmentSlot slot, IPrototypeManager proto, IComponentFactory factory)
    {
        if (!SlotAvailable(species, slot, proto, factory))
            return new List<WFAugmentPrototype>();

        return proto.EnumeratePrototypes<WFAugmentPrototype>()
            .Where(a => a.Slot == slot && !a.SpeciesBlacklist.Contains(species))
            .OrderBy(a => a.Order)
            .ThenBy(a => a.ID)
            .ToList();
    }

    /// <summary>Whether the profile's legs are digitigrade.</summary>
    public static bool Digitigrade(HumanoidCharacterProfile profile, IPrototypeManager proto)
    {
        return LegStyleRules.IsDigitigrade(profile.Species, profile.LegStance, proto);
    }

    /// <summary>The slots the given traits put their own parts in, with the trait that claims each.</summary>
    public static Dictionary<WFAugmentSlot, ProtoId<TraitPrototype>> ClaimedSlots(IEnumerable<ProtoId<TraitPrototype>> traits,
        IPrototypeManager proto)
    {
        var claimed = new Dictionary<WFAugmentSlot, ProtoId<TraitPrototype>>();
        foreach (var trait in traits)
        {
            if (!proto.TryIndex<WFAugmentTraitSlotsPrototype>(trait.Id, out var slots))
                continue;
            foreach (var slot in slots.Slots)
                claimed.TryAdd(slot, trait);
        }

        return claimed;
    }

    /// <summary>
    /// Drops choices the species can't take, slots a trait claims, augments that don't belong to their slot and
    /// styles that don't fit, and gives a styled augment without a style the first one that fits.
    /// </summary>
    public static Dictionary<WFAugmentSlot, WFAugmentChoice> Validate(string species,
        IReadOnlyDictionary<WFAugmentSlot, WFAugmentChoice>? choices,
        IEnumerable<ProtoId<TraitPrototype>> traits,
        IPrototypeManager proto,
        IComponentFactory factory)
    {
        var claimed = ClaimedSlots(traits, proto);
        var valid = new Dictionary<WFAugmentSlot, WFAugmentChoice>();
        if (choices == null)
            return valid;

        foreach (var (slot, choice) in choices)
        {
            if (!Enum.IsDefined(typeof(WFAugmentSlot), slot)
                || claimed.ContainsKey(slot)
                || !proto.TryIndex(choice.Augment, out var augment)
                || augment.Slot != slot
                || augment.SpeciesBlacklist.Contains(species)
                || !SlotAvailable(species, slot, proto, factory))
                continue;

            ProtoId<WFAugmentStylePrototype>? style = null;
            if (augment.Styled)
            {
                if (choice.Style is { } picked && proto.TryIndex(picked, out var pickedStyle) && StyleFits(pickedStyle, slot))
                    style = picked;
                else if (StylesFor(slot, proto).FirstOrDefault() is { } first)
                    style = first.ID;
                else
                    continue;
            }

            valid[slot] = new WFAugmentChoice(augment.ID, style);
        }

        return valid;
    }

    /// <summary>The price still owed for the profile's augments: priced ones it picked but hasn't unlocked.</summary>
    public static int Owed(HumanoidCharacterProfile profile, IPrototypeManager proto)
    {
        var owed = 0;
        foreach (var choice in profile.Augments.Values)
        {
            if (proto.TryIndex(choice.Augment, out var augment)
                && augment.Price > 0
                && !profile.AugmentUnlocks.Contains(augment.ID))
                owed += augment.Price;
        }

        return owed;
    }

    /// <summary>Choices as stored in the database: slot:augment[:style] entries.</summary>
    public static string ToStored(IReadOnlyDictionary<WFAugmentSlot, WFAugmentChoice> choices)
    {
        var builder = new StringBuilder();
        foreach (var (slot, choice) in choices.OrderBy(c => c.Key))
        {
            if (builder.Length > 0)
                builder.Append(EntrySeparator);
            builder.Append(slot).Append(FieldSeparator).Append(choice.Augment.Id);
            if (choice.Style is { } style)
                builder.Append(FieldSeparator).Append(style.Id);
        }

        return builder.ToString();
    }

    /// <summary>Reads <see cref="ToStored(IReadOnlyDictionary{WFAugmentSlot, WFAugmentChoice})"/> text. Entries it can't read are dropped.</summary>
    public static Dictionary<WFAugmentSlot, WFAugmentChoice> FromStored(string? stored)
    {
        var choices = new Dictionary<WFAugmentSlot, WFAugmentChoice>();
        if (string.IsNullOrEmpty(stored))
            return choices;

        foreach (var entry in stored.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = entry.Split(FieldSeparator);
            if (fields.Length < 2 || !Enum.TryParse<WFAugmentSlot>(fields[0], out var slot))
                continue;

            choices[slot] = new WFAugmentChoice(fields[1], fields.Length > 2 ? fields[2] : null);
        }

        return choices;
    }

    /// <summary>Unlocked augments as stored in the database, separated by semicolons.</summary>
    public static string ToStored(IEnumerable<ProtoId<WFAugmentPrototype>> unlocks)
    {
        return string.Join(EntrySeparator, unlocks.Select(u => u.Id).Order());
    }

    /// <summary>Reads stored unlocks.</summary>
    public static HashSet<ProtoId<WFAugmentPrototype>> UnlocksFromStored(string? stored)
    {
        var unlocks = new HashSet<ProtoId<WFAugmentPrototype>>();
        if (string.IsNullOrEmpty(stored))
            return unlocks;

        foreach (var id in stored.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            unlocks.Add(id);
        return unlocks;
    }
}
