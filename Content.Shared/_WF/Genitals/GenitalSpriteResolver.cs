using System.Diagnostics.CodeAnalysis;
using Content.Shared._WF.Genitals.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Genitals;

/// <summary>Maps shapes, sizes and arousal to RSI states. Pure.</summary>
public static class GenitalSpriteResolver
{
    public const string FrontLayer = "FRONT";
    public const string BehindLayer = "BEHIND";

    /// <summary>RSI state for a shape at a logical step. False when the shape has no art for that layer.</summary>
    /// <remarks>
    /// FRONT uses the nearest available step (see ResolveStep). BEHIND is drawn only when that same step has BEHIND art.
    /// Aroused art is used where it exists for the step; otherwise the resting art.
    /// </remarks>
    public static bool TryGetState(GenitalShapePrototype shape, int step, bool aroused, bool behind, [NotNullWhen(true)] out string? state)
    {
        state = null;
        if (shape.Sizes.Count == 0)
            return false;

        var artStep = ResolveStep(shape.Sizes, ToArtStep(shape, step));

        if (!behind)
        {
            state = FormatState(shape, artStep, aroused && shape.ArousedSizes.Contains(artStep), false);
            return true;
        }

        if (aroused && shape.BehindArousedSizes.Contains(artStep))
            state = FormatState(shape, artStep, true, true);
        else if (shape.BehindSizes.Contains(artStep))
            state = FormatState(shape, artStep, false, true);

        return state != null;
    }

    /// <summary>Maps a logical step through ArtSteps (identity when null).</summary>
    public static int ToArtStep(GenitalShapePrototype shape, int step)
    {
        if (shape.ArtSteps is not { Count: > 0 } map)
            return step;

        return map[Math.Clamp(step, 1, map.Count) - 1];
    }

    /// <summary>Highest available step not above the requested one, else the lowest available; the request itself when none are listed.</summary>
    public static int ResolveStep(IReadOnlyList<int> available, int requested)
    {
        var best = int.MinValue;
        var lowest = int.MaxValue;
        foreach (var candidate in available)
        {
            if (candidate <= requested && candidate > best)
                best = candidate;

            if (candidate < lowest)
                lowest = candidate;
        }

        if (best != int.MinValue)
            return best;

        return lowest != int.MaxValue ? lowest : requested;
    }

    /// <summary>State name for an art step: the template with its tokens filled in, then StateOverrides.</summary>
    public static string FormatState(GenitalShapePrototype shape, int artStep, bool aroused, bool behind)
    {
        var size = shape.SizeTokens is { } tokens && artStep >= 1 && artStep <= tokens.Count
            ? tokens[artStep - 1]
            : artStep.ToString();

        var state = shape.State
            .Replace("{size}", size)
            .Replace("{aroused}", aroused ? "1" : "0")
            .Replace("{layer}", behind ? BehindLayer : FrontLayer);

        return shape.StateOverrides.TryGetValue(state, out var fixedState) ? fixedState : state;
    }

    /// <summary>Whether a slot draws its aroused art at this arousal state (penis: Full; vagina: from VaginaArousedFrom).</summary>
    public static bool UsesArousedArt(GenitalSlot slot, ArousalState arousal, GenitalSettingsPrototype settings)
    {
        return slot switch
        {
            GenitalSlot.Penis => arousal == ArousalState.Full,
            GenitalSlot.Vagina => settings.VaginaArousedFrom != ArousalState.None && arousal >= settings.VaginaArousedFrom,
            _ => false,
        };
    }

    /// <summary>Outer and inner sheath states for an arousal state; null hides that layer.</summary>
    public static (string? Outer, string? Inner) GetSheathStates(GenitalSheathPrototype sheath, ArousalState arousal)
    {
        return arousal switch
        {
            ArousalState.None => (sheath.RetractedOuter, sheath.RetractedInner),
            ArousalState.Partial => (sheath.EmergingOuter, sheath.EmergingInner),
            _ => (sheath.ErectOuter, null),
        };
    }

    /// <summary>Rooted RSI path for a prototype sprite path; relative paths are under /Textures.</summary>
    public static ResPath RsiPath(ResPath sprite)
    {
        return sprite.IsRooted ? sprite : SpriteSpecifierSerializer.TextureRoot / sprite;
    }
}
