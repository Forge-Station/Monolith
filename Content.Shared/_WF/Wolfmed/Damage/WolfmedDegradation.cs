using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Wolfmed.Damage;

/// <summary>
/// What a limb looks like once its wounds have gone past being bruises. Two stages per material, plus
/// dead tissue, which is a state rather than a stage: a necrotic limb shows the same discolouration
/// whether it carries one wound or six.
/// </summary>
[Serializable, NetSerializable]
public enum WolfmedPartDegradation : byte
{
    None = 0,

    /// <summary>Organic stage 1: skin torn back off muscle.</summary>
    Muscle,

    /// <summary>Organic stage 2: the same wound down to bone.</summary>
    Bone,

    /// <summary>Mechanical stage 1: plating peeled off the frame.</summary>
    Struts,

    /// <summary>Mechanical stage 2: the frame opened onto loose wiring.</summary>
    Wiring,

    /// <summary>Dead tissue, whatever the severity.</summary>
    Necrotic,
}

/// <summary>
/// Severity thresholds and art for the per-part degradation overlay. One shipped profile
/// (<see cref="WolfmedDegradationVisualsComponent.DefaultProfile"/>); the prototype exists so the
/// thresholds and the RSI can be retuned or replaced without a code change.
/// </summary>
[Prototype("wolfmedDegradationProfile")]
public sealed partial class WolfmedDegradationProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The overlay art. One state per (visual layer, variant), four directions each.</summary>
    [DataField(required: true)]
    public ResPath Rsi = default!;

    /// <summary>Summed wound severity on a part before stage 1 shows.</summary>
    [DataField]
    public FixedPoint2 Stage1 = FixedPoint2.New(25);

    /// <summary>Summed wound severity on a part before stage 2 shows.</summary>
    [DataField]
    public FixedPoint2 Stage2 = FixedPoint2.New(60);

    /// <summary>RSI state suffix per stage. The state id is <c>{layer prefix}_{suffix}</c>.</summary>
    [DataField]
    public Dictionary<WolfmedPartDegradation, string> Suffixes = new();

    /// <summary>The stage this severity reaches on a part of this material.</summary>
    public WolfmedPartDegradation GetStage(FixedPoint2 severity, bool mechanical)
    {
        if (severity < Stage1)
            return WolfmedPartDegradation.None;

        if (severity < Stage2)
            return mechanical ? WolfmedPartDegradation.Struts : WolfmedPartDegradation.Muscle;

        return mechanical ? WolfmedPartDegradation.Wiring : WolfmedPartDegradation.Bone;
    }

    /// <summary>The RSI state for one layer at one stage, or null when the profile has no art for it.</summary>
    public string? GetState(HumanoidVisualLayers layer, WolfmedPartDegradation stage)
    {
        return WolfmedDegradationLayers.TryGetPrefix(layer, out var prefix) &&
               Suffixes.TryGetValue(stage, out var suffix)
            ? $"{prefix}_{suffix}"
            : null;
    }
}

/// <summary>
/// Opt-out and profile selector for the degradation overlay, read off the body. The masks the art is cut
/// from are human, so a species whose limbs sit somewhere else entirely turns the overlay off here rather
/// than shipping its own art.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedDegradationVisualsComponent : Component
{
    public const string DefaultProfile = "WFWolfmedDegradationDefault";

    /// <summary>Set false on a species whose silhouette the human-derived overlays do not fit.</summary>
    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    [DataField, AutoNetworkedField]
    public ProtoId<WolfmedDegradationProfilePrototype> Profile = DefaultProfile;
}

/// <summary>The visual layers the overlay covers, and their RSI state prefixes.</summary>
public static class WolfmedDegradationLayers
{
    /// <summary>
    /// Every layer with art. Unlike phase 3's damage overlay, hands and feet are not folded into the arm
    /// and leg: the overlay adds its own sprite layers, so it can use the hand and foot layers the stock
    /// six-layer <c>targetLayers</c> list has no art for.
    /// </summary>
    public static readonly HumanoidVisualLayers[] All =
    [
        HumanoidVisualLayers.Chest,
        HumanoidVisualLayers.Head,
        HumanoidVisualLayers.LArm,
        HumanoidVisualLayers.RArm,
        HumanoidVisualLayers.LHand,
        HumanoidVisualLayers.RHand,
        HumanoidVisualLayers.LLeg,
        HumanoidVisualLayers.RLeg,
        HumanoidVisualLayers.LFoot,
        HumanoidVisualLayers.RFoot,
    ];

    /// <summary>Maps a layer to its RSI state prefix. False for a layer the overlay has no art for.</summary>
    public static bool TryGetPrefix(HumanoidVisualLayers layer, out string prefix)
    {
        prefix = layer switch
        {
            HumanoidVisualLayers.Chest => "Chest",
            HumanoidVisualLayers.Head => "Head",
            HumanoidVisualLayers.LArm => "LArm",
            HumanoidVisualLayers.RArm => "RArm",
            HumanoidVisualLayers.LHand => "LHand",
            HumanoidVisualLayers.RHand => "RHand",
            HumanoidVisualLayers.LLeg => "LLeg",
            HumanoidVisualLayers.RLeg => "RLeg",
            HumanoidVisualLayers.LFoot => "LFoot",
            HumanoidVisualLayers.RFoot => "RFoot",
            _ => string.Empty,
        };
        return prefix.Length != 0;
    }
}
