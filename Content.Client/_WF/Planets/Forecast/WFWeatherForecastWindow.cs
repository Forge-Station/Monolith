using System.Numerics;
using Content.Shared._WF.Planets.Forecast;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._WF.Planets.Forecast;

/// <summary>A world picker, its season and weather now, and a table of what is forecast.</summary>
public sealed class WFWeatherForecastWindow : DefaultWindow
{
    public event Action<NetEntity>? OnWorldSelected;

    private readonly OptionButton _worlds;
    private readonly Label _season;
    private readonly Label _now;
    private readonly Label _off;
    private readonly GridContainer _table;
    private readonly List<NetEntity> _worldIds = new();
    private bool _filling;

    public WFWeatherForecastWindow()
    {
        Title = Loc.GetString("wf-forecast-console-title");
        MinSize = new Vector2(480, 360);
        SetSize = new Vector2(540, 460);

        var column = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(6),
        };
        Contents.AddChild(column);

        var picker = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        picker.AddChild(new Label { Text = Loc.GetString("wf-forecast-console-world"), VerticalAlignment = VAlignment.Center });
        _worlds = new OptionButton { HorizontalExpand = true };
        _worlds.OnItemSelected += args =>
        {
            _worlds.SelectId(args.Id);
            if (!_filling && args.Id >= 0 && args.Id < _worldIds.Count)
                OnWorldSelected?.Invoke(_worldIds[args.Id]);
        };
        picker.AddChild(_worlds);
        column.AddChild(picker);

        _season = new Label();
        _now = new Label();
        _off = new Label { StyleClasses = { "LabelSubText" }, Visible = false };
        column.AddChild(_season);
        column.AddChild(_now);
        column.AddChild(_off);

        _table = new GridContainer { Columns = 5, HSeparationOverride = 12, VSeparationOverride = 2 };
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_table);
        column.AddChild(scroll);
    }

    public void UpdateState(WFWeatherForecastState state)
    {
        _filling = true;
        _worlds.Clear();
        _worldIds.Clear();
        foreach (var world in state.Worlds)
        {
            _worlds.AddItem(world.Name, _worldIds.Count);
            _worldIds.Add(world.World);
        }

        var selected = state.Selected is { } picked ? _worldIds.IndexOf(picked) : -1;
        if (selected >= 0)
            _worlds.SelectId(selected);
        _filling = false;

        _table.RemoveAllChildren();
        if (state.Worlds.Count == 0 || state.Rows.Count == 0)
        {
            _season.Text = state.Worlds.Count == 0 ? Loc.GetString("wf-forecast-console-no-worlds") : string.Empty;
            _now.Text = string.Empty;
            _off.Visible = false;
            return;
        }

        _season.Text = state.Season is { } season
            ? Loc.GetString("wf-forecast-console-season", ("season", Loc.GetString(season)))
            : Loc.GetString("wf-forecast-console-no-season");
        _season.FontColorOverride = state.Season != null ? state.SeasonColor : null;
        var now = state.Rows[0];
        _now.Text = Loc.GetString("wf-forecast-console-now",
            ("weather", WeatherName(now.Weather)), ("temperature", FormatTemperature(now)));
        _off.Text = Loc.GetString("wf-forecast-console-off");
        _off.Visible = !state.Cycling;

        foreach (var header in new[] { "when", "local", "weather", "temperature", "flags" })
        {
            _table.AddChild(new Label { Text = Loc.GetString($"wf-forecast-console-header-{header}"), StyleClasses = { "LabelKeyText" } });
        }

        for (var i = 1; i < state.Rows.Count; i++)
        {
            var row = state.Rows[i];
            _table.AddChild(new Label { Text = FormatWhen(row) });
            _table.AddChild(new Label { Text = $"{row.MinuteOfDay / 60:00}:{row.MinuteOfDay % 60:00}" });
            _table.AddChild(new Label { Text = FormatWeather(row), HorizontalExpand = true });
            _table.AddChild(new Label { Text = FormatTemperature(row) });
            _table.AddChild(new Label { Text = FormatFlags(row) });
        }
    }

    public static string WeatherName(string weather)
    {
        if (weather.Length == 0)
            return Loc.GetString("wf-planet-weather-clear");
        return Loc.TryGetString("wf-planet-weather-" + weather, out var name) ? name : weather;
    }

    /// <summary>How far off a row is; later rows are rounded to ten minutes.</summary>
    public static string FormatWhen(WFForecastRow row)
    {
        var minutes = (int) MathF.Ceiling(row.InSeconds / 60f);
        if (row.Certainty == WFForecastCertainty.Possible)
            minutes = (int) MathF.Round(minutes / 10f) * 10;
        return minutes < 60
            ? Loc.GetString("wf-forecast-console-in", ("minutes", minutes))
            : Loc.GetString("wf-forecast-console-in-hours", ("hours", minutes / 60), ("minutes", $"{minutes % 60:00}"));
    }

    public static string FormatWeather(WFForecastRow row)
    {
        return Loc.GetString("wf-forecast-console-weather", ("certainty", (int) row.Certainty), ("weather", WeatherName(row.Weather)));
    }

    /// <summary>The forecast temperature: exact for the next change, to the nearest five degrees after.</summary>
    public static string FormatTemperature(WFForecastRow row)
    {
        if (row.Certainty == WFForecastCertainty.Exact)
            return Loc.GetString("wf-planet-temperature", ("celsius", row.Celsius));
        var rounded = (int) MathF.Round(row.Celsius / 5f) * 5;
        return Loc.GetString("wf-forecast-console-temperature-about",
            ("temperature", Loc.GetString("wf-planet-temperature", ("celsius", rounded))));
    }

    private static string FormatFlags(WFForecastRow row)
    {
        var flags = new List<string>();
        if (row.Wind)
            flags.Add(Loc.GetString("wf-forecast-console-wind"));
        if (row.Lightning)
            flags.Add(Loc.GetString("wf-forecast-console-lightning"));
        return string.Join(", ", flags);
    }
}
