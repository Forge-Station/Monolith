using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared._WF.Announcements;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WF.Announcements;

/// <summary>Opens the roleplay announcement window.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WFAnnounceCommand : LocalizedCommands
{
    [Dependency] private EuiManager _eui = default!;

    public override string Command => WFAnnounceCommands.Open;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        _eui.OpenEui(new WFAnnounceEui(), player);
    }
}
