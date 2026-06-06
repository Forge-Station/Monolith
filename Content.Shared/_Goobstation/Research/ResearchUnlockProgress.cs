using System.Linq;
using Content.Shared.Research.Prototypes;

namespace Content.Shared._Goobstation.Research;

/// <summary>
/// Progress (0–100) toward making a technology researchable from the console.
/// </summary>
public static class ResearchUnlockProgress
{
    public static byte Calculate(
        TechnologyPrototype technology,
        ResearchAvailability availability,
        IReadOnlySet<string> unlockedTechnologies,
        int researchPoints)
    {
        switch (availability)
        {
            case ResearchAvailability.Researched:
            case ResearchAvailability.Available:
                return 100;

            case ResearchAvailability.PrereqsMet:
                if (technology.Cost <= 0)
                    return 100;

                return (byte) Math.Clamp((long) researchPoints * 100 / technology.Cost, 0, 99);

            case ResearchAvailability.Unavailable:
            {
                var prereqs = technology.TechnologyPrerequisites;
                if (prereqs.Count == 0)
                    return 0;

                var met = prereqs.Count(p => unlockedTechnologies.Contains(p));
                return (byte) (met * 100 / prereqs.Count);
            }

            default:
                return 0;
        }
    }
}
