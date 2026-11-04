using System.Globalization;
using Content.Server.Administration;
using System.Linq;
using Content.Shared._WF.Planets;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.WolfWeather;

/// <summary>Admin command that sets or clears the weather on a map.</summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class WolfWeatherCommand : LocalizedEntityCommands
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private WolfWeatherSystem _wolfWeather = default!;

    public override string Command => "wolfweather";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 2 or > 3)
        {
            shell.WriteError(Loc.GetString("cmd-wolfweather-help"));
            return;
        }

        if (!WolfWeatherCommandHelpers.TryParseMap(shell, _map, args[0], out var mapUid))
            return;

        ProtoId<WolfWeatherPrototype>? weather = null;
        if (args[1] != "clear")
        {
            if (!_proto.HasIndex<WolfWeatherPrototype>(args[1]))
            {
                shell.WriteError(Loc.GetString("wolf-weather-command-bad-weather"));
                return;
            }

            weather = args[1];
        }

        if (weather != null && !_wolfWeather.IsSurface(mapUid))
        {
            shell.WriteError(Loc.GetString("wolf-weather-command-not-surface"));
            return;
        }

        TimeSpan? duration = null;
        if (args.Length == 3)
        {
            if (!float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || !float.IsFinite(seconds)
                || seconds <= 0f
                || seconds > SharedWolfWeatherSystem.MaxDuration.TotalSeconds)
            {
                shell.WriteError(Loc.GetString("wolf-weather-command-bad-seconds"));
                return;
            }

            duration = TimeSpan.FromSeconds(seconds);
        }

        _wolfWeather.SetWeather(mapUid, weather, duration);
        shell.WriteLine(Loc.GetString("wolf-weather-command-set", ("weather", args[1])));
        if (EntityManager.HasComponent<WFPlanetLayerComponent>(mapUid))
            shell.WriteLine(Loc.GetString("wolf-weather-command-planet"));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => WolfWeatherCommandHelpers.MapCompletion(EntityManager),
            2 => CompletionResult.FromHintOptions(
                CompletionHelper.PrototypeIDs<WolfWeatherPrototype>().Append(new CompletionOption("clear")),
                Loc.GetString("wolf-weather-command-hint-weather")),
            3 => CompletionResult.FromHint(Loc.GetString("wolf-weather-command-hint-seconds")),
            _ => CompletionResult.Empty,
        };
    }
}
