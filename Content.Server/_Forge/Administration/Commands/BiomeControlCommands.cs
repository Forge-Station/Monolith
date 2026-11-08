using System.Globalization;
using System.Text;
using Content.Server._Forge.BiomeSystem;
using Content.Server.Administration;
using Content.Server.Worldgen;
using Content.Server.Worldgen.Prototypes;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Administration.Commands;

internal static class BiomeCommandHelpers
{
    public static bool ParseInt(string s, out int value)
    {
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static string RectMeters(BiomeRect r)
    {
        return
            $"{r.MinX * WorldGen.ChunkSize}..{r.MaxX * WorldGen.ChunkSize} м (X), " +
            $"{r.MinY * WorldGen.ChunkSize}..{r.MaxY * WorldGen.ChunkSize} м (Y)";
    }

    public static string Shape(BiomePrototype biome)
    {
        if (BiomeControlSystem.GetYamlRect(biome) is { } rect)
            return $"rect {rect} ({RectMeters(rect)})";

        if (BiomeControlSystem.GetYamlDistance(biome) is { } range)
            return $"кольцо {range.X}..{range.Y} м";

        return $"шум ({biome.NoiseRanges.Count} каналов: {string.Join(", ", biome.NoiseRanges.Keys)})";
    }
}

[AdminCommand(AdminFlags.Mapping)]
public sealed partial class BiomeInfoCommand : IConsoleCommand
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public string Command => "biomeinfo";
    public string Description => "Показывает YAML-значения, прототипа биома";
    public string Help => "biomeinfo <biomeId>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError($"Использование: {Help}");
            return;
        }

        ProtoId<BiomePrototype> biomeId = args[0];

        if (!_proto.TryIndex(biomeId, out var biome))
        {
            shell.WriteError($"Неизвестный биом '{biomeId}' (см. biomelist).");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"=== {biomeId} (priority {biome.Priority}) ===");
        sb.Append("YAML: ");
        sb.Append(BiomeCommandHelpers.Shape(biome));

        shell.WriteLine(sb.ToString().TrimEnd());
    }
}

[AdminCommand(AdminFlags.Mapping)]
public sealed partial class BiomeListCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public string Command => "biomelist";
    public string Description => "Показывает список биомов на карте";
    public string Help => "biomelist <mapId>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError($"Использование: {Help}");
            return;
        }

        if (!BiomeCommandHelpers.ParseInt(args[0], out var id))
        {
            shell.WriteError($"Некорректный id: {args[0]}");
            return;
        }

        EntityUid mapId = new(id);

        if (!mapId.IsValid() || !_entManager.EntityExists(mapId))
        {
            shell.WriteError($"Некорректный id: {args[0]}");
            return;
        }

        var biomeControlSystem = _entManager.System<BiomeControlSystem>();
        var biomes = biomeControlSystem.GetBiomes(mapId);

        foreach (var biome in biomes)
        {
            shell.WriteLine(biome);
        }
    }
}

[AdminCommand(AdminFlags.Mapping)]
public sealed partial class BiomeOffsetCommand : IConsoleCommand
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;

    public string Command => "biomeoffsetrect";
    public string Description => "Сдвигает прямоугольник биома на указанное число чанков";
    public string Help => "biomeoffsetrect <biomeId> <dxЧанков> <dyЧанков>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 3)
        {
            shell.WriteError($"Использование: {Help}");
            return;
        }

        ProtoId<BiomePrototype> biomeId = args[0];

        if (!BiomeCommandHelpers.ParseInt(args[1], out var dx) || !BiomeCommandHelpers.ParseInt(args[2], out var dy))
        {
            shell.WriteError($"Сдвиги dx/dy должны быть целыми числами чанков (1 чанк = {WorldGen.ChunkSize} м).");
            return;
        }

        if (!_proto.TryIndex(biomeId, out var biomePrototype))
        {
            shell.WriteError($"Неизвестный биом '{biomeId}' (см. biomelist).");
            return;
        }

        var oldRect = BiomeControlSystem.GetYamlRect(biomePrototype) ?? default;
        var biomeControlSystem = _entManager.System<BiomeControlSystem>();
        var isError = !biomeControlSystem.TryOffset(biomeId, new(dx, dy), out var resultStr);

        var newRect = BiomeControlSystem.GetYamlRect(biomePrototype) ?? default;

        if (isError)
            shell.WriteError(resultStr);
        {
            shell.WriteLine(resultStr);
            shell.WriteLine($"Было:  {BiomeCommandHelpers.RectMeters(oldRect)}");
            shell.WriteLine($"Стало: {BiomeCommandHelpers.RectMeters(newRect)}");
        }
    }
}

[AdminCommand(AdminFlags.Mapping)]
public sealed partial class BiomeMoveCommand : IConsoleCommand
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;

    public string Command => "biomemoverect";
    public string Description => "Задаёт позицию прямоугольника биома";
    public string Help => "biomemoverect <biomeId> <X> <Y> <width> <height>    x,y - левый верхнийугол прямоугольника.";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 5)
        {
            shell.WriteError($"Использование: {Help}");
            return;
        }

        ProtoId<BiomePrototype> biomeId = args[0];

        if (!BiomeCommandHelpers.ParseInt(args[1], out var x) || !BiomeCommandHelpers.ParseInt(args[2], out var y) ||
            !BiomeCommandHelpers.ParseInt(args[3], out var width) || !BiomeCommandHelpers.ParseInt(args[4], out var height))
        {
            shell.WriteError($"координаты x/y должны быть целыми числами чанков (1 чанк = {WorldGen.ChunkSize} м).");
            return;
        }

        if (!_proto.TryIndex(biomeId, out var biomePrototype))
        {
            shell.WriteError($"Неизвестный биом '{biomeId}' (см. biomelist).");
            return;
        }

        var oldRect = BiomeControlSystem.GetYamlRect(biomePrototype) ?? default;

        var biomeControlSystem = _entManager.System<BiomeControlSystem>();
        var isError = !biomeControlSystem.TryMove(biomeId, new(x, y), new(width, height), out var resultStr);

        var newRect = BiomeControlSystem.GetYamlRect(biomePrototype) ?? default;

        if (isError)
            shell.WriteError(resultStr);
        else
        {
            shell.WriteLine(resultStr);
            shell.WriteLine($"Было:  {BiomeCommandHelpers.RectMeters(oldRect)}");
            shell.WriteLine($"Стало: {BiomeCommandHelpers.RectMeters(newRect)}");
        }
    }
}

[AdminCommand(AdminFlags.Mapping)]
public sealed partial class BiomeRefreshCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public string Command => "biomerefresh";
    public string Description => "Удаляет прогруженные чанки, у которых был изменён биом.";
    public string Help => "biomerefresh <mapId>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError($"Использование: {Help}");
            return;
        }

        if (!BiomeCommandHelpers.ParseInt(args[0], out var id))
        {
            shell.WriteError($"Некорректный id: {args[0]}");
            return;
        }

        EntityUid mapId = new(id);

        if (!mapId.IsValid() || !_entManager.EntityExists(mapId))
        {
            shell.WriteError($"Некорректный id: {args[0]}");
            return;
        }

        var biomeControlSystem = _entManager.System<BiomeControlSystem>();
        biomeControlSystem.RefreshChunks(mapId);
    }
}
