using System.Globalization;
using System.Linq;
using Content.Server.Administration;
using Content.Shared._WF.Administration;
using Content.Shared._WF.Planets.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WF.Planets.Administration;

/// <summary>Console half of Planet Control: lists worlds or sets one's time, weather, gravity or sanction.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class PlanetControlCommand : LocalizedEntityCommands
{
    [Dependency] private PlanetControlSystem _planets = default!;

    private static readonly string[] Fields = { "time", "weather", "gravity", "sanctioned" };

    public override string Command => WolfgateAdminCommands.PlanetControl;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var list = _planets.BuildList();

        if (args.Length == 0)
        {
            foreach (var info in list)
            {
                shell.WriteLine(info.Built
                    ? Loc.GetString("cmd-planetcontrol-row",
                        ("planet", info.Name),
                        ("time", $"{info.MinuteOfDay / 60:00}:{info.MinuteOfDay % 60:00}"),
                        ("weather", info.Weather.Length == 0
                            ? Loc.GetString("cmd-planetcontrol-weather-clear")
                            : info.Weather),
                        ("gravity", $"{info.Gravity:F2}"),
                        ("sanctioned", info.Sanctioned.ToString()))
                    : Loc.GetString("cmd-planetcontrol-row-unbuilt",
                        ("planet", info.Name),
                        ("sanctioned", info.Sanctioned.ToString())));
            }

            return;
        }

        var target = list.FirstOrDefault(p => p.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase));

        if (target == null || args.Length < 3 || !TryBuild(target, args, out var request))
        {
            shell.WriteError(Loc.GetString("cmd-planetcontrol-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        var summary = _planets.Apply(EntityManager.GetEntity(target.Planet), request, shell.Player);
        shell.WriteLine(summary.Length == 0
            ? Loc.GetString("cmd-planetcontrol-no-change")
            : Loc.GetString("cmd-planetcontrol-applied", ("planet", target.Name), ("changes", summary)));
    }

    private static bool TryBuild(PlanetControlInfo target, string[] args, out PlanetControlSetEvent request)
    {
        request = new PlanetControlSetEvent { Planet = target.Planet };

        switch (args[1])
        {
            case "time":
                if (!TimeSpan.TryParseExact(args[2], @"h\:mm", CultureInfo.InvariantCulture, out var time))
                    return false;
                request.MinuteOfDay = (int) time.TotalMinutes % 1440;
                return true;
            case "weather":
                request.SetWeather = true;
                request.Weather = args[2] == "clear" ? null : args[2];
                if (args.Length > 3 && float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                    request.WeatherSeconds = seconds;
                return true;
            case "gravity":
                if (!float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var gravity))
                    return false;
                request.Gravity = gravity;
                return true;
            case "sanctioned":
                if (!bool.TryParse(args[2], out var sanctioned))
                    return false;
                request.Sanctioned = sanctioned;
                return true;
            default:
                return false;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromOptions(_planets.BuildList().Select(p => p.Name)),
            2 => CompletionResult.FromOptions(Fields),
            _ => CompletionResult.Empty,
        };
    }
}
