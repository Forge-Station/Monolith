using System.Globalization;
using System.Linq;
using Content.Server._WF.AdsIncursion.Components;
using Content.Server._WF.AdsIncursion.Systems;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server._WF.SectorControl.Systems;
using Content.Server.Administration;
using Content.Shared._WF.AdsIncursion;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.SectorControl;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.AdsIncursion.Commands;

/// <summary>The ADS incursion from the console: show it, found or deploy a node, send a sentinel, sweep, interdiction, challenge or the dreadnought, set the tier, step it, pause it or clear it.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WFAdsCommand : LocalizedEntityCommands
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WFAdsCampaignSystem _campaign = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;

    private static readonly string[] Subcommands =
        { "status", "found", "deploy", "sentinel", "tier", "spread", "sweep", "interdict", "challenge", "dreadnought", "pause", "resume", "clear" };

    public override string Command => "wf_ads";

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
            case "deploy" when args.Length == 1:
                Deploy(shell);
                break;
            case "sentinel" when args.Length == 2:
                Sentinel(shell, args[1]);
                break;
            case "tier" when args.Length == 2:
                Tier(shell, args[1]);
                break;
            case "spread" when args.Length == 1:
                if (_campaign.TrySpreadOnce())
                    shell.WriteLine(Loc.GetString("cmd-wf_ads-spread"));
                else
                    Failed(shell, "cmd-wf_ads-spread-failed");
                break;
            case "sweep" when args.Length == 1:
                Sweep(shell);
                break;
            case "interdict" when args.Length == 2:
                Interdict(shell, args[1]);
                break;
            case "challenge" when args.Length is 1 or 3:
                Challenge(shell, args);
                break;
            case "dreadnought" when args.Length == 1:
                Dreadnought(shell);
                break;
            case "pause" when args.Length == 1:
                _campaign.Paused = true;
                shell.WriteLine(Loc.GetString("cmd-wf_ads-paused"));
                break;
            case "resume" when args.Length == 1:
                _campaign.Paused = false;
                shell.WriteLine(Loc.GetString("cmd-wf_ads-resumed"));
                break;
            case "clear" when args.Length == 1:
                shell.WriteLine(Loc.GetString("cmd-wf_ads-cleared", ("count", _campaign.ClearCampaign())));
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
            1 => CompletionResult.FromHintOptions(Subcommands, Loc.GetString("cmd-wf_ads-hint-sub")),
            2 when args[0] == "found" => CompletionResult.FromHint(Loc.GetString("cmd-wf_ads-hint-x")),
            3 when args[0] == "found" => CompletionResult.FromHint(Loc.GetString("cmd-wf_ads-hint-y")),
            2 when args[0] == "challenge" => CompletionResult.FromHint(Loc.GetString("cmd-wf_ads-hint-q")),
            3 when args[0] == "challenge" => CompletionResult.FromHint(Loc.GetString("cmd-wf_ads-hint-r")),
            2 when args[0] == "interdict" => CompletionResult.FromHint(Loc.GetString("cmd-wf_ads-hint-grid")),
            2 when args[0] == "sentinel" => CompletionResult.FromHint(Loc.GetString("cmd-wf_ads-hint-node")),
            2 when args[0] == "tier" => CompletionResult.FromHintOptions(
                Enumerable.Range(0, WFAdsCampaignSystem.MaxTier + 1).Select(tier => tier.ToString()),
                Loc.GetString("cmd-wf_ads-hint-tier")),
            _ => CompletionResult.Empty,
        };
    }

    private void Status(IConsoleShell shell)
    {
        if (_campaign.Campaign is not { } campaign)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_ads-no-campaign"));
            return;
        }

        var state = _campaign.State();
        var nodes = _campaign.Nodes();
        var sweeps = _campaign.Sweeps();
        var now = _timing.CurTime;
        shell.WriteLine(Loc.GetString("cmd-wf_ads-status-header",
            ("campaign", campaign.ID),
            ("running", _campaign.Running.ToString()),
            ("paused", _campaign.Held.ToString()),
            ("tier", _campaign.TierName(state?.Tier ?? 0)),
            ("score", (state?.Score ?? 0f).ToString("0.0", CultureInfo.InvariantCulture))));
        shell.WriteLine(Loc.GetString("cmd-wf_ads-status-counts",
            ("nodes", nodes.Count),
            ("cells", _territory.Held(_territory.SectorMap, campaign.Faction).Count),
            ("sweeps", sweeps.Count),
            ("challenges", _campaign.Interdictors().Count),
            ("pickets", _campaign.Pickets().Count)));
        shell.WriteLine(Loc.GetString("cmd-wf_ads-status-clocks",
            ("spread", Seconds(state?.NextSpread, now)),
            ("challenge", Seconds(state?.NextChallenge, now))));

        if (state is { Finished: true })
            shell.WriteLine(Loc.GetString("cmd-wf_ads-status-finished"));
        else if (campaign.Dreadnought != null && state?.OverrunSince is { } overrun && _campaign.Dreadnoughts().Count == 0)
            shell.WriteLine(Loc.GetString("cmd-wf_ads-status-dreadnought-due", ("seconds", Seconds(overrun + campaign.DreadnoughtDelay, now))));

        if (nodes.Count == 0 && state is not { Finished: true })
            shell.WriteLine(WhyNoNode(campaign, state, now));

        foreach (var (uid, node, _) in nodes)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_ads-status-node",
                ("uid", EntityManager.GetNetEntity(uid).ToString()),
                ("callsign", WFSectorHex.Callsign(node.Cell)),
                ("hits", node.Hits),
                ("sweeps", sweeps.Count(sweep => sweep.Comp1.Node == uid)),
                ("sentinels", node.Sentinels.Count),
                ("inbound", node.Inbound != null ? Loc.GetString("cmd-wf_ads-status-node-inbound") : string.Empty)));
        }

        var transform = EntityManager.System<SharedTransformSystem>();
        foreach (var (uid, dreadnought, encounter) in _campaign.Dreadnoughts())
        {
            if (!encounter.Ships.TryGetValue(dreadnought.Lead, out var ship) || !EntityManager.EntityExists(ship.Grid))
                continue;

            var at = transform.GetWorldPosition(ship.Grid);
            var target = dreadnought.Target ?? dreadnought.Station;
            var range = target is { } aim && EntityManager.EntityExists(aim) ? WFReaverCampaignSystem.Kilometres(at, Lead(aim, transform) ?? at) : "-";
            shell.WriteLine(Loc.GetString(dreadnought.Target != null ? "cmd-wf_ads-status-dreadnought-hunting" : "cmd-wf_ads-status-dreadnought-holding",
                ("uid", EntityManager.GetNetEntity(uid).ToString()),
                ("target", target is { } named && EntityManager.TryGetComponent<MetaDataComponent>(named, out var meta) ? meta.EntityName : "-"),
                ("range", range),
                ("seconds", Math.Max(0, (int) (now - dreadnought.Launched).TotalSeconds))));
        }

        foreach (var (uid, deployment, encounter) in _campaign.Deployments())
        {
            var at = encounter.Ships.TryGetValue(deployment.Lead, out var lead) && EntityManager.EntityExists(lead.Grid)
                ? transform.GetWorldPosition(lead.Grid)
                : encounter.Origin.Position;
            shell.WriteLine(Loc.GetString("cmd-wf_ads-status-deployment",
                ("uid", EntityManager.GetNetEntity(uid).ToString()),
                ("callsign", WFSectorHex.Callsign(deployment.Target)),
                ("range", WFReaverCampaignSystem.Kilometres(at, deployment.TargetPosition)),
                ("seconds", Math.Max(0, (int) (now - deployment.Launched).TotalSeconds))));
        }

        foreach (var (uid, interdictor, _) in _campaign.Interdictors())
        {
            var contest = _territory.ContestOf(_territory.SectorMap, interdictor.Target);
            shell.WriteLine(Loc.GetString("cmd-wf_ads-status-challenge",
                ("uid", EntityManager.GetNetEntity(uid).ToString()),
                ("callsign", WFSectorHex.Callsign(interdictor.Target)),
                ("arrived", interdictor.Arrived.ToString()),
                ("seconds", Seconds(contest?.Deadline, now))));
        }

        foreach (var (uid, sweep, _) in sweeps)
        {
            if (sweep.Interdicting is not { } target)
                continue;

            shell.WriteLine(Loc.GetString("cmd-wf_ads-status-interdict",
                ("sweep", EntityManager.GetNetEntity(uid).ToString()),
                ("ship", EntityManager.TryGetComponent<MetaDataComponent>(target, out var meta) ? meta.EntityName : target.ToString()),
                ("uid", EntityManager.GetNetEntity(target).ToString()),
                ("seconds", Math.Max(0, (int) (now - sweep.InterdictSince).TotalSeconds))));
        }
    }

    /// <summary>Why no node stands and when one will: the switch that is off, the pause, a deployment out, or the minutes to the founding.</summary>
    private string WhyNoNode(WFAdsCampaignPrototype campaign, WFAdsCampaignComponent? state, TimeSpan now)
    {
        switch (_campaign.Idle())
        {
            case WFAdsIdle.CampaignOff:
                return Loc.GetString("cmd-wf_ads-status-mode-off", ("cvar", AdsIncursionCVars.Enabled.Name));
            case WFAdsIdle.EncountersOff:
                return Loc.GetString("cmd-wf_ads-status-switch-off", ("cvar", EncountersCVars.Enabled.Name));
            case WFAdsIdle.SectorControlOff:
                return Loc.GetString("cmd-wf_ads-status-switch-off", ("cvar", SectorControlCVars.Enabled.Name));
            case WFAdsIdle.NotInRound:
                return Loc.GetString("cmd-wf_ads-status-not-in-round");
        }

        if (_campaign.Paused)
            return Loc.GetString("cmd-wf_ads-status-paused");

        if (_campaign.RunnerPaused)
            return Loc.GetString("cmd-wf_ads-status-runner-paused");

        if (_campaign.Deployments().Count > 0)
            return Loc.GetString("cmd-wf_ads-status-deployment-out");

        if (_campaign.FoundingDue(campaign, state) is not { } due)
            return Loc.GetString("cmd-wf_ads-status-founding-untimed");

        if (due <= now)
            return Loc.GetString("cmd-wf_ads-status-founding-overdue");

        var minutes = (int) Math.Ceiling((due - now).TotalMinutes);
        return Loc.GetString(state is { Founded: > 0 } ? "cmd-wf_ads-status-regroup-due" : "cmd-wf_ads-status-first-due",
            ("minutes", minutes));
    }

    /// <summary>Where an encounter's first ship is, or the entity itself if it is no encounter.</summary>
    private System.Numerics.Vector2? Lead(EntityUid uid, SharedTransformSystem transform)
    {
        if (!EntityManager.TryGetComponent<WFEncounterComponent>(uid, out var encounter))
            return transform.GetWorldPosition(uid);

        foreach (var ship in encounter.Ships.Values)
        {
            if (EntityManager.EntityExists(ship.Grid))
                return transform.GetWorldPosition(ship.Grid);
        }

        return null;
    }

    /// <summary>wf_ads dreadnought: activates the dreadnought now, whatever the tier and delay; one at a time.</summary>
    private void Dreadnought(IConsoleShell shell)
    {
        if (!_campaign.TryLaunchDreadnought(out var dreadnought))
        {
            Failed(shell, "cmd-wf_ads-dreadnought-failed");
            return;
        }

        var target = EntityManager.GetComponent<WFAdsDreadnoughtComponent>(dreadnought).Target;
        shell.WriteLine(Loc.GetString("cmd-wf_ads-dreadnought",
            ("uid", EntityManager.GetNetEntity(dreadnought).ToString()),
            ("target", target is { } aim && EntityManager.TryGetComponent<MetaDataComponent>(aim, out var meta) ? meta.EntityName : "-")));
    }

    /// <summary>Seconds until a time, or a dash when there is none.</summary>
    private static string Seconds(TimeSpan? at, TimeSpan now)
    {
        return at is { } time ? Math.Max(0, (int) (time - now).TotalSeconds).ToString(CultureInfo.InvariantCulture) : "-";
    }

    /// <summary>wf_ads found [x y]: where the rules put it, or at a point on the sector map.</summary>
    private void Found(IConsoleShell shell, string[] args)
    {
        EntityUid node;
        bool founded;
        if (args.Length == 3)
        {
            if (!float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                || !float.IsFinite(x) || !float.IsFinite(y))
            {
                shell.WriteLine(Loc.GetString("cmd-wf_ads-bad-position", ("x", args[1]), ("y", args[2])));
                return;
            }

            founded = _campaign.TryFoundAt(new MapCoordinates(x, y, _territory.SectorMap), out node);
        }
        else
        {
            founded = _campaign.TryFound(out node);
        }

        if (!founded)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_ads-found-failed"));
            return;
        }

        var cell = _territory.CellOf(EntityManager.System<SharedTransformSystem>().GetMapCoordinates(node));
        shell.WriteLine(Loc.GetString("cmd-wf_ads-founded",
            ("uid", EntityManager.GetNetEntity(node).ToString()),
            ("callsign", WFSectorHex.Callsign(cell))));
    }

    /// <summary>wf_ads deploy: sends a node out to the next cell, as the campaign does.</summary>
    private void Deploy(IConsoleShell shell)
    {
        if (!_campaign.TryLaunchDeployment(out var deployment)
            || !EntityManager.TryGetComponent<WFAdsDeploymentComponent>(deployment, out var comp))
        {
            Failed(shell, "cmd-wf_ads-deploy-failed");
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wf_ads-deploy",
            ("uid", EntityManager.GetNetEntity(deployment).ToString()),
            ("callsign", WFSectorHex.Callsign(comp.Target))));
    }

    /// <summary>wf_ads sentinel &lt;node uid&gt;: sends a node a sentinel now.</summary>
    private void Sentinel(IConsoleShell shell, string arg)
    {
        if (_campaign.Campaign == null)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_ads-no-campaign"));
            return;
        }

        if (!NetEntity.TryParse(arg, out var net) || !EntityManager.TryGetEntity(net, out var node)
            || !_campaign.Nodes().Any(entry => entry.Owner == node))
        {
            shell.WriteLine(Loc.GetString("cmd-wf_ads-bad-node", ("arg", arg)));
            return;
        }

        if (!_campaign.TrySentinel(node.Value)
            || EntityManager.GetComponent<WFAdsNodeComponent>(node.Value).Inbound is not { } inbound)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_ads-sentinel-failed"));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wf_ads-sentinel",
            ("uid", EntityManager.GetNetEntity(inbound).ToString()),
            ("callsign", WFSectorHex.Callsign(EntityManager.GetComponent<WFAdsNodeComponent>(node.Value).Cell))));
    }

    /// <summary>wf_ads tier &lt;n&gt;: forces the tier.</summary>
    private void Tier(IConsoleShell shell, string arg)
    {
        if (!int.TryParse(arg, out var tier) || tier < 0 || tier > WFAdsCampaignSystem.MaxTier)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_ads-bad-tier", ("arg", arg), ("max", WFAdsCampaignSystem.MaxTier)));
            return;
        }

        if (_campaign.Campaign == null)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_ads-no-campaign"));
            return;
        }

        _campaign.SetTier(tier);
        shell.WriteLine(Loc.GetString("cmd-wf_ads-tier-set", ("tier", _campaign.TierName(tier))));
    }

    /// <summary>wf_ads sweep: from the node with the fewest sweeps out.</summary>
    private void Sweep(IConsoleShell shell)
    {
        var sweeps = _campaign.Sweeps();
        foreach (var node in _campaign.Nodes().OrderBy(entry => sweeps.Count(sweep => sweep.Comp1.Node == entry.Owner)))
        {
            if (!_campaign.TryLaunchSweep(node.Owner))
                continue;

            shell.WriteLine(Loc.GetString("cmd-wf_ads-sweep", ("callsign", WFSectorHex.Callsign(node.Comp1.Cell))));
            return;
        }

        Failed(shell, "cmd-wf_ads-sweep-failed");
    }

    /// <summary>wf_ads interdict &lt;grid uid&gt;: sends a sweep after a ship now.</summary>
    private void Interdict(IConsoleShell shell, string arg)
    {
        if (!NetEntity.TryParse(arg, out var net) || !EntityManager.TryGetEntity(net, out var grid)
            || !EntityManager.HasComponent<MapGridComponent>(grid))
        {
            shell.WriteLine(Loc.GetString("cmd-wf_ads-bad-grid", ("arg", arg)));
            return;
        }

        if (_campaign.Campaign == null)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_ads-no-campaign"));
            return;
        }

        var name = EntityManager.GetComponent<MetaDataComponent>(grid.Value).EntityName;
        shell.WriteLine(Loc.GetString(_campaign.TryInterdict(grid.Value) ? "cmd-wf_ads-interdict" : "cmd-wf_ads-interdict-failed",
            ("ship", name)));
    }

    /// <summary>wf_ads challenge [q r]: the cell the rules pick, or a given one.</summary>
    private void Challenge(IConsoleShell shell, string[] args)
    {
        EntityUid interdictor;
        bool sent;
        if (args.Length == 3)
        {
            if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var q)
                || !int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r))
            {
                shell.WriteLine(Loc.GetString("cmd-wf_ads-bad-cell", ("q", args[1]), ("r", args[2])));
                return;
            }

            sent = _campaign.TryChallenge(new WFSectorCell(q, r), out interdictor);
        }
        else
        {
            sent = _campaign.TryChallenge(out interdictor);
        }

        if (!sent || !EntityManager.TryGetComponent<WFAdsInterdictorComponent>(interdictor, out var comp))
        {
            Failed(shell, "cmd-wf_ads-challenge-failed");
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wf_ads-challenge",
            ("uid", EntityManager.GetNetEntity(interdictor).ToString()),
            ("callsign", WFSectorHex.Callsign(comp.Target))));
    }

    /// <summary>Prints why a step failed: no campaign, or no node standing and the way to place one, else the step's own line.</summary>
    private void Failed(IConsoleShell shell, string key)
    {
        if (_campaign.Campaign == null)
            key = "cmd-wf_ads-no-campaign";
        else if (_campaign.Nodes().Count == 0)
            key = "cmd-wf_ads-no-node";

        shell.WriteLine(Loc.GetString(key));
    }
}
