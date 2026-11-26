using Robust.Shared.Input;

namespace Content.Shared._WF.MappingMode;

/// <summary>Keys of the mapping screen: build modes, the mode's mouse buttons, template nudges and the link tool.</summary>
[KeyFunctions]
public static class WFMappingKeyFunctions
{
    public static readonly BoundKeyFunction WFMappingMode1 = "WFMappingMode1";
    public static readonly BoundKeyFunction WFMappingMode2 = "WFMappingMode2";
    public static readonly BoundKeyFunction WFMappingMode3 = "WFMappingMode3";
    public static readonly BoundKeyFunction WFMappingMode4 = "WFMappingMode4";
    public static readonly BoundKeyFunction WFMappingMode5 = "WFMappingMode5";
    public static readonly BoundKeyFunction WFMappingMode6 = "WFMappingMode6";
    public static readonly BoundKeyFunction WFMappingMode7 = "WFMappingMode7";
    public static readonly BoundKeyFunction WFMappingMode8 = "WFMappingMode8";
    public static readonly BoundKeyFunction WFMappingMode9 = "WFMappingMode9";
    public static readonly BoundKeyFunction WFMappingMode10 = "WFMappingMode10";
    public static readonly BoundKeyFunction WFMappingModePrimary = "WFMappingModePrimary";
    public static readonly BoundKeyFunction WFMappingModeSecondary = "WFMappingModeSecondary";
    public static readonly BoundKeyFunction WFMappingModeMiddle = "WFMappingModeMiddle";
    public static readonly BoundKeyFunction WFMappingModeCancel = "WFMappingModeCancel";
    public static readonly BoundKeyFunction WFMappingTemplateNudgeUp = "WFMappingTemplateNudgeUp";
    public static readonly BoundKeyFunction WFMappingTemplateNudgeDown = "WFMappingTemplateNudgeDown";
    public static readonly BoundKeyFunction WFMappingTemplateNudgeLeft = "WFMappingTemplateNudgeLeft";
    public static readonly BoundKeyFunction WFMappingTemplateNudgeRight = "WFMappingTemplateNudgeRight";

    /// <summary>Tab in Link mode: the next sub-tool.</summary>
    public static readonly BoundKeyFunction WFMappingLinkCycle = "WFMappingLinkCycle";

    /// <summary>Ctrl+S: saves the session to the player's library under its name, as the menu bar's Quicksave.</summary>
    public static readonly BoundKeyFunction WFMappingQuicksave = "WFMappingQuicksave";

    /// <summary>Leaves a live test for the mapping observer, as the test banner's button; bound to nothing by default.</summary>
    public static readonly BoundKeyFunction WFMappingTestReturn = "WFMappingTestReturn";

    /// <summary>The mode keys, Place first.</summary>
    public static readonly BoundKeyFunction[] Modes =
    {
        WFMappingMode1,
        WFMappingMode2,
        WFMappingMode3,
        WFMappingMode4,
        WFMappingMode5,
        WFMappingMode6,
        WFMappingMode7,
        WFMappingMode8,
        WFMappingMode9,
        WFMappingMode10,
    };

    /// <summary>The template's arrow keys, added while Template mode is on.</summary>
    public static readonly BoundKeyFunction[] Nudges =
    {
        WFMappingTemplateNudgeUp,
        WFMappingTemplateNudgeDown,
        WFMappingTemplateNudgeLeft,
        WFMappingTemplateNudgeRight,
    };
}
