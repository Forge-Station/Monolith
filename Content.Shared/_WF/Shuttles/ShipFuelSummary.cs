using Robust.Shared.Serialization;

namespace Content.Shared._WF.Shuttles;

/// <summary>Mean normalized reserves of installed fuel sources, rather than a runtime or raw fuel sum.</summary>
[Serializable, NetSerializable]
public struct ShipFuelSummary
{
    /// <summary>Reserve fraction at or below which FUEL LOW lights steadily.</summary>
    public const float LowThreshold = 0.2f;

    /// <summary>Reserve fraction below which FUEL LOW flashes.</summary>
    public const float CriticalThreshold = 0.1f;

    /// <summary>Mean remaining fraction among sources with known capacity, from zero to one.</summary>
    public float Fraction;

    /// <summary>Installed conventional generators, antimatter injectors and fission reactors.</summary>
    public int Sources;

    /// <summary>Installed sources whose fuel capacity could not be measured.</summary>
    public int UnknownSources;

    /// <summary>Whether the reading covers every installed fuel source.</summary>
    public readonly bool Available => Sources > 0 && UnknownSources == 0;

    /// <summary>Unknown or absent fuel sources must not appear as an empty ship.</summary>
    public readonly bool Low => Available && Fraction <= LowThreshold;

    /// <summary>Reserves below the critical threshold, which also makes the reading <see cref="Low"/>.</summary>
    public readonly bool Critical => Available && Fraction < CriticalThreshold;
}
