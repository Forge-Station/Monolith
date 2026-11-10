using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Random;

namespace Content.Server._WF.Remade.Sector;

/// <summary>
/// A while into the round, and rarely, places one dormant Scion in the sector for a crew to find. At most one is wild at a
/// time, and none follows soon after one is gone.
/// </summary>
public sealed class WFRemadeDiscoveryRuleSystem : GameRuleSystem<WFRemadeDiscoveryRuleComponent>
{
    [Dependency] private WFRemadeSiteSystem _sites = default!;

    protected override void Started(EntityUid uid, WFRemadeDiscoveryRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        component.Next = Timing.CurTime + TimeSpan.FromSeconds(component.EarliestStart);
    }

    protected override void ActiveTick(EntityUid uid, WFRemadeDiscoveryRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        var now = Timing.CurTime;
        if (now < component.Next)
            return;

        component.Next = now + TimeSpan.FromSeconds(RobustRandom.NextFloat(component.CheckMin, MathF.Max(component.CheckMin, component.CheckMax)));
        if (!_sites.Enabled
            || !_sites.CanStartWild(component.MinGap, component.GoneCooldown, out _)
            || !RobustRandom.Prob(component.Chance))
            return;

        if (_sites.PickDiscovery() is not { } site)
            return;

        if (_sites.TryStartSite(site, null, announce: true, out var encounter))
            Log.Info($"Discovery rule placed {site.ID} as {ToPrettyString(encounter)}.");
    }
}
