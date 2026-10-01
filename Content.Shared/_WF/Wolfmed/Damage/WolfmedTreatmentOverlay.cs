using Content.Shared._Onyx.Wounds;
using Content.Shared.Humanoid;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Wolfmed.Damage;

/// <summary>
/// What is tied around a limb right now (G3). One value per visual layer, so a limb shows the treatment
/// it is wearing and nothing else; a splint outranks a dressing because it is the bigger object.
/// </summary>
[Serializable, NetSerializable]
public enum WolfmedPartTreatment : byte
{
    None = 0,

    /// <summary>A dressing over a bleed: gauze, a medical patch, sutures under a wrap.</summary>
    Gauze,

    /// <summary>A manufactured splint holding a reduced fracture.</summary>
    Splint,

    /// <summary>A rod and a rag doing the same job.</summary>
    SplintImprovised,

    /// <summary>Bound wood. Nothing in this fork makes one; the art is here for a fork that does.</summary>
    SplintTribal,
}

/// <summary>Art and state mapping for the treatment overlays.</summary>
// Which bleeding states count as a dressing and the per-limb state suffixes are data here, so a species with a
// different leg shape ships a second profile instead of new code. One profile ships, the component's DefaultProfile.
[Prototype("wolfmedTreatmentOverlayProfile")]
public sealed partial class WolfmedTreatmentOverlayProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The overlay art. One state per (treatment, limb), four directions each.</summary>
    [DataField(required: true)]
    public ResPath Rsi = default!;

    /// <summary>RSI state prefix per treatment.</summary>
    [DataField]
    public Dictionary<WolfmedPartTreatment, string> Prefixes = new();

    /// <summary>RSI state suffix per visual layer. A layer with no entry gets no overlay.</summary>
    [DataField]
    public Dictionary<HumanoidVisualLayers, string> Suffixes = new();

    /// <summary>Bleeding treatments that mean a dressing is physically on the limb.</summary>
    [DataField]
    public HashSet<BleedingTreatment> Dressings = new();

    /// <summary>
    /// How long a dressing stays on the limb after the wound under it has closed. A bandage that vanished the
    /// moment the treatment worked told the medic nothing had been done.
    /// </summary>
    [DataField]
    public TimeSpan DressingLinger = TimeSpan.FromMinutes(5);

    /// <summary>The RSI state for one layer under one treatment, or null when there is no art for it.</summary>
    public string? GetState(HumanoidVisualLayers layer, WolfmedPartTreatment treatment)
    {
        return Prefixes.TryGetValue(treatment, out var prefix) && Suffixes.TryGetValue(layer, out var suffix)
            ? $"{prefix}_{suffix}"
            : null;
    }
}

/// <summary>
/// Opt-out and profile selector for the treatment overlays, read off the body. Same shape as
/// <see cref="WolfmedDegradationVisualsComponent"/>: the art is human, so a species it does not fit turns
/// it off here rather than drawing a bandage in the wrong place.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedTreatmentVisualsComponent : Component
{
    public const string DefaultProfile = "WFWolfmedTreatmentOverlayDefault";

    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    [DataField, AutoNetworkedField]
    public ProtoId<WolfmedTreatmentOverlayProfilePrototype> Profile = DefaultProfile;
}

/// <summary>The visual layers the treatment overlays cover, and where the extremities fold into.</summary>
public static class WolfmedTreatmentLayers
{
    /// <summary>
    /// Six layers, not the degradation overlay's ten: the art has one arm band and one leg band, so a
    /// dressing on a hand shows on the arm it is attached to.
    /// </summary>
    public static readonly HumanoidVisualLayers[] All =
    [
        HumanoidVisualLayers.Head,
        HumanoidVisualLayers.Chest,
        HumanoidVisualLayers.RArm,
        HumanoidVisualLayers.LArm,
        HumanoidVisualLayers.RLeg,
        HumanoidVisualLayers.LLeg,
    ];

    /// <summary>Folds a hand into its arm and a foot into its leg. Anything else maps to itself.</summary>
    public static HumanoidVisualLayers Fold(HumanoidVisualLayers layer) => layer switch
    {
        HumanoidVisualLayers.LHand => HumanoidVisualLayers.LArm,
        HumanoidVisualLayers.RHand => HumanoidVisualLayers.RArm,
        HumanoidVisualLayers.LFoot => HumanoidVisualLayers.LLeg,
        HumanoidVisualLayers.RFoot => HumanoidVisualLayers.RLeg,
        _ => layer,
    };

    /// <summary>Which treatment wins when one limb wears two. The bigger object is the one you see.</summary>
    public static int Rank(WolfmedPartTreatment treatment) => treatment switch
    {
        WolfmedPartTreatment.Gauze => 1,
        WolfmedPartTreatment.SplintTribal => 2,
        WolfmedPartTreatment.SplintImprovised => 3,
        WolfmedPartTreatment.Splint => 4,
        _ => 0,
    };
}
