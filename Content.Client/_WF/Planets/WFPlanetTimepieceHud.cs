using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Planets;

/// <summary>A compact, mouse-transparent readout created once and updated only when its text changes.</summary>
public sealed class WFPlanetTimepieceHud : PanelContainer
{
    public const float PanelWidth = 164f;

    private readonly Label _planet;
    private readonly Label _time;
    private readonly Label _weather;

    public string PlanetText => _planet.Text ?? string.Empty;
    public string TimeText => _time.Text ?? string.Empty;
    public string WeatherText => _weather.Text ?? string.Empty;

    public WFPlanetTimepieceHud(IResourceCache resources)
    {
        SetWidth = PanelWidth;
        MinSize = new Vector2(PanelWidth, 72);
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

        column.AddChild(_planet);
        column.AddChild(_time);
        column.AddChild(_weather);
    }

    public void SetReadout(string planet, string time, string weather)
    {
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
            ClipText = true,
            MouseFilter = Control.MouseFilterMode.Ignore,
        };
    }
}
