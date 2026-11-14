using Content.Server.Administration;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WF.Xenonids.Hive;

/// <summary>Adds burrowed larva to the default hive (creating it if needed) so ghosts can join as xenos.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class XenoLarvaCommand : LocalizedEntityCommands
{
    [Dependency] private WFXenoHiveBootstrapSystem _bootstrap = default!;
    [Dependency] private SharedXenoHiveSystem _hive = default!;

    public override string Command => "xeno_larva";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 || !int.TryParse(args[0], out var count))
        {
            shell.WriteError(Loc.GetString("cmd-xeno_larva-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        var hive = _bootstrap.EnsureDefaultHive();
        _hive.ChangeBurrowedLarva(hive, count);
        shell.WriteLine(Loc.GetString("cmd-xeno_larva-done", ("count", count), ("total", hive.Comp.BurrowedLarva)));
    }
}
