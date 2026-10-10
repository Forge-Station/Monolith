using Content.Server._Mono.AlertLevel;
using Content.Server._NF.SectorServices;
using Content.Server._WF.ColdWar;
using Content.Shared._Mono.Company;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Mono.WarDeclarator;

public sealed partial class ManualPortstrikeRuleSystem
{
    [Dependency] private IGameTiming _wfTiming = default!;
    [Dependency] private SectorServiceSystem _wfSectorService = default!;

    protected override void Started(EntityUid uid, ManualPortstrikeRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        var state = EnsureComp<WFColdWarComponent>(uid);
        state.AnnounceAt = _wfTiming.CurTime + state.AnnounceDelay;
    }

    protected override void ActiveTick(EntityUid uid, ManualPortstrikeRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (!TryComp<WFColdWarComponent>(uid, out var state) || state.AnnounceAt is not { } at || _wfTiming.CurTime < at)
            return;

        // Announcing the level it already has tells the round where it stands without changing anything.
        state.AnnounceAt = null;
        if (TryComp<WarLevelComponent>(_wfSectorService.GetServiceEntity(), out var level))
            _warLevelSystem.SetLevel(level.PostWar);
    }

    /// <summary>
    /// Flips the stance of the declarator's faction: a declaration of war or its withdrawal while the ceasefire
    /// holds, a ceasefire offer or its withdrawal during a war. The level moves once every faction asks for the
    /// same change. Always handles the use.
    /// </summary>
    private bool WfUseDeclarator(Entity<FactionWarDeclaratorComponent> ent, EntityUid user)
    {
        var faction = ent.Comp.Faction;
        var now = _wfTiming.CurTime;
        // Read once: with two rules for the same factions, the second must not see the level the first has just set.
        var level = _warLevelSystem.GetWarLevel(ent);
        var linked = false;
        var announced = false;
        var moved = false;
        var query = EntityQueryEnumerator<ManualPortstrikeRuleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.SectorStatus.ContainsKey(faction))
                continue;

            linked = true;
            var state = EnsureComp<WFColdWarComponent>(uid);
            if (state.NextUse.TryGetValue(faction, out var next) && now < next)
            {
                var seconds = (int) Math.Ceiling((next - now).TotalSeconds);
                _popup.PopupEntity(Loc.GetString("wf-cold-war-declarator-cooldown", ("seconds", seconds)), ent, user);
                continue;
            }

            var atWar = level == comp.WarLevel;
            // A level set from outside (an admin, a timed rule) leaves every stance behind it: bring them along.
            if (WfAllWant(comp, !atWar))
                WfSetAll(comp, atWar);

            var wantsWar = !comp.SectorStatus[faction];
            comp.SectorStatus[faction] = wantsWar;
            state.NextUse[faction] = now + state.Cooldown;

            if (!announced)
            {
                announced = true;
                var name = _prototypeManager.Index(faction).Name;
                var message = (atWar, wantsWar) switch
                {
                    (false, true) => Loc.GetString(ent.Comp.WarDeclarationMessage),
                    (false, false) => Loc.GetString("wf-cold-war-declaration-withdrawn", ("faction", name)),
                    (true, false) => Loc.GetString("wf-cold-war-ceasefire-offered", ("faction", name)),
                    (true, true) => Loc.GetString("wf-cold-war-ceasefire-withdrawn", ("faction", name)),
                };
                _radio.SendRadioMessage(ent, message, _prototypeManager.Index(ent.Comp.Channel), ent);
            }

            if (!moved && WfAllWant(comp, !atWar))
            {
                moved = true;
                _warLevelSystem.SetLevel(atWar ? !comp.WarLevel : comp.WarLevel);
            }
        }

        if (!linked)
            _popup.PopupEntity(Loc.GetString("wf-cold-war-declarator-unlinked"), ent, user);

        return true;
    }

    /// <summary>Whether every faction's stance is the given one.</summary>
    private static bool WfAllWant(ManualPortstrikeRuleComponent comp, bool war)
    {
        foreach (var stance in comp.SectorStatus.Values)
        {
            if (stance != war)
                return false;
        }

        return true;
    }

    private static void WfSetAll(ManualPortstrikeRuleComponent comp, bool war)
    {
        foreach (var faction in new List<ProtoId<CompanyPrototype>>(comp.SectorStatus.Keys))
        {
            comp.SectorStatus[faction] = war;
        }
    }
}
