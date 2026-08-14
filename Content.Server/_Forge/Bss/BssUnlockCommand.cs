using Content.Server.Administration;
using Content.Server.Shuttles.Systems;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Forge.Bss;

[AdminCommand(AdminFlags.Fun)]
public sealed class BssUnlockCommand : LocalizedEntityCommands
{
    [Dependency] private readonly BssWorldSystem _world = default!;
    [Dependency] private readonly ShuttleConsoleSystem _consoles = default!;

    public override string Command => "bss_unlock";
    public override string Description => "Opens a warp gate from a red system into a hidden black system.";
    public override string Help => "bss_unlock [fromSystem] [toSystem]";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var from = args.Length > 0 ? args[0] : null;
        var to = args.Length > 1 ? args[1] : null;
        if (!_world.TryUnlockHiddenRoute(from, to, out var fromId, out var toId, out var reason))
        {
            shell.WriteError(Loc.GetString(reason));
            return;
        }

        _consoles.RefreshShuttleConsoles();
        shell.WriteLine($"Opened BSS route {fromId} -> {toId}.");
    }
}
