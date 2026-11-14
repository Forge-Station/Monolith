using Content.Shared.Mining;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.AsteroidMining;

/// <summary>How a rock reads on a scan: its ore names, how hard an overcharge would blow, and how much its heat drifts.</summary>
public static class WFAsteroidScan
{
    /// <summary>Overcharge blast intensities below this read as low, and below <see cref="SevereBlast"/> as high.</summary>
    public const float HighBlast = 350f;
    public const float SevereBlast = 650f;

    /// <summary>Instability below this reads as steady, and below <see cref="ErraticInstability"/> as drifting.</summary>
    public const float DriftingInstability = 0.2f;
    public const float ErraticInstability = 0.35f;

    /// <summary>The name of the ore a vein drops, or the ore's ID if it has none.</summary>
    public static string OreName(IPrototypeManager prototypes, ProtoId<OrePrototype> ore)
    {
        return prototypes.TryIndex(ore, out var proto) && proto.OreEntity is { } entity &&
               prototypes.TryIndex(entity, out var entityProto)
            ? entityProto.Name
            : ore.Id;
    }

    /// <summary>How much a rock's ores feed an overcharge blast.</summary>
    public static float Volatility(List<WFAsteroidOre> ores)
    {
        var volatility = 0f;
        foreach (var ore in ores)
        {
            volatility += ore.Fraction * ore.Volatility;
        }

        return volatility;
    }

    /// <summary>The explosion intensity an overcharged rock goes off with; plasma-rich rocks reach much further.</summary>
    public static float BlastIntensity(float mass, float volatility)
    {
        return 150f + mass * 3f + volatility * 1500f;
    }

    /// <summary>The blast's band for a scan readout: low, high or severe.</summary>
    public static string BlastBand(float intensity)
    {
        return intensity < HighBlast ? "low" : intensity < SevereBlast ? "high" : "severe";
    }

    /// <summary>The heat drift's band for a scan readout: steady, drifting or erratic.</summary>
    public static string StabilityBand(float instability)
    {
        return instability < DriftingInstability ? "steady" : instability < ErraticInstability ? "drifting" : "erratic";
    }
}
