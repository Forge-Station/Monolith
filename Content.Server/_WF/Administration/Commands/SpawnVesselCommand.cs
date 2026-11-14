using System.Linq;
using Content.Server._WF.Administration.Systems;
using Content.Server.Administration;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Administration.Commands;

/// <summary>
/// Spawns a vessel prototype at the calling admin's position, optionally registering it to a player.
/// </summary>
[AdminCommand(AdminFlags.Spawn)]
public sealed partial class SpawnVesselCommand : LocalizedEntityCommands
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private AdminVesselSpawnSystem _vesselSpawn = default!;

    public override string Command => WolfgateAdminCommands.SpawnVessel;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError(Loc.GetString("cmd-spawnvessel-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        if (shell.Player?.AttachedEntity is not { Valid: true } player)
        {
            shell.WriteError(Loc.GetString("cmd-spawnvessel-no-entity"));
            return;
        }

        if (!_prototypeManager.TryIndex<VesselPrototype>(args[0], out var vessel) || vessel.Abstract)
        {
            shell.WriteError(Loc.GetString("cmd-spawnvessel-unknown-vessel", ("id", args[0])));
            return;
        }

        var xform = EntityManager.GetComponent<TransformComponent>(player);
        if (xform.MapID == MapId.Nullspace)
        {
            shell.WriteError(Loc.GetString("cmd-spawnvessel-no-map"));
            return;
        }

        // Resolve the owner before spawning so a bad owner leaves nothing behind.
        ICommonSession? owner = null;
        if (args.Length == 2 && !_players.TryGetSessionByUsername(args[1], out owner))
        {
            shell.WriteError(Loc.GetString("cmd-spawnvessel-owner-not-found", ("name", args[1])));
            return;
        }

        if (!_vesselSpawn.TrySpawnVessel(vessel, xform.MapID, _transform.GetWorldPosition(xform), player, out var grid))
        {
            shell.WriteError(Loc.GetString("cmd-spawnvessel-failed", ("id", vessel.ID)));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-spawnvessel-success", ("name", vessel.Name), ("uid", grid.Value)));

        if (owner == null)
            return;

        // No card to hold the deed (a ghost, say) still registers the ship to the player's account.
        var hasCard = _vesselSpawn.TryGetDeedCard(owner, out var idCard, out var noCardKey);
        if (!_vesselSpawn.TryAssignOwner(grid.Value, vessel, idCard, owner))
            shell.WriteError(Loc.GetString("cmd-spawnvessel-owner-failed", ("name", owner.Name)));
        else if (hasCard)
            shell.WriteLine(Loc.GetString("cmd-spawnvessel-owner-assigned", ("name", owner.Name)));
        else
            shell.WriteLine(Loc.GetString("cmd-spawnvessel-owner-registered", ("name", owner.Name),
                ("reason", Loc.GetString(noCardKey!, ("name", owner.Name)))));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        switch (args.Length)
        {
            case 1:
                var vessels = _prototypeManager.EnumeratePrototypes<VesselPrototype>()
                    .Where(vessel => !vessel.Abstract)
                    .OrderBy(vessel => vessel.ID)
                    .Select(vessel => new CompletionOption(vessel.ID, vessel.Name));
                return CompletionResult.FromHintOptions(vessels, Loc.GetString("cmd-spawnvessel-hint"));
            case 2:
                var players = _players.Sessions.Select(session => session.Name).OrderBy(name => name);
                return CompletionResult.FromHintOptions(players, Loc.GetString("cmd-spawnvessel-owner-hint"));
            default:
                return CompletionResult.Empty;
        }
    }
}
