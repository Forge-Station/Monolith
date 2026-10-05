using Content.Shared.Atmos;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Caverns;

/// <summary>What the air rising from a shaft is like, as its examine reports it.</summary>
[Serializable, NetSerializable]
public enum WFCavernAir : byte
{
    /// <summary>Safe to breathe.</summary>
    Breathable,

    /// <summary>Breathable, but with ammonia or nitrous oxide in it.</summary>
    Foul,

    /// <summary>Too little oxygen to breathe.</summary>
    Thin,

    /// <summary>Carbon dioxide, plasma or tritium enough to poison.</summary>
    Toxic,

    /// <summary>Hot enough to burn.</summary>
    Scalding,

    /// <summary>Cold enough to freeze.</summary>
    Freezing,
}

/// <summary>Sorts a gas mixture into a <see cref="WFCavernAir"/> reading.</summary>
public static class WFCavernAirClassifier
{
    /// <summary>Hotter than this, in kelvin, is scalding.</summary>
    public const float ScaldingAbove = 330f;

    /// <summary>Colder than this, in kelvin, is freezing.</summary>
    public const float FreezingBelow = 260f;

    /// <summary>At least this much carbon dioxide, in kPa, is toxic.</summary>
    public const float ToxicCarbonDioxide = 5f;

    /// <summary>Less oxygen than this, in kPa, is too thin to breathe.</summary>
    public const float ThinOxygen = 16f;

    /// <summary>Classifies a mixture: heat, cold, poison, thin air and stink, in that order.</summary>
    public static WFCavernAir Classify(GasMixture mixture)
    {
        if (mixture.Temperature > ScaldingAbove)
            return WFCavernAir.Scalding;

        if (mixture.Temperature < FreezingBelow)
            return WFCavernAir.Freezing;

        if (PartialPressure(mixture, Gas.CarbonDioxide) >= ToxicCarbonDioxide
            || mixture.GetMoles(Gas.Plasma) > 0f
            || mixture.GetMoles(Gas.Tritium) > 0f)
            return WFCavernAir.Toxic;

        if (PartialPressure(mixture, Gas.Oxygen) < ThinOxygen)
            return WFCavernAir.Thin;

        if (mixture.GetMoles(Gas.Ammonia) > 0f || mixture.GetMoles(Gas.NitrousOxide) > 0f)
            return WFCavernAir.Foul;

        return WFCavernAir.Breathable;
    }

    /// <summary>One gas's partial pressure in kPa: moles × R × T / volume.</summary>
    public static float PartialPressure(GasMixture mixture, Gas gas)
    {
        if (mixture.Volume <= 0f)
            return 0f;

        return mixture.GetMoles(gas) * Atmospherics.R * mixture.Temperature / mixture.Volume;
    }
}
