using Content.Shared._WF.Biotype;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    /// <summary>
    /// Organic or synthetic body. Default follows the species; validation turns it into the species' own.
    /// </summary>
    [DataField]
    public WFBiotype Biotype { get; set; }

    /// <summary>Copy of this profile with the given biotype.</summary>
    public HumanoidCharacterProfile WithBiotype(WFBiotype biotype)
    {
        return new(this) { Biotype = biotype };
    }
}
