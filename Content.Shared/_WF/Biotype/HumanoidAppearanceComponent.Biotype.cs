using Content.Shared._WF.Biotype;

namespace Content.Shared.Humanoid;

public sealed partial class HumanoidAppearanceComponent
{
    /// <summary>The body's effective biotype, set when a profile loads. Never Default once a profile is on.</summary>
    [DataField, AutoNetworkedField]
    public WFBiotype Biotype;
}
