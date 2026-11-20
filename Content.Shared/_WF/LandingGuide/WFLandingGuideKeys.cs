using Robust.Shared.Input;

namespace Content.Shared._WF.LandingGuide;

/// <summary>The landing guide's key, bound in the pilot's shuttle input context.</summary>
[KeyFunctions]
public static class WFLandingGuideKeys
{
    /// <summary>Turns the landing guide off and on again while piloting.</summary>
    public static readonly BoundKeyFunction WFToggleLandingGuide = "WFToggleLandingGuide";
}
