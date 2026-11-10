using System.Globalization;
using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Timing;

namespace Content.Server._WF.Defcon;

/// <summary>The sector DEFCON from the console: show the score, set or lock the level, unlock it, bias the score or score it now.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WFDefconCommand : LocalizedEntityCommands
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WFDefconSystem _defcon = default!;

    private static readonly string[] Subcommands = { "status", "set", "lock", "unlock", "bias", "eval" };

    public override string Command => "wf_defcon";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        switch (args.Length > 0 ? args[0] : string.Empty)
        {
            case "status" when args.Length == 1:
                Status(shell);
                break;
            case "set" or "lock" when args.Length == 2:
                if (!int.TryParse(args[1], out var level) || level is < WFDefconTracker.Severest or > WFDefconTracker.Calmest)
                {
                    shell.WriteError(Loc.GetString("cmd-wf_defcon-bad-level"));
                    return;
                }

                _defcon.SetLevel(level, args[0] == "lock");
                shell.WriteLine(Loc.GetString(args[0] == "lock" ? "cmd-wf_defcon-locked" : "cmd-wf_defcon-set", ("level", level)));
                break;
            case "unlock" when args.Length == 1:
                _defcon.Unlock();
                shell.WriteLine(Loc.GetString("cmd-wf_defcon-unlocked"));
                break;
            case "bias" when args.Length == 2:
                if (!float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var bias) || !float.IsFinite(bias))
                {
                    shell.WriteError(Loc.GetString("cmd-wf_defcon-bad-bias"));
                    return;
                }

                _defcon.Bias = bias;
                shell.WriteLine(Loc.GetString("cmd-wf_defcon-biased", ("bias", bias)));
                break;
            case "eval" when args.Length == 1:
                _defcon.Evaluate(_timing.CurTime);
                Status(shell);
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
            1 => CompletionResult.FromHintOptions(Subcommands, Loc.GetString("cmd-wf_defcon-hint-sub")),
            2 when args[0] is "set" or "lock" => CompletionResult.FromHintOptions(
                Enumerable.Range(WFDefconTracker.Severest, WFDefconTracker.Calmest).Select(l => l.ToString()),
                Loc.GetString("cmd-wf_defcon-hint-level")),
            2 when args[0] == "bias" => CompletionResult.FromHint(Loc.GetString("cmd-wf_defcon-hint-bias")),
            _ => CompletionResult.Empty,
        };
    }

    private void Status(IConsoleShell shell)
    {
        shell.WriteLine(Loc.GetString("cmd-wf_defcon-status",
            ("level", _defcon.Level),
            ("name", _defcon.LevelName(_defcon.Level)),
            ("score", _defcon.Score.ToString("0.#", CultureInfo.InvariantCulture)),
            ("floor", _defcon.Floor),
            ("locked", _defcon.Locked != null ? "true" : "false")));

        foreach (var contribution in _defcon.Contributions)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_defcon-status-line",
                ("score", contribution.Score.ToString("0.#", CultureInfo.InvariantCulture)),
                ("reason", contribution.Reason)));
        }
    }
}
