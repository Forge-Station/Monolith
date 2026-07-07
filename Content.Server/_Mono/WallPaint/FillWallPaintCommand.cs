using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Toolshed;

namespace Content.Server._Mono.WallPaint;

[AdminCommand(AdminFlags.Mapping)]
public sealed partial class FillWallPaintCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IEntitySystemManager _systemManager = default!;

    public string Command => "fpaint";
    public string Description => "Paints every paintable wall/window on a grid.";
    public string Help => $"{Command} <grid id> #<color>";

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(CompletionHelper.Components<MapGridComponent>(args[0], _entManager), "<grid id>"),
            2 => CompletionResult.FromHint("#RRGGBB or #RRGGBBAA"),
            _ => CompletionResult.Empty,
        };
    }

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError(Help);
            return;
        }

        if (!NetEntity.TryParse(args[0], out var gridNetId) ||
            !_entManager.TryGetEntity(gridNetId, out var gridUid) ||
            !_entManager.TryGetComponent(gridUid.Value, out TransformComponent? transform) ||
            !_entManager.HasComponent<MapGridComponent>(gridUid.Value))
        {
            shell.WriteError($"Failed to parse grid id '{args[0]}'. Expected an entity id like n123.");
            return;
        }

        var mapSystem = _systemManager.GetEntitySystem<SharedMapSystem>();
        if (!mapSystem.IsPaused(transform.MapID))
        {
            shell.WriteError("Wall paint is only available on paused mapping maps.");
            return;
        }

        var remove = args[1].Equals("clear", StringComparison.OrdinalIgnoreCase);
        var color = Color.White;

        if (!remove)
        {
            var parsed = Color.TryFromHex(args[1]);
            if (parsed == null)
            {
                shell.WriteError($"Failed to parse color '{args[1]}'. Expected #RRGGBB or #RRGGBBAA.");
                return;
            }

            color = parsed.Value;
        }

        var wallPaint = _systemManager.GetEntitySystem<WallPaintSystem>();
        var count = wallPaint.PaintGrid(gridUid.Value, color, remove);
        var action = remove ? "Cleared paint from" : $"Painted with {color.ToHex()}";
        shell.WriteLine($"{action} {count} walls/windows.");
    }
}
