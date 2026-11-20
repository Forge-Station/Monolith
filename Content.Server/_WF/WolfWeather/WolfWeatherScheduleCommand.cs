using System.Globalization;
using Content.Server.Administration;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.WolfWeather;

/// <summary>Admin command that sets, rolls or shows the weather schedule on a map.</summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class WolfWeatherScheduleCommand : LocalizedEntityCommands
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private WeatherScheduleSystem _schedule = default!;

    public override string Command => "wolfweather_schedule";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError(Loc.GetString("cmd-wolfweather_schedule-help"));
            return;
        }

        if (!WolfWeatherCommandHelpers.TryParseMap(shell, _map, args[0], out var map))
            return;

        switch (args[1])
        {
            case "status":
                WriteStatus(shell, map);
                return;
            case "off":
                _schedule.SetSchedule(map, null);
                shell.WriteLine(Loc.GetString("wolf-weather-schedule-command-off"));
                return;
            case "next":
                shell.WriteLine(Loc.GetString(_schedule.RollNow(map)
                    ? "wolf-weather-schedule-command-rolled"
                    : "wolf-weather-schedule-command-not-rolled"));
                WriteStatus(shell, map);
                return;
        }

        if (!_proto.HasIndex<WeatherSchedulePrototype>(args[1]))
        {
            shell.WriteError(Loc.GetString("wolf-weather-schedule-command-bad-schedule"));
            return;
        }

        if (!_schedule.SetSchedule(map, args[1]))
        {
            shell.WriteError(Loc.GetString("wolf-weather-schedule-command-planet"));
            return;
        }

        shell.WriteLine(Loc.GetString("wolf-weather-schedule-command-set", ("schedule", args[1])));
    }

    private void WriteStatus(IConsoleShell shell, EntityUid map)
    {
        var none = Loc.GetString("wolf-weather-schedule-command-none");
        EntityManager.TryGetComponent(map, out WeatherScheduleComponent? schedule);
        EntityManager.TryGetComponent(map, out WolfWeatherMapComponent? mapWeather);

        var seconds = schedule == null ? 0 : Math.Max(0, (schedule.NextChange - _timing.CurTime).TotalSeconds);
        shell.WriteLine(Loc.GetString("wolf-weather-schedule-command-status",
            ("schedule", schedule?.Schedule?.Id ?? none),
            ("weather", mapWeather != null && SharedWolfWeatherSystem.GetCurrent(mapWeather) is { } current ? current.Weather.Id : none),
            ("seconds", seconds.ToString("0", CultureInfo.InvariantCulture)),
            ("next", Loc.GetString(schedule?.Current == null ? "wolf-weather-schedule-command-random" : "wolf-weather-schedule-command-clear")),
            ("waiting", Loc.GetString(schedule?.Waiting == true ? "wolf-weather-schedule-command-yes" : "wolf-weather-schedule-command-no"))));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return WolfWeatherCommandHelpers.MapCompletion(EntityManager);

        if (args.Length != 2)
            return CompletionResult.Empty;

        var options = new List<CompletionOption>
        {
            new("status"),
            new("next"),
            new("off"),
        };
        options.AddRange(CompletionHelper.PrototypeIDs<WeatherSchedulePrototype>());
        return CompletionResult.FromHintOptions(options, Loc.GetString("wolf-weather-schedule-command-hint"));
    }
}
