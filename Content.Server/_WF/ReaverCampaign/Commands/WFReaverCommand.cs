using System.Globalization;
using System.Linq;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Server.Administration;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.SectorControl;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.ReaverCampaign.Commands;

/// <summary>The Reaver campaign from the console: show it, found a stronghold or send a convoy or a reinforcement, set the tier, step it, send an intercept, pause it or clear it.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WFReaverCommand : LocalizedEntityCommands
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WFReaverCampaignSystem _campaign = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;

    private static readonly string[] Subcommands = { "status", "found", "convoy", "reinforce", "tier", "spread", "patrol", "raid", "intercept", "pause", "resume", "clear" };

    public override string Command => "wf_reavers";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        switch (args.Length > 0 ? args[0] : string.Empty)
        {
            case "status" when args.Length == 1:
                Status(shell);
                break;
            case "found" when args.Length is 1 or 3:
                Found(shell, args);
                break;
            case "convoy" when args.Length == 1:
                Convoy(shell);
                break;
            case "reinforce" when args.Length == 2:
                Reinforce(shell, args[1]);
                break;
            case "tier" when args.Length == 2:
                if (!int.TryParse(args[1], out var tier) || tier < 0 || tier > WFReaverCampaignSystem.MaxTier)
                {
                    shell.WriteLine(Loc.GetString("cmd-wf_reavers-bad-tier", ("arg", args[1]), ("max", WFReaverCampaignSystem.MaxTier)));
                    break;
                }

                if (_campaign.Campaign == null)
                {
                    shell.WriteLine(Loc.GetString("cmd-wf_reavers-no-campaign"));
                    break;
                }

                _campaign.SetTier(tier);
                shell.WriteLine(Loc.GetString("cmd-wf_reavers-tier-set", ("tier", _campaign.TierName(tier))));
                break;
            case "spread" when args.Length == 1:
                if (_campaign.TrySpreadOnce())
                    shell.WriteLine(Loc.GetString("cmd-wf_reavers-spread"));
                else
                    Failed(shell, "cmd-wf_reavers-spread-failed");
                break;
            case "patrol" when args.Length == 1:
                Patrol(shell);
                break;
            case "raid" when args.Length == 1:
                if (_campaign.TryLaunchRaid())
                    shell.WriteLine(Loc.GetString("cmd-wf_reavers-raid"));
                else
                    Failed(shell, "cmd-wf_reavers-raid-failed");
                break;
            case "intercept" when args.Length == 2:
                Intercept(shell, args[1]);
                break;
            case "pause" when args.Length == 1:
                _campaign.Paused = true;
                shell.WriteLine(Loc.GetString("cmd-wf_reavers-paused"));
                break;
            case "resume" when args.Length == 1:
                _campaign.Paused = false;
                shell.WriteLine(Loc.GetString("cmd-wf_reavers-resumed"));
                break;
            case "clear" when args.Length == 1:
                shell.WriteLine(Loc.GetString("cmd-wf_reavers-cleared", ("count", _campaign.ClearCampaign())));
                break;
            default:
                shell.WriteLine(Help);
                break;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(Subcommands, Loc.GetString("cmd-wf_reavers-hint-sub")),
            2 when args[0] == "found" => CompletionResult.FromHint(Loc.GetString("cmd-wf_reavers-hint-x")),
            3 when args[0] == "found" => CompletionResult.FromHint(Loc.GetString("cmd-wf_reavers-hint-y")),
            2 when args[0] == "intercept" => CompletionResult.FromHint(Loc.GetString("cmd-wf_reavers-hint-grid")),
            2 when args[0] == "reinforce" => CompletionResult.FromHint(Loc.GetString("cmd-wf_reavers-hint-stronghold")),
            2 when args[0] == "tier" => CompletionResult.FromHintOptions(
                Enumerable.Range(0, WFReaverCampaignSystem.MaxTier + 1).Select(tier => tier.ToString()),
                Loc.GetString("cmd-wf_reavers-hint-tier")),
            _ => CompletionResult.Empty,
        };
    }

    private void Status(IConsoleShell shell)
    {
        if (_campaign.Campaign is not { } campaign)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_reavers-no-campaign"));
            return;
        }

        var state = _campaign.State();
        var strongholds = _campaign.Strongholds();
        var patrols = _campaign.Patrols();
        var now = _timing.CurTime;
        shell.WriteLine(Loc.GetString("cmd-wf_reavers-status-header",
            ("campaign", campaign.ID),
            ("running", _campaign.Running.ToString()),
            ("paused", _campaign.Held.ToString()),
            ("tier", _campaign.TierName(state?.Tier ?? 0)),
            ("score", (state?.Score ?? 0f).ToString("0.0", CultureInfo.InvariantCulture))));
        shell.WriteLine(Loc.GetString("cmd-wf_reavers-status-counts",
            ("strongholds", strongholds.Count),
            ("cells", _territory.Held(_territory.SectorMap, campaign.Faction).Count),
            ("patrols", patrols.Count),
            ("raids", _campaign.ActiveRaids())));
        shell.WriteLine(Loc.GetString("cmd-wf_reavers-status-clocks",
            ("spread", Seconds(state?.NextSpread, now)),
            ("raid", Seconds(state?.NextRaid, now))));

        if (strongholds.Count == 0)
            shell.WriteLine(WhyNoStronghold(campaign, state, now));

        foreach (var (uid, stronghold, encounter) in strongholds)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_reavers-status-stronghold",
                ("uid", EntityManager.GetNetEntity(uid).ToString()),
                ("callsign", WFSectorHex.Callsign(stronghold.Cell)),
                ("level", stronghold.Level),
                ("prototype", encounter.Prototype.Id),
                ("hits", stronghold.Hits),
                ("patrols", patrols.Count(patrol => patrol.Comp1.Stronghold == uid)),
                ("reinforcements", stronghold.Reinforcements.Count),
                ("state", (stronghold.Inbound != null ? Loc.GetString("cmd-wf_reavers-status-stronghold-inbound") : string.Empty)
                    + (stronghold.Scattered ? Loc.GetString("cmd-wf_reavers-status-stronghold-scattered") : string.Empty))));
        }

        foreach (var (uid, reinforcement, encounter) in _campaign.Reinforcements())
        {
            var hubEncounter = EntityManager.GetComponentOrNull<WFEncounterComponent>(reinforcement.Stronghold);
            var home = EntityManager.GetComponentOrNull<WFReaverStrongholdComponent>(reinforcement.Stronghold);
            var range = "-";
            if (hubEncounter != null && home != null && hubEncounter.Ships.TryGetValue(home.Lead, out var hub)
                && encounter.Ships.TryGetValue(reinforcement.Lead, out var ship)
                && EntityManager.EntityExists(hub.Grid) && EntityManager.EntityExists(ship.Grid))
            {
                var position = EntityManager.System<SharedTransformSystem>();
                range = WFReaverCampaignSystem.Kilometres(position.GetWorldPosition(ship.Grid), position.GetWorldPosition(hub.Grid));
            }

            shell.WriteLine(Loc.GetString("cmd-wf_reavers-status-reinforcement",
                ("uid", EntityManager.GetNetEntity(uid).ToString()),
                ("callsign", home != null ? WFSectorHex.Callsign(home.Cell) : "-"),
                ("level", reinforcement.Level),
                ("phase", reinforcement.Phase.ToString()),
                ("range", range),
                ("seconds", Math.Max(0, (int) (now - reinforcement.Launched).TotalSeconds))));
        }

        var transform = EntityManager.System<SharedTransformSystem>();
        foreach (var (uid, convoy, encounter) in _campaign.Convoys())
        {
            var at = encounter.Ships.TryGetValue(convoy.Lead, out var lead) && EntityManager.EntityExists(lead.Grid)
                ? transform.GetWorldPosition(lead.Grid)
                : encounter.Origin.Position;
            shell.WriteLine(Loc.GetString("cmd-wf_reavers-status-convoy",
                ("uid", EntityManager.GetNetEntity(uid).ToString()),
                ("callsign", WFSectorHex.Callsign(convoy.Target)),
                ("range", WFReaverCampaignSystem.Kilometres(at, convoy.TargetPosition)),
                ("seconds", Math.Max(0, (int) (now - convoy.Launched).TotalSeconds))));
        }

        foreach (var (uid, patrol, _) in patrols)
        {
            if (patrol.Intercepting is not { } target)
                continue;

            shell.WriteLine(Loc.GetString("cmd-wf_reavers-status-intercept",
                ("patrol", EntityManager.GetNetEntity(uid).ToString()),
                ("ship", EntityManager.TryGetComponent<MetaDataComponent>(target, out var meta) ? meta.EntityName : target.ToString()),
                ("uid", EntityManager.GetNetEntity(target).ToString()),
                ("seconds", Math.Max(0, (int) (now - patrol.InterceptSince).TotalSeconds))));
        }

        var marks = EntityManager.EntityQueryEnumerator<WFReaverMarkComponent, MetaDataComponent>();
        while (marks.MoveNext(out var marked, out var mark, out var markedMeta))
        {
            shell.WriteLine(Loc.GetString("cmd-wf_reavers-status-mark",
                ("ship", markedMeta.EntityName),
                ("uid", EntityManager.GetNetEntity(marked).ToString()),
                ("seconds", Seconds(mark.Until, now))));
        }
    }

    /// <summary>Why none stands and when one will: the switch that is off, the pause, a convoy out, or the minutes to the founding; always the way to place one now.</summary>
    private string WhyNoStronghold(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent? state, TimeSpan now)
    {
        switch (_campaign.Idle())
        {
            case WFReaverIdle.CampaignOff:
                return Loc.GetString("cmd-wf_reavers-status-mode-off", ("cvar", ReaverCampaignCVars.Enabled.Name));
            case WFReaverIdle.EncountersOff:
                return Loc.GetString("cmd-wf_reavers-status-switch-off", ("cvar", EncountersCVars.Enabled.Name));
            case WFReaverIdle.SectorControlOff:
                return Loc.GetString("cmd-wf_reavers-status-switch-off", ("cvar", SectorControlCVars.Enabled.Name));
            case WFReaverIdle.NotInRound:
                return Loc.GetString("cmd-wf_reavers-status-not-in-round");
        }

        if (_campaign.Paused)
            return Loc.GetString("cmd-wf_reavers-status-paused");

        if (_campaign.RunnerPaused)
            return Loc.GetString("cmd-wf_reavers-status-runner-paused");

        if (_campaign.Convoys().Count > 0)
            return Loc.GetString("cmd-wf_reavers-status-convoy-out");

        if (_campaign.FoundingDue(campaign, state) is not { } due)
            return Loc.GetString("cmd-wf_reavers-status-founding-untimed");

        if (due <= now)
            return Loc.GetString("cmd-wf_reavers-status-founding-overdue");

        var minutes = (int) Math.Ceiling((due - now).TotalMinutes);
        return Loc.GetString(state is { Founded: > 0 } ? "cmd-wf_reavers-status-regroup-due" : "cmd-wf_reavers-status-first-due",
            ("minutes", minutes));
    }

    /// <summary>wf_reavers intercept &lt;grid uid&gt;: sends a patrol after a ship now.</summary>
    private void Intercept(IConsoleShell shell, string arg)
    {
        if (!NetEntity.TryParse(arg, out var net) || !EntityManager.TryGetEntity(net, out var grid)
            || !EntityManager.HasComponent<MapGridComponent>(grid))
        {
            shell.WriteLine(Loc.GetString("cmd-wf_reavers-bad-grid", ("arg", arg)));
            return;
        }

        if (_campaign.Campaign == null)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_reavers-no-campaign"));
            return;
        }

        var name = EntityManager.GetComponent<MetaDataComponent>(grid.Value).EntityName;
        shell.WriteLine(Loc.GetString(_campaign.TryIntercept(grid.Value) ? "cmd-wf_reavers-intercept" : "cmd-wf_reavers-intercept-failed",
            ("ship", name)));
    }

    /// <summary>Seconds until a time, or a dash when there is none.</summary>
    private static string Seconds(TimeSpan? at, TimeSpan now)
    {
        return at is { } time ? Math.Max(0, (int) (time - now).TotalSeconds).ToString(CultureInfo.InvariantCulture) : "-";
    }

    /// <summary>wf_reavers found [x y]: where the rules put it, or at a point on the sector map.</summary>
    private void Found(IConsoleShell shell, string[] args)
    {
        EntityUid stronghold;
        bool founded;
        if (args.Length == 3)
        {
            if (!float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                || !float.IsFinite(x) || !float.IsFinite(y))
            {
                shell.WriteLine(Loc.GetString("cmd-wf_reavers-bad-position", ("x", args[1]), ("y", args[2])));
                return;
            }

            founded = _campaign.TryFoundAt(new MapCoordinates(x, y, _territory.SectorMap), out stronghold);
        }
        else
        {
            founded = _campaign.TryFound(out stronghold);
        }

        if (!founded)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_reavers-found-failed"));
            return;
        }

        var cell = _territory.CellOf(EntityManager.System<SharedTransformSystem>().GetMapCoordinates(stronghold));
        shell.WriteLine(Loc.GetString("cmd-wf_reavers-founded",
            ("uid", EntityManager.GetNetEntity(stronghold).ToString()),
            ("callsign", WFSectorHex.Callsign(cell))));
    }

    /// <summary>wf_reavers convoy: sends a stronghold out to the next cell, as the campaign does.</summary>
    private void Convoy(IConsoleShell shell)
    {
        if (!_campaign.TryLaunchConvoy(out var convoy)
            || !EntityManager.TryGetComponent<WFReaverConvoyComponent>(convoy, out var comp))
        {
            Failed(shell, "cmd-wf_reavers-convoy-failed");
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wf_reavers-convoy",
            ("uid", EntityManager.GetNetEntity(convoy).ToString()),
            ("callsign", WFSectorHex.Callsign(comp.Target))));
    }

    /// <summary>wf_reavers reinforce &lt;stronghold uid&gt;: sends a stronghold its next reinforcement now.</summary>
    private void Reinforce(IConsoleShell shell, string arg)
    {
        if (_campaign.Campaign == null)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_reavers-no-campaign"));
            return;
        }

        if (!NetEntity.TryParse(arg, out var net) || !EntityManager.TryGetEntity(net, out var stronghold)
            || !_campaign.Strongholds().Any(entry => entry.Owner == stronghold))
        {
            shell.WriteLine(Loc.GetString("cmd-wf_reavers-bad-stronghold", ("arg", arg)));
            return;
        }

        if (!_campaign.TryReinforce(stronghold.Value)
            || EntityManager.GetComponent<WFReaverStrongholdComponent>(stronghold.Value).Inbound is not { } inbound)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_reavers-reinforce-failed"));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wf_reavers-reinforce",
            ("uid", EntityManager.GetNetEntity(inbound).ToString()),
            ("callsign", WFSectorHex.Callsign(EntityManager.GetComponent<WFReaverStrongholdComponent>(stronghold.Value).Cell))));
    }

    /// <summary>wf_reavers patrol: from the stronghold with the fewest patrols out.</summary>
    private void Patrol(IConsoleShell shell)
    {
        var patrols = _campaign.Patrols();
        foreach (var stronghold in _campaign.Strongholds().OrderBy(entry => patrols.Count(patrol => patrol.Comp1.Stronghold == entry.Owner)))
        {
            if (!_campaign.TryLaunchPatrol(stronghold.Owner))
                continue;

            shell.WriteLine(Loc.GetString("cmd-wf_reavers-patrol", ("callsign", WFSectorHex.Callsign(stronghold.Comp1.Cell))));
            return;
        }

        Failed(shell, "cmd-wf_reavers-patrol-failed");
    }

    /// <summary>Prints why a step failed: no campaign, or no stronghold standing and the way to place one, else the step's own line.</summary>
    private void Failed(IConsoleShell shell, string key)
    {
        if (_campaign.Campaign == null)
            key = "cmd-wf_reavers-no-campaign";
        else if (_campaign.Strongholds().Count == 0)
            key = "cmd-wf_reavers-no-stronghold";

        shell.WriteLine(Loc.GetString(key));
    }
}
