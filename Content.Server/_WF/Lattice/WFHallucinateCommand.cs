using Content.Server.Administration;
using Content.Shared._WF.Lattice;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._WF.Lattice;

/// <summary>Shows one Drift hallucination to a player, for testing the effects or for events.</summary>
[AdminCommand(AdminFlags.Fun)]
public sealed class WFHallucinateCommand : LocalizedEntityCommands
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private WFDriftEffectsSystem _effects = default!;

    public override string Command => "wfhallucinate";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2 || !Enum.TryParse<WFLatticeHallucinationKind>(args[0], true, out var kind))
        {
            shell.WriteLine(Help);
            return;
        }

        var session = shell.Player;
        if (args.Length == 2 && !_players.TryGetSessionByUsername(args[1], out session))
        {
            shell.WriteError(Loc.GetString("cmd-wfhallucinate-no-player", ("name", args[1])));
            return;
        }

        if (session?.AttachedEntity is not { } mob)
        {
            shell.WriteError(Loc.GetString("cmd-wfhallucinate-no-body"));
            return;
        }

        _effects.Show(session, mob, kind);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromOptions(Enum.GetNames<WFLatticeHallucinationKind>()),
            2 => CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _players), Loc.GetString("cmd-wfhallucinate-hint-player")),
            _ => CompletionResult.Empty,
        };
    }
}
