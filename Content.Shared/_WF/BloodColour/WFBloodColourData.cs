using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.BloodColour;

/// <summary>Reagent data that paints one character's blood a colour of their own, in place of the reagent's.</summary>
[Serializable, NetSerializable]
public sealed partial class WFBloodColourData : ReagentData
{
    [DataField]
    public Color Color = Color.White;

    public override ReagentData Clone() => this;

    public override bool Equals(ReagentData? other)
    {
        return other is WFBloodColourData data && data.Color == Color;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(typeof(WFBloodColourData), Color);
    }

    /// <summary>The colour a reagent's data overrides it with, or null when it has none.</summary>
    public static Color? ColourOf(ReagentId reagent)
    {
        if (reagent.Data == null)
            return null;

        foreach (var data in reagent.Data)
        {
            if (data is WFBloodColourData colour)
                return colour.Color;
        }

        return null;
    }
}
