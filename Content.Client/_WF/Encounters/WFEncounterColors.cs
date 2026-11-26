using Content.Shared._WF.Encounters;

namespace Content.Client._WF.Encounters;

/// <summary>The colours an encounter is drawn in on the radar and the ghost menu, when its ships have none of their own.</summary>
public static class WFEncounterColors
{
    public static Color Category(WFEncounterCategory category)
    {
        return category switch
        {
            WFEncounterCategory.Patrol => Color.FromHex("#6fb6ff"),
            WFEncounterCategory.Threat => Color.FromHex("#ff5c5c"),
            WFEncounterCategory.Distress => Color.FromHex("#ffae3d"),
            _ => Color.FromHex("#7fe0c8"),
        };
    }
}
