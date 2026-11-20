using System.Numerics;
using Content.Shared._WF.Warmth;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Warmth;

/// <summary>A small mouse-transparent panel off a planet: the air here and the body feel word.</summary>
public sealed class WFWarmthHud : PanelContainer
{
    /// <summary>Narrowest the panel gets; it widens to fit its longest line, up to <see cref="MaxPanelWidth"/>.</summary>
    public const float PanelWidth = 120f;
    public const float MaxPanelWidth = 220f;
    public const float PanelHeight = 36f;

    private readonly Label _here;
    private readonly Label _feel;
    private readonly WFWarmthGauge _gauge;

    public string HereText => _here.Text ?? string.Empty;
    public string FeelText => _feel.Text ?? string.Empty;

    public WFWarmthHud(IResourceCache resources)
    {
        MinSize = new Vector2(PanelWidth, PanelHeight);
        MaxWidth = MaxPanelWidth;
        MouseFilter = MouseFilterMode.Ignore;

        var panel = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#081013D9"),
            BorderColor = Color.FromHex("#74BFA680"),
            BorderThickness = new Thickness(1),
        };
        panel.SetContentMarginOverride(StyleBox.Margin.Horizontal, 8);
        panel.SetContentMarginOverride(StyleBox.Margin.Vertical, 4);
        PanelOverride = panel;

        var font = new VectorFont(resources.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Regular.ttf"), 9);
        var column = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 0,
            MouseFilter = MouseFilterMode.Ignore,
        };
        AddChild(column);

        _here = MakeLabel(font, Color.FromHex("#A8C8BE"));
        _feel = MakeLabel(font, Color.FromHex("#8FD694"));
        column.AddChild(_here);
        column.AddChild(_feel);

        _gauge = new WFWarmthGauge(font);
        column.AddChild(_gauge);
    }

    public void SetGauge(int gauge, WFWarmthTrend trend, bool nearWarmth)
    {
        _gauge.Set(gauge, trend, nearWarmth);
    }

    public void SetReadout(string here, string feel, Color feelColor)
    {
        if (_here.Text != here)
            _here.Text = here;
        if (_feel.Text != feel)
            _feel.Text = feel;
        _feel.FontColorOverride = feelColor;
    }

    private static Label MakeLabel(Font font, Color color)
    {
        return new Label
        {
            FontOverride = font,
            FontColorOverride = color,
            HorizontalAlignment = HAlignment.Stretch,
            Align = Label.AlignMode.Center,
            MouseFilter = MouseFilterMode.Ignore,
        };
    }
}
