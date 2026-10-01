using Content.Shared.Body.Part;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Wolfmed.Damage;

/// <summary>What the open wound on a limb looks like on the humanoid sprite (VISUALS).</summary>
[Serializable, NetSerializable]
public enum WolfmedWoundOverlay : byte
{
    None = 0,

    /// <summary>Still open but not bleeding: clotted, dressed, or a stump that has stopped.</summary>
    Old,

    /// <summary>Bleeding: a drip runs from the wound.</summary>
    Drip,

    /// <summary>Bleeding at or past wolfmed.wound_overlay_stream_rate: a steady trickle.</summary>
    Stream,
}

/// <summary>
/// An artery on the humanoid sprite (playtest 4): a cut artery on a part, or the stump where a part was. Drawn as the
/// still artery; the spray plays once per blood spurt while the look is Bleeding.
/// </summary>
[Serializable, NetSerializable]
public enum WolfmedArteryOverlay : byte
{
    None = 0,

    /// <summary>The artery is open but not spurting: clamped, dressed, tied off or clotted.</summary>
    Still,

    /// <summary>Spurting, by the same rule the blood spurts use: the spray plays on each of them.</summary>
    Bleeding,
}

/// <summary>Where an artery overlay is drawn: the head's own artery, the neck once the head is off, or a limb.</summary>
[Serializable, NetSerializable]
public enum WolfmedArterySite : byte
{
    Head,
    Neck,
    LArm,
    RArm,
    LHand,
    RHand,
    LLeg,
    RLeg,
    LFoot,
    RFoot,
}

/// <summary>Which artery site a part stands for, and which layer's species shift the site takes.</summary>
public static class WolfmedArterySites
{
    /// <summary>
    /// The site for a part: its own artery when attached, its stump when it is off. A head off is the neck; a torso
    /// or an unknown part has none.
    /// </summary>
    public static WolfmedArterySite? ForPart(BodyPartType type, BodyPartSymmetry symmetry, bool stump)
    {
        var left = symmetry == BodyPartSymmetry.Left;
        return type switch
        {
            BodyPartType.Head => stump ? WolfmedArterySite.Neck : WolfmedArterySite.Head,
            BodyPartType.Arm => left ? WolfmedArterySite.LArm : WolfmedArterySite.RArm,
            BodyPartType.Hand => left ? WolfmedArterySite.LHand : WolfmedArterySite.RHand,
            BodyPartType.Leg => left ? WolfmedArterySite.LLeg : WolfmedArterySite.RLeg,
            BodyPartType.Foot => left ? WolfmedArterySite.LFoot : WolfmedArterySite.RFoot,
            _ => null,
        };
    }

    /// <summary>The humanoid layer whose species shift the site follows; the neck follows the head.</summary>
    public static HumanoidVisualLayers Layer(WolfmedArterySite site) => site switch
    {
        WolfmedArterySite.Head or WolfmedArterySite.Neck => HumanoidVisualLayers.Head,
        WolfmedArterySite.LArm => HumanoidVisualLayers.LArm,
        WolfmedArterySite.RArm => HumanoidVisualLayers.RArm,
        WolfmedArterySite.LHand => HumanoidVisualLayers.LHand,
        WolfmedArterySite.RHand => HumanoidVisualLayers.RHand,
        WolfmedArterySite.LLeg => HumanoidVisualLayers.LLeg,
        WolfmedArterySite.RLeg => HumanoidVisualLayers.RLeg,
        WolfmedArterySite.LFoot => HumanoidVisualLayers.LFoot,
        _ => HumanoidVisualLayers.RFoot,
    };

    /// <summary>The RSI state prefix: head, neck, r_arm, l_foot and so on.</summary>
    public static string Prefix(WolfmedArterySite site) => site switch
    {
        WolfmedArterySite.Head => "head",
        WolfmedArterySite.Neck => "neck",
        WolfmedArterySite.LArm => "l_arm",
        WolfmedArterySite.RArm => "r_arm",
        WolfmedArterySite.LHand => "l_hand",
        WolfmedArterySite.RHand => "r_hand",
        WolfmedArterySite.LLeg => "l_leg",
        WolfmedArterySite.RLeg => "r_leg",
        WolfmedArterySite.LFoot => "l_foot",
        _ => "r_foot",
    };
}

/// <summary>The wound and rot overlay art, and the RSI state each (layer, look) pair draws.</summary>
// The states are baked per part and per direction by Tools/_WF/Wolfmed/gen_wolfmed_overlays.py, so the layer map
// is the same ten layers the degradation overlay covers, with the same state prefixes.
public static class WolfmedWoundOverlays
{
    public static readonly ResPath WoundRsi = new("_WF/Wolfmed/Damage/wounds.rsi");
    public static readonly ResPath RotRsi = new("_WF/Wolfmed/Damage/rot.rsi");
    public static readonly ResPath ArteryRsi = new("_WF/Wolfmed/Damage/artery.rsi");
    public static readonly ResPath StumpRsi = new("_WF/Wolfmed/Damage/stumps.rsi");

    /// <summary>The wound state for one layer, or null for none or a layer with no art.</summary>
    public static string? GetWoundState(HumanoidVisualLayers layer, WolfmedWoundOverlay overlay)
    {
        var suffix = overlay switch
        {
            WolfmedWoundOverlay.Old => "old",
            WolfmedWoundOverlay.Drip => "drip",
            WolfmedWoundOverlay.Stream => "stream",
            _ => null,
        };
        return suffix != null && WolfmedDegradationLayers.TryGetPrefix(layer, out var prefix) ? $"{prefix}_{suffix}" : null;
    }

    /// <summary>The rot state for one layer, or null for a layer with no art. The chest state carries the groin too.</summary>
    public static string? GetRotState(HumanoidVisualLayers layer) =>
        WolfmedDegradationLayers.TryGetPrefix(layer, out var prefix) ? $"{prefix}_rot" : null;

    /// <summary>The artery state for one site: &lt;site&gt;_artery0 (still) or _artery1 (the spray), null for none.</summary>
    public static string? GetArteryState(WolfmedArterySite site, WolfmedArteryOverlay overlay)
    {
        if (overlay == WolfmedArteryOverlay.None)
            return null;

        return $"{WolfmedArterySites.Prefix(site)}_artery{(overlay == WolfmedArteryOverlay.Bleeding ? 1 : 0)}";
    }

    /// <summary>The stump's flesh state for a site where a limb was, null for the head (a head off is the neck).</summary>
    public static string? GetStumpState(WolfmedArterySite site) =>
        site == WolfmedArterySite.Head ? null : $"{WolfmedArterySites.Prefix(site)}_stump";

    /// <summary>The stump's bone and outline, drawn in their own colours over the flesh.</summary>
    public static string? GetStumpBoneState(WolfmedArterySite site) =>
        site == WolfmedArterySite.Head ? null : $"{WolfmedArterySites.Prefix(site)}_stump_bone";

    /// <summary>The drip or trickle hung from the stump while it bleeds; null for a stump that has stopped.</summary>
    public static string? GetStumpBleedState(WolfmedArterySite site, WolfmedWoundOverlay look)
    {
        if (site == WolfmedArterySite.Head)
            return null;

        return look switch
        {
            WolfmedWoundOverlay.Drip => $"{WolfmedArterySites.Prefix(site)}_stump_drip",
            WolfmedWoundOverlay.Stream => $"{WolfmedArterySites.Prefix(site)}_stump_stream",
            _ => null,
        };
    }

    /// <summary>Which look wins when one layer has two parts behind it (a second left arm).</summary>
    public static int Rank(WolfmedWoundOverlay overlay) => (int) overlay;
}

/// <summary>
/// Per-species pixel shifts for the wound and rot overlays, which are drawn on the human silhouette. Generated by
/// Tools/_WF/Wolfmed/gen_overlay_offsets.py; a species or a layer missing from the table is not shifted.
/// </summary>
[Prototype("wolfmedOverlayOffsets")]
public sealed partial class WolfmedOverlayOffsetsPrototype : IPrototype
{
    public const string Default = "WFWolfmedOverlayOffsets";

    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Species, then layer, to a shift in pixels, up and right positive. Applied in every direction.</summary>
    [DataField]
    public Dictionary<ProtoId<SpeciesPrototype>, Dictionary<HumanoidVisualLayers, Vector2i>> Species = new();

    /// <summary>The shift for one species' layer, zero when the table has none.</summary>
    public Vector2i Get(ProtoId<SpeciesPrototype> species, HumanoidVisualLayers layer) =>
        Species.TryGetValue(species, out var layers) && layers.TryGetValue(layer, out var offset)
            ? offset
            : Vector2i.Zero;
}
