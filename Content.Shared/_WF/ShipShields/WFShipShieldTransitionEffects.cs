using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipShields;

/// <summary>The visual transition of a ship shield field.</summary>
[Serializable, NetSerializable]
public enum WFShipShieldTransition : byte
{
    None,
    Forming,
    Collapsing,
    Lowering,
}

/// <summary>Controls field transition timing independently of interception.</summary>
public static class WFShipShieldTransitionEffects
{
    /// <summary>Returns the visual transition duration in seconds.</summary>
    public static float Duration(WFShipShieldTransition transition)
    {
        return transition switch
        {
            WFShipShieldTransition.Forming => 1f,
            WFShipShieldTransition.Collapsing => 0.9f,
            WFShipShieldTransition.Lowering => 0.7f,
            _ => 0f,
        };
    }

    /// <summary>Returns clamped transition progress for an elapsed server time.</summary>
    public static float Progress(WFShipShieldTransition transition, float elapsed)
    {
        var duration = Duration(transition);
        return duration > 0f ? Math.Clamp(elapsed / duration, 0f, 1f) : 1f;
    }
}
