namespace Content.Shared.Humanoid;

public sealed partial class HumanoidAppearanceComponent
{
    /// <summary>
    /// The base layers the profile's augments last set, so a reload can take back the ones nothing has replaced since.
    /// </summary>
    [DataField]
    public Dictionary<HumanoidVisualLayers, CustomBaseLayerInfo> AugmentLayers = new();

    /// <summary>The layers the profile's augments last hid, for limbs picked as missing.</summary>
    [DataField]
    public HashSet<HumanoidVisualLayers> AugmentHidden = new();
}
