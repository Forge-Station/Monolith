using Robust.Shared.Serialization;

namespace Content.Shared._WF.AsteroidMining;

/// <summary>Sets the power, from zero to one, of every mining laser on the gunnery console's ship.</summary>
[Serializable, NetSerializable]
public sealed class WFMiningBeamPowerMessage(float power) : BoundUserInterfaceMessage
{
    public readonly float Power = power;
}

/// <summary>The mining lasers of a gunnery console's ship and the rock they are working, for its mining panel.</summary>
[Serializable, NetSerializable]
public sealed class WFMiningBeamGunneryUpdateMessage(WFMiningBeamGunneryState state) : BoundUserInterfaceMessage
{
    public readonly WFMiningBeamGunneryState State = state;
}

[Serializable, NetSerializable]
public readonly record struct WFMiningBeamGunneryState(
    int Lasers,
    float Power,
    // Seconds until an overloaded laser can fire again.
    float Cooldown,
    WFMiningBeamTargetEntry? Target);

[Serializable, NetSerializable]
public readonly record struct WFMiningBeamOreEntry(string Name, float Fraction, Color Color);

[Serializable, NetSerializable]
public readonly record struct WFMiningBeamTargetEntry(
    string Name,
    float Mass,
    WFMiningBeamOreEntry[] Ores,
    float Resistance,
    float WindowLow,
    float WindowHigh,
    float Charge,
    float Progress,
    // Overcharge time as a share of the time it takes to blow up.
    float Overcharge,
    // Share of the mass a crack would keep now, after time spent in the hot band.
    float Yield);
