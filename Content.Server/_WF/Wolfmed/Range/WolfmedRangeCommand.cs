using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Server._WF.Wolfmed.Range;

/// <summary>
/// Loads the Wolfmed range initialised and running, and puts the caller in the middle of it (playtest 4).
/// </summary>
// loadmap leaves a map uninitialised, so the range's species markers never fire and its power net never forms.
[AdminCommand(AdminFlags.Mapping)]
public sealed class WolfmedRangeCommand : LocalizedEntityCommands
{
    public static readonly ResPath MapPath = new("/Maps/_WF/Wolfmed/wolfmed_range.yml");

    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override string Command => "wolfmedrange";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var options = DeserializationOptions.Default with { InitializeMaps = true };
        if (!_loader.TryLoadMap(MapPath, out var map, out var grids, options) || grids.Count == 0)
        {
            shell.WriteError(Loc.GetString("cmd-wolfmedrange-failed", ("path", MapPath.ToString())));
            return;
        }

        var grid = grids.First();
        if (shell.Player?.AttachedEntity is { } player)
            _transform.SetCoordinates(player, new EntityCoordinates(grid, grid.Comp.LocalAABB.Center));

        shell.WriteLine(Loc.GetString("cmd-wolfmedrange-loaded", ("map", map.Value.Comp.MapId.ToString())));
    }
}
