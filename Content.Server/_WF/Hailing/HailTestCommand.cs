using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WF.Hailing;

/// <summary>
/// Has a test ship hail the ship the admin is standing on, so hailing can be tried without a second player.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class HailTestCommand : LocalizedEntityCommands
{
    [Dependency] private HailTestSystem _test = default!;
    [Dependency] private HailingSystem _hailing = default!;

    public override string Command => "hailtest";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 1 && args[0] == "clear")
        {
            shell.WriteLine(Loc.GetString("cmd-hailtest-cleared", ("count", _test.Clear())));
            return;
        }

        if (args.Length != 0)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (shell.Player?.AttachedEntity is not { } player ||
            EntityManager.GetComponent<TransformComponent>(player).GridUid is not { } grid)
        {
            shell.WriteError(Loc.GetString("cmd-hailtest-no-grid"));
            return;
        }

        if (!_hailing.HasFreeConsole(grid))
        {
            shell.WriteError(Loc.GetString("cmd-hailtest-no-console"));
            return;
        }

        _test.Hail(grid);
        shell.WriteLine(Loc.GetString("cmd-hailtest-started", ("ship", EntityManager.GetComponent<MetaDataComponent>(grid).EntityName)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1 ? CompletionResult.FromOptions(new[] { "clear" }) : CompletionResult.Empty;
    }
}
