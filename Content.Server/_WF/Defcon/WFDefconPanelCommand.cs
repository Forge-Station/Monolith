using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared._WF.Defcon;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WF.Defcon;

/// <summary>Opens the DEFCON admin panel.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WFDefconPanelCommand : LocalizedCommands
{
    [Dependency] private EuiManager _eui = default!;

    public override string Command => WFDefconCommands.Panel;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        _eui.OpenEui(new WFDefconEui(), player);
    }
}
