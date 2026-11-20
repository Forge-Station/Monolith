using Content.Client.Players.PlayTimeTracking;
using Content.Shared._WF.Newbie;
using Content.Shared.Traits;
using Robust.Shared.Configuration;

namespace Content.Client._WF.Newbie;

/// <summary>Whether the local player's newbie state closes a trait to them.</summary>
public static class WFNewbieTraitRules
{
    public static bool TraitBlocked(TraitPrototype trait, IConfigurationManager cfg, JobRequirementsManager requirements)
    {
        if (!trait.NewbieBlocked || !cfg.GetCVar(WFNewbieCVars.ShowTag))
            return false;

        var hours = cfg.GetCVar(WFNewbieCVars.Hours);
        return hours > 0 && requirements.FetchOverallPlaytime() < TimeSpan.FromHours(hours);
    }
}
