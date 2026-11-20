using System.Linq;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server.Administration;
using Content.Shared._WF.Encounters;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Encounters.Commands;

/// <summary>Encounters from the console: list, spawn one near the caller, end one, run or pause the scheduler.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WFEncounterCommand : LocalizedEntityCommands
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private WFEncounterSchedulerSystem _scheduler = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly string[] Subcommands = { "list", "spawn", "end", "schedule", "pause", "resume" };

    /// <summary>How far ahead of the caller a spawned encounter's origin sits, unless given.</summary>
    private const float DefaultDistance = 300f;

    public override string Command => "wf_encounter";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        switch (args.Length > 0 ? args[0] : string.Empty)
        {
            case "list" when args.Length == 1:
                List(shell);
                break;
            case "spawn" when args.Length is >= 2 and <= 5:
                Spawn(shell, args);
                break;
            case "end" when args.Length == 2:
                End(shell, args[1]);
                break;
            case "schedule" when args.Length == 1:
                shell.WriteLine(Loc.GetString(_scheduler.TrySchedule(out var scheduled)
                    ? "cmd-wf_encounter-scheduled"
                    : "cmd-wf_encounter-not-scheduled", ("uid", EntityManager.GetNetEntity(scheduled))));
                break;
            case "pause" when args.Length == 1:
                _scheduler.Paused = true;
                shell.WriteLine(Loc.GetString("cmd-wf_encounter-paused"));
                break;
            case "resume" when args.Length == 1:
                _scheduler.Paused = false;
                shell.WriteLine(Loc.GetString("cmd-wf_encounter-resumed"));
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
            1 => CompletionResult.FromHintOptions(Subcommands, Loc.GetString("cmd-wf_encounter-hint-sub")),
            2 when args[0] == "spawn" => CompletionResult.FromHintOptions(
                _prototypes.EnumeratePrototypes<WFEncounterPrototype>().Select(prototype => prototype.ID).Order(),
                Loc.GetString("cmd-wf_encounter-hint-prototype")),
            3 when args[0] == "spawn" => CompletionResult.FromHintOptions(Keeping, Loc.GetString("cmd-wf_encounter-hint-distance")),
            4 when args[0] == "spawn" => CompletionResult.FromHintOptions(Keeping, Loc.GetString("cmd-wf_encounter-hint-keep")),
            5 when args[0] == "spawn" => CompletionResult.FromHintOptions(
                Enumerable.Range(0, WFEncounterThreat.MaxTier + 1).Select(tier => TierPrefix + tier),
                Loc.GetString("cmd-wf_encounter-hint-tier")),
            2 when args[0] == "end" => CompletionResult.FromHint(Loc.GetString("cmd-wf_encounter-hint-uid")),
            _ => CompletionResult.Empty,
        };
    }

    private void List(IConsoleShell shell)
    {
        var count = 0;
        var query = EntityManager.EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out var uid, out var encounter))
        {
            count++;
            shell.WriteLine(Loc.GetString("cmd-wf_encounter-list-line",
                ("uid", EntityManager.GetNetEntity(uid)),
                ("prototype", encounter.Prototype.Id),
                ("name", encounter.Name),
                ("state", _encounters.StateDetail(uid, encounter,
                    encounter.Resolution?.ToString() ?? Loc.GetString("cmd-wf_encounter-state-active"), true)),
                ("ships", encounter.Ships.Values.Count(ship => !EntityManager.Deleted(ship.Grid))),
                ("x", encounter.Origin.Position.X.ToString("0")),
                ("y", encounter.Origin.Position.Y.ToString("0"))));
        }

        shell.WriteLine(Loc.GetString("cmd-wf_encounter-list-footer", ("count", count)));
    }

    /// <summary>The words for keeping a spawned encounter until it is ended, or letting it jump out as usual.</summary>
    private static readonly string[] Keeping = { "keep", "jump" };

    /// <summary>The argument that sets the threat tier, as tier=3.</summary>
    private const string TierPrefix = "tier=";

    /// <summary>
    /// wf_encounter spawn &lt;prototype&gt; [distance] [keep|jump] [tier=N]: its origin north of the caller, kept until
    /// ended unless told to jump out as usual, at the given threat tier or the sector's.
    /// </summary>
    private void Spawn(IConsoleShell shell, string[] args)
    {
        if (shell.Player?.AttachedEntity is not { Valid: true } player
            || _transform.GetMapCoordinates(player) is var here && here.MapId == MapId.Nullspace)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_encounter-no-player"));
            return;
        }

        if (!_prototypes.TryIndex<WFEncounterPrototype>(args[1], out var prototype))
        {
            shell.WriteLine(Loc.GetString("cmd-wf_encounter-unknown", ("prototype", args[1])));
            return;
        }

        var distance = DefaultDistance;
        var pinned = true;
        int? tier = null;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] == Keeping[0] || args[i] == Keeping[1])
            {
                pinned = args[i] == Keeping[0];
                continue;
            }

            if (args[i].StartsWith(TierPrefix))
            {
                if (!int.TryParse(args[i][TierPrefix.Length..], out var parsed) || parsed is < 0 or > WFEncounterThreat.MaxTier)
                {
                    shell.WriteLine(Loc.GetString("cmd-wf_encounter-bad-tier", ("arg", args[i]), ("max", WFEncounterThreat.MaxTier)));
                    return;
                }

                tier = parsed;
                continue;
            }

            if (!float.TryParse(args[i], out distance) || !float.IsFinite(distance) || distance is < 0 or > 20000)
            {
                shell.WriteLine(Loc.GetString("cmd-wf_encounter-bad-distance", ("arg", args[i])));
                return;
            }
        }

        var origin = new MapCoordinates(here.Position + new System.Numerics.Vector2(0f, distance), here.MapId);
        shell.WriteLine(_scheduler.TryStartAt(prototype, origin, out var encounter, player, pinned, tier)
            ? Loc.GetString("cmd-wf_encounter-spawned", ("name", EntityManager.GetComponent<WFEncounterComponent>(encounter).Name),
                ("uid", EntityManager.GetNetEntity(encounter)))
            : Loc.GetString("cmd-wf_encounter-spawn-failed", ("prototype", prototype.ID)));
    }

    private void End(IConsoleShell shell, string arg)
    {
        if (!NetEntity.TryParse(arg, out var net) || !EntityManager.TryGetEntity(net, out var uid)
            || !EntityManager.HasComponent<WFEncounterComponent>(uid))
        {
            shell.WriteLine(Loc.GetString("cmd-wf_encounter-not-found", ("arg", arg)));
            return;
        }

        _encounters.End(uid.Value);
        shell.WriteLine(Loc.GetString("cmd-wf_encounter-ended", ("uid", net)));
    }
}
