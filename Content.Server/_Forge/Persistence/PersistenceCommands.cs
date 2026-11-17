using Content.Shared.Administration;
using Content.Server.Administration;
using Robust.Shared.Console;

namespace Content.Server._Forge.Persistence;

[AdminCommand(AdminFlags.Server)]
public sealed class ForgePersistenceSaveCommand : LocalizedEntityCommands
{
    [Dependency] private readonly PersistentWorldSystem _persistence = default!;

    public override string Command => "forge.persist-save";
    public override string Description => "Saves all marked Forge maps and grids.";
    public override string Help => Command;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        shell.WriteLine(_persistence.SaveWorld()
            ? "Persistent world saved."
            : "Persistent world save failed; check server logs.");
    }
}

[AdminCommand(AdminFlags.Server)]
public sealed class ForgePersistenceListCommand : LocalizedEntityCommands
{
    [Dependency] private readonly PersistentWorldSystem _persistence = default!;

    public override string Command => "forge.persist-list";
    public override string Description => "Lists file-backed Forge persistence entries.";
    public override string Help => Command;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var entries = _persistence.GetManifestEntries();
        var cycle = _persistence.GetCycleInfo();
        if (cycle != null)
            shell.WriteLine($"Cycle started {cycle.Value.StartedAt:u}, wipe after {cycle.Value.WipeAt:u}.");
        shell.WriteLine($"{entries.Count} persistence entries:");
        foreach (var entry in entries)
            shell.WriteLine($"{entry.Kind}: {entry.Id} -> {entry.SavePath}");
    }
}

[AdminCommand(AdminFlags.Server)]
public sealed class ForgePersistenceClearCommand : LocalizedEntityCommands
{
    [Dependency] private readonly PersistentWorldSystem _persistence = default!;

    public override string Command => "forge.persist-clear";
    public override string Description => "Deletes persisted world maps (same as a weekly wipe) and their manifest.";
    public override string Help => Command;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _persistence.ClearPersistence();
        shell.WriteLine("Persistent world files cleared.");
    }
}
