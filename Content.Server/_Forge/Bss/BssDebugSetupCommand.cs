#if DEBUG
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Toolshed;

namespace Content.Server._Forge.Bss;

/// <summary>
/// Creates a temporary two-sector BSS route around the executor's current shuttle.
/// Available only in Debug builds.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class BssDebugSetupCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IEntitySystemManager _systems = default!;

    public string Command => "bss_debug_setup";
    public string Description => "Creates a temporary pair of BSS gates for testing.";
    public string Help => "bss_debug_setup - run while standing on the shuttle that should test the route.";

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        => CompletionResult.Empty;

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        if (shell.Player?.AttachedEntity is not { Valid: true } player ||
            !_entities.TryGetComponent(player, out TransformComponent? transform) ||
            transform.GridUid is not { Valid: true } shuttle)
        {
            shell.WriteError("Stand on a shuttle grid before running this command.");
            return;
        }

        var system = _systems.GetEntitySystem<BssGateSystem>();
        if (!system.SetupDebugRoute(shuttle, out var result))
        {
            shell.WriteError(result);
            return;
        }

        shell.WriteLine(result);
        shell.WriteLine("Open the shuttle console, select SYSTEM, choose Frontier Sector, and fly east to the gate.");
    }
}
#endif
