using System.Numerics;
using Content.Client._WF.Warmth;
using Content.Shared._WF.Warmth;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Planets;

/// <summary>A compact, mouse-transparent readout created once and updated only when its text changes.</summary>
public sealed class WFPlanetTimepieceHud : PanelContainer
{
    /// <summary>Narrowest the panel gets; it widens to fit its longest line, up to <see cref="MaxPanelWidth"/>.</summary>
    public const float PanelWidth = 164f;
    public const float MaxPanelWidth = 240f;
    public const float PanelHeight = 98f;

    private readonly Label _planet;
    private readonly Label _time;
    private readonly Label _weather;
    private readonly Label _climate;
    private readonly Label _here;
    private readonly Label _feel;
    private readonly WFWarmthGauge _gauge;

    public bool GaugeVisible => _gauge.Visible;
    public string PlanetText => _planet.Text ?? string.Empty;
    public string TimeText => _time.Text ?? string.Empty;
    public string WeatherText => _weather.Text ?? string.Empty;
    public string ClimateText => _climate.Text ?? string.Empty;
    public string HereText => _here.Text ?? string.Empty;
    public string FeelText => _feel.Text ?? string.Empty;

    public WFPlanetTimepieceHud(IResourceCache resources)
    {
        MinSize = new Vector2(PanelWidth, PanelHeight);
        MaxWidth = MaxPanelWidth;
        MouseFilter = Control.MouseFilterMode.Ignore;

        var panel = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#081013D9"),
            BorderColor = Color.FromHex("#74BFA680"),
            BorderThickness = new Thickness(1),
        };
        panel.SetContentMarginOverride(StyleBox.Margin.Horizontal, 8);
        panel.SetContentMarginOverride(StyleBox.Margin.Vertical, 5);
        PanelOverride = panel;

        var regular = new VectorFont(
            resources.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Regular.ttf"),
            9);
        var clock = new VectorFont(
            resources.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf"),
            20);

        var column = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 0,
            MouseFilter = Control.MouseFilterMode.Ignore,
        };
        AddChild(column);

        _planet = MakeLabel(regular, Color.FromHex("#A8C8BE"));
        _time = MakeLabel(clock, Color.FromHex("#D8FFF2"));
        _weather = MakeLabel(regular, Color.FromHex("#78AA9B"));
        _climate = MakeLabel(regular, Color.FromHex("#A8C8BE"));

        column.AddChild(_planet);
        column.AddChild(_time);
        column.AddChild(_weather);
        column.AddChild(_climate);

        // The air on the wearer's own tile and how warm their body feels, the word coloured by danger.
        _here = MakeLabel(regular, Color.FromHex("#A8C8BE"));
        _here.HorizontalAlignment = HAlignment.Right;
        _here.HorizontalExpand = true;
        _feel = MakeLabel(regular, Color.FromHex("#8FD694"));
        _feel.HorizontalAlignment = HAlignment.Left;
        _feel.HorizontalExpand = true;
        var thermal = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            MouseFilter = Control.MouseFilterMode.Ignore,
        };
        thermal.AddChild(_here);
        thermal.AddChild(_feel);
        column.AddChild(thermal);

        _gauge = new WFWarmthGauge(regular) { Visible = false };
        column.AddChild(_gauge);
    }

    /// <summary>The body temperature gauge under the thermal row; hidden while the body has no reading.</summary>
    public void SetGauge(bool show, int gauge, WFWarmthTrend trend, bool nearWarmth)
    {
        _gauge.Visible = show;
        if (show)
            _gauge.Set(gauge, trend, nearWarmth);
    }

    /// <summary>The thermal row: the tile's air temperature and the body feel word in its danger colour.</summary>
    public void SetThermal(string here, string feel, Color feelColor)
    {
        if (_here.Text != here)
            _here.Text = here;
        if (_feel.Text != feel)
            _feel.Text = feel;
        _feel.FontColorOverride = feelColor;
        _feel.Visible = feel.Length > 0;
    }

    public void SetReadout(string planet, string time, string weather, string climate = "", Color? climateColor = null)
    {
        if (_climate.Text != climate)
            _climate.Text = climate;
        _climate.FontColorOverride = climateColor ?? Color.FromHex("#A8C8BE");
        if (_planet.Text != planet)
            _planet.Text = planet;
        if (_time.Text != time)
            _time.Text = time;
        if (_weather.Text != weather)
            _weather.Text = weather;
    }

    private static Label MakeLabel(Font font, Color color)
    {
        return new Label
        {
            FontOverride = font,
            FontColorOverride = color,
            HorizontalAlignment = HAlignment.Stretch,
            Align = Label.AlignMode.Center,
            MouseFilter = Control.MouseFilterMode.Ignore,
        };
    }
}
