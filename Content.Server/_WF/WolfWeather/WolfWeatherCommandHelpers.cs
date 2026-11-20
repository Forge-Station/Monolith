using Robust.Shared.Console;
using Robust.Shared.Map;

namespace Content.Server._WF.WolfWeather;

/// <summary>Map argument parsing and completion shared by the weather commands.</summary>
public static class WolfWeatherCommandHelpers
{
    /// <summary>Reads a map ID argument and finds its map, writing the error if there is none.</summary>
    public static bool TryParseMap(IConsoleShell shell, SharedMapSystem maps, string arg, out EntityUid map)
    {
        map = default;
        if (int.TryParse(arg, out var mapInt) && maps.TryGetMap(new MapId(mapInt), out var found))
        {
            map = found.Value;
            return true;
        }

        shell.WriteError(Loc.GetString("wolf-weather-command-bad-map"));
        return false;
    }

    /// <summary>Completion for a map ID argument.</summary>
    public static CompletionResult MapCompletion(IEntityManager entities)
    {
        return CompletionResult.FromHintOptions(CompletionHelper.MapIds(entities),
            Loc.GetString("wolf-weather-command-hint-map"));
    }
}
