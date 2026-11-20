using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Console;

namespace Content.Server._WF.RoundSetup;

/// <summary>Withdraws a crew's posting and stops it recruiting.</summary>
[AdminCommand(AdminFlags.Moderator)]
public sealed class WFCrewUnpostCommand : LocalizedEntityCommands
{
    [Dependency] private WFRoundSetupSystem _setup = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;

    public override string Command => "crewunpost";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 || !int.TryParse(args[0], out var id))
        {
            shell.WriteError(Loc.GetString("cmd-crewunpost-invalid"));
            return;
        }
        if (!_setup.RevokePosting(id))
        {
            shell.WriteError(Loc.GetString("cmd-crewunpost-missing", ("id", id)));
            return;
        }
        shell.WriteLine(Loc.GetString("cmd-crewunpost-done", ("id", id)));
        _adminLog.Add(LogType.AdminCommands, LogImpact.Low, $"{shell.Player?.Name ?? "Server"} withdrew crew {id}'s posting");
    }
}
