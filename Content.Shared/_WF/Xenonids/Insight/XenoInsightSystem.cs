using Content.Shared._WF.RMCCore.Actions;
using Content.Shared._WF.RMCCore.Emote;
using Content.Shared._WF.Xenonids.DeployTraps;
using Content.Shared.Popups;

namespace Content.Shared._WF.Xenonids.Insight;

public sealed partial class XenoInsightSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedRMCEmoteSystem _emote = default!;
    [Dependency] private SharedRMCActionsSystem _rmcActions = default!;

    public int GetInsight(EntityUid uid)
    {
        if (!TryComp<XenoInsightComponent>(uid, out var insight))
            return 0;

        return insight.Insight;
    }

    public void IncrementInsight(Entity<XenoInsightComponent?> xeno, int amount)
    {
        if (!Resolve(xeno, ref xeno.Comp, false))
            return;

        var wasMaxed = xeno.Comp.Insight >= xeno.Comp.MaxInsight;
        xeno.Comp.Insight += amount;
        xeno.Comp.Insight = Math.Clamp(xeno.Comp.Insight, 0, xeno.Comp.MaxInsight);
        Dirty(xeno);

        if (!wasMaxed && xeno.Comp.Insight >= xeno.Comp.MaxInsight)
            InsightEmpower((xeno.Owner, xeno.Comp));
    }

    public void InsightEmpower(Entity<XenoInsightComponent> xeno)
    {
        if (TryComp(xeno.Owner, out XenoDeployTrapsComponent? deployTraps))
        {
            deployTraps.Empowered = true;
            foreach (var action in _rmcActions.GetActionsWithEvent<XenoDeployTrapsActionEvent>(xeno.Owner))
            {
                _rmcActions.SetIcon(action.Owner, deployTraps.ActionIconEmpowered);
            }
        }

        if (xeno.Comp.Emote is { } emote)
            _emote.TryEmoteWithChat(xeno.Owner, emote, false, null, false, true);

        _popup.PopupClient(Loc.GetString("rmc-xeno-insight-empower"), xeno, xeno, PopupType.Medium);
    }
}

