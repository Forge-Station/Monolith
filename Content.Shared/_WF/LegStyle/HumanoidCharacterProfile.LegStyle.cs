using Content.Shared._WF.LegStyle;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    /// <summary>
    /// The legs picked in the creator. Default draws the species' own.
    /// </summary>
    [DataField]
    public LegStance LegStance { get; set; }

    /// <summary>Copy of this profile with the given leg stance.</summary>
    public HumanoidCharacterProfile WithLegStance(LegStance stance)
    {
        return new(this) { LegStance = stance };
    }
}
