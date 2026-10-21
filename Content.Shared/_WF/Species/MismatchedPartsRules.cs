using System.Collections.Frozen;
using System.Linq;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Species;

/// <summary>
/// What the Mismatched parts option unlocks: every species' markings, hair and facial hair, on body parts the
/// character's sprite can draw, with one point for each category the species has none for.
/// </summary>
public static class MismatchedPartsRules
{
    /// <summary>Body parts only some species' sprites have a layer for; the option leaves them to those species.</summary>
    private static readonly HumanoidVisualLayers[] SpeciesOnlyParts =
    {
        HumanoidVisualLayers.TailExtras, HumanoidVisualLayers.LArmExtension, HumanoidVisualLayers.RArmExtension,
    };

    /// <summary>Whether this is a hair category, whose style is saved outside the marking set.</summary>
    public static bool IsHair(MarkingCategories category)
    {
        return category is MarkingCategories.Hair or MarkingCategories.FacialHair;
    }

    /// <summary>
    /// Whether the species wears this category on its own: it has points for it, a sprite layer that takes markings
    /// and at least one style.
    /// </summary>
    public static bool IsNative(MarkingCategories category, string species, MarkingManager markings, IPrototypeManager proto)
    {
        return NativeStyles(category, species, markings, proto) != null;
    }

    /// <summary>
    /// The styles of a category a character of this species may pick: every species' styles with the option on,
    /// otherwise its own, and no hair or facial hair it can't wear.
    /// </summary>
    public static IReadOnlyDictionary<string, MarkingPrototype> Styles(
        MarkingCategories category,
        string species,
        bool mismatchedParts,
        MarkingManager markings,
        IPrototypeManager proto)
    {
        if (mismatchedParts)
            return markings.MarkingsByCategory(category);

        if (!IsHair(category))
            return markings.MarkingsByCategoryAndSpecies(category, species);

        return NativeStyles(category, species, markings, proto) ?? FrozenDictionary<string, MarkingPrototype>.Empty;
    }

    /// <summary>
    /// Whether the option puts this hair or facial hair style on the character: it is on, and the style is one the
    /// species can't wear on its own but the option opens for this sex.
    /// </summary>
    public static bool Unlocks(
        MarkingCategories category,
        string style,
        string species,
        Sex sex,
        bool mismatchedParts,
        MarkingManager markings,
        IPrototypeManager proto)
    {
        if (!mismatchedParts
            || !IsHair(category)
            || !markings.MarkingsByCategory(category).TryGetValue(style, out var prototype)
            || NativeStyles(category, species, markings, proto)?.ContainsKey(style) == true)
            return false;

        return prototype.SexRestriction == null || prototype.SexRestriction == sex;
    }

    /// <summary>Whether a character of this species can have the marking drawn: its sprite has a layer for the part.</summary>
    public static bool Drawable(MarkingPrototype marking, string species, IPrototypeManager proto)
    {
        if (!SpeciesOnlyParts.Contains(marking.BodyPart))
            return true;

        return proto.TryIndex<SpeciesPrototype>(species, out var speciesProto)
               && proto.TryIndex<HumanoidSpeciesBaseSpritesPrototype>(speciesProto.SpriteSet, out var sprites)
               && sprites.Sprites.ContainsKey(marking.BodyPart);
    }

    /// <summary>Gives one point to every category of a fresh set's budget that has none.</summary>
    public static void OpenPoints(Dictionary<MarkingCategories, MarkingPoints> points)
    {
        foreach (var limit in points.Values)
        {
            if (limit.Points <= 0)
                limit.Points = 1;
        }
    }

    /// <summary>The species' own styles if it wears the category on its own, otherwise null.</summary>
    private static IReadOnlyDictionary<string, MarkingPrototype>? NativeStyles(
        MarkingCategories category,
        string species,
        MarkingManager markings,
        IPrototypeManager proto)
    {
        if (!proto.TryIndex<SpeciesPrototype>(species, out var speciesProto))
            return null;

        if (proto.TryIndex<MarkingPointsPrototype>(speciesProto.MarkingPoints, out var points)
            && points.Points.TryGetValue(category, out var limit)
            && limit.Points <= 0)
            return null;

        if (Layer(category) is not { } layer
            || !proto.TryIndex<HumanoidSpeciesBaseSpritesPrototype>(speciesProto.SpriteSet, out var sprites)
            || !sprites.Sprites.TryGetValue(layer, out var layerId)
            || !proto.TryIndex<HumanoidSpeciesSpriteLayer>(layerId, out var layerProto)
            || !layerProto.AllowsMarkings)
            return null;

        var styles = markings.MarkingsByCategoryAndSpecies(category, species);
        return styles.Count > 0 ? styles : null;
    }

    /// <summary>How a character's skin colour is limited: any colour with the option on, otherwise as the species says.</summary>
    public static HumanoidSkinColor SkinColoration(HumanoidSkinColor species, bool mismatchedParts)
    {
        return mismatchedParts ? HumanoidSkinColor.AnyColour : species;
    }

    private static HumanoidVisualLayers? Layer(MarkingCategories category)
    {
        return category switch
        {
            MarkingCategories.Hair => HumanoidVisualLayers.Hair,
            MarkingCategories.FacialHair => HumanoidVisualLayers.FacialHair,
            _ => null,
        };
    }
}
