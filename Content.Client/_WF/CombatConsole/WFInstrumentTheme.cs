using System.Linq;
using System.Numerics;
using Content.Client.Shuttles.UI;
using Content.Client._WF.Stylesheets;
using Content.Client.Resources;
using Content.Shared._WF.CCVar;
using Robust.Shared.Configuration;
using Content.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client._WF.CombatConsole;

/// <summary>Builds metal instrument housings, amber displays and tactile console switches.</summary>
public static class WFInstrumentTheme
{
    /// <summary>Uses the same authoritative palette as the player's Wolfgate UI style, read from the CVar outside a session.</summary>
    public static WolfgateSkin Skin => IoCManager.Resolve<IEntityManager>().TrySystem(out WFConsoleThemeSystem? theme)
        ? theme.Skin
        : WolfgateSkins.Get(IoCManager.Resolve<IConfigurationManager>().GetCVar(WolfgateCVars.UiStyle));
    /// <summary>Futurist instruments replace physical chrome with luminous digital geometry.</summary>
    public static bool Digital => Skin == WolfgateSkins.Futurist;
    public static Color Accent => Skin.Accent;
    public static Color Ink => Skin.Ink;
    public static Color Cream => Skin.Text;
    public static Color Muted => Skin.TextMuted;
    public static Color Red => Skin.Danger;
    public static Color Green => Skin.Good;

    private static Font Mono => IoCManager.Resolve<IResourceCache>().GetFont(Skin.MenuFonts, 12);

    /// <summary>Styles the current controls and follows later skin changes without rebuilding their state.</summary>
    public static void Install(Control root)
    {
        Apply(root);
        root.AddChild(new WFConsoleThemeBinding(root));
    }

    /// <summary>Fits the initial deck to the available screen at the player's UI scale.</summary>
    public static void FitWindow(BaseWindow window, Vector2 preferred)
    {
        var available = IoCManager.Resolve<IUserInterfaceManager>().RootControl.Size - new Vector2(24);
        window.SetSize = Vector2.Max(window.MinSize, Vector2.Min(preferred, available));
    }

    /// <summary>Constructs a padded, square-edged instrument face.</summary>
    public static StyleBoxFlat Face(Color fill, Color edge, int padding = 8)
    {
        var box = new StyleBoxFlat { BackgroundColor = fill, BorderColor = edge, BorderThickness = new Thickness(1) };
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, padding);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, padding);
        return box;
    }

    /// <summary>Styles a switch locally, including pressed, hover and disabled states.</summary>
    public static void Switch(ContainerButton button)
    {
        Label label;
        if (button is Button key)
        {
            label = key.Label;
            key.ClipText = true;
        }
        else if (button is CheckBox check)
        {
            label = check.Label;
            check.ClipText = true;
            check.TextureRect.Visible = false;
            check.Label.HorizontalExpand = true;
        }
        else
            return;
        // Explicit overrides encode status and access colors owned by the source panel.
        if (button.StyleBoxOverride is WFConsoleStyleBox)
            button.StyleBoxOverride = null;
        button.MuteSounds = true;
        button.OnPressed -= WFConsoleAudio.Press;
        if (!button.HasStyleClass("WfBearingDetent"))
            button.OnPressed += WFConsoleAudio.Press;
        var compact = button.HasStyleClass("WfCompact");
        var toggle = button.ToggleMode && button.Group == null && !compact;
        // The per-button stylesheet below replaces the stock rule that centres button text.
        if (button is Button)
        {
            label.Align = button.HasStyleClass("WfWeapon") ? Robust.Client.UserInterface.Controls.Label.AlignMode.Left :
                Robust.Client.UserInterface.Controls.Label.AlignMode.Center;
            label.VAlign = Robust.Client.UserInterface.Controls.Label.VAlignMode.Center;
        }
        var header = button.HasStyleClass("WfHeader");
        var compactSize = button.HasStyleClass("WfWeaponDense") ? 8 : 10;
        var font = compact ? IoCManager.Resolve<IResourceCache>().GetFont(Skin.MenuFonts, compactSize) : Mono;
        var danger = button.HasStyleClass("WfDispense");
        var amber = button.HasStyleClass("WfGroup");
        var normal = danger ? "button_red" : amber ? "button_amber" : "button_up";
        var down = danger ? "button_red_down" : amber || button.Group != null ? "button_amber_down" : "button_down";
        WFConsoleStyleBox SwitchFace(string face, bool pressed, bool disabled = false)
        {
            var box = new WFConsoleStyleBox(face, toggle, pressed, disabled, disabled ? button : null, compact);
            if (button.HasStyleClass("WfWeaponTight"))
                box.SetContentMarginOverride(StyleBox.Margin.Vertical, 0);
            if (header)
                box.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);
            return box;
        }
        button.Stylesheet = new Stylesheet(new StyleRule[]
        {
            Element<Label>().Prop(Robust.Client.UserInterface.Controls.Label.StylePropertyFont, font)
                .Prop(Robust.Client.UserInterface.Controls.Label.StylePropertyFontColor, Digital ? Cream : amber ? Ink : Cream),
            Child().Parent(Element<ContainerButton>().Pseudo(ContainerButton.StylePseudoClassPressed)).Child(Element<Label>())
                .Prop(Robust.Client.UserInterface.Controls.Label.StylePropertyFontColor, Digital ? Accent : amber || button.Group != null ? Ink : Cream),
            Child().Parent(Element<ContainerButton>().Pseudo(ContainerButton.StylePseudoClassDisabled)).Child(Element<Label>())
                .Prop(Robust.Client.UserInterface.Controls.Label.StylePropertyFontColor, Muted),
            Element<ContainerButton>().Prop(ContainerButton.StylePropertyStyleBox, SwitchFace(normal, false)),
            Element<ContainerButton>().Pseudo(ContainerButton.StylePseudoClassHover)
                .Prop(ContainerButton.StylePropertyStyleBox, SwitchFace(danger || amber ? normal : "button_hover", false)),
            Element<ContainerButton>().Pseudo(ContainerButton.StylePseudoClassPressed)
                .Prop(ContainerButton.StylePropertyStyleBox, SwitchFace(down, true)),
            Element<ContainerButton>().Pseudo(ContainerButton.StylePseudoClassDisabled)
                .Prop(ContainerButton.StylePropertyStyleBox, SwitchFace(normal, false, true)),
        });
        label.FontColorOverride = null;
        // A window-header key shares the title bar's height instead of growing it.
        button.MinHeight = header ? 26 : compact ? 32 : Math.Max(button.MinHeight, toggle ? 38 : 32);
        button.MinWidth = Math.Max(button.MinWidth, 32);
    }

    /// <summary>Creates a localized console button.</summary>
    public static Button Button(string key, bool toggle = false)
    {
        var button = new Button { Text = Loc.GetString(key), ToggleMode = toggle, HorizontalExpand = true };
        Switch(button);
        return button;
    }

    /// <summary>Creates a localized engraved caption.</summary>
    public static Label Label(string key, Color? color = null) => new()
    {
        Text = Loc.GetString(key), FontOverride = Mono, FontColorOverride = color ?? Cream,
        Margin = new Thickness(2, 3),
    };

    /// <summary>Moves a bound control without replacing its event handlers.</summary>
    public static T Detach<T>(T control) where T : Control
    {
        control.Parent?.RemoveChild(control);
        return control;
    }

    /// <summary>Builds a vertical stack from existing controls.</summary>
    public static BoxContainer Column(params Control[] controls)
    {
        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        foreach (var control in controls)
            box.AddChild(Detach(control));
        return box;
    }

    /// <summary>Builds a horizontal bank of controls.</summary>
    public static BoxContainer Row(params Control[] controls)
    {
        var box = new BoxContainer { SeparationOverride = 6, HorizontalExpand = true };
        foreach (var control in controls)
            box.AddChild(Detach(control));
        return box;
    }

    /// <summary>Wraps a titled instrument in its own bolted housing; a smaller padding or title gap frees height in tall columns.</summary>
    public static WFInstrumentPanel Panel(string key, Control content, bool expand = false, int padding = 12, int gap = 6)
    {
        var body = Column(Label(key, Accent), content);
        body.SeparationOverride = gap;
        body.HorizontalExpand = true;
        body.VerticalExpand = expand;
        var panel = new WFInstrumentPanel(padding) { HorizontalExpand = true, VerticalExpand = expand };
        panel.AddChild(body);
        return panel;
    }

    /// <summary>Gives lists their own scrolling region at smaller window sizes.</summary>
    public static ScrollContainer Scroll(Control content)
    {
        var scroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, HorizontalExpand = true };
        scroll.AddChild(Detach(content));
        return scroll;
    }

    /// <summary>Frames the existing interactive radar with a passive CRT bezel.</summary>
    public static Control Scope(string key, Control radar, Vector2? minimum = null)
    {
        Detach(radar);
        radar.Margin = new Thickness(0);
        radar.SetSize = new Vector2(float.NaN, float.NaN);
        radar.MinSize = Vector2.Zero;
        if (radar is MapGridControl plot)
            plot.WfFitInstrument = true;
        radar.HorizontalExpand = radar.VerticalExpand = true;
        radar.HorizontalAlignment = Control.HAlignment.Stretch;
        radar.VerticalAlignment = Control.VAlignment.Stretch;
        var layers = ScopeLayers(radar, minimum ?? new Vector2(240, 220));
        var bezel = new WFScreenBezel { HorizontalExpand = true, VerticalExpand = true };
        bezel.AddChild(layers);
        return Panel(key, bezel, true);
    }

    /// <summary>Fills a plotting surface while keeping radar controls above its glass.</summary>
    public static LayoutContainer ScopeLayers(Control radar, Vector2 minimum)
    {
        var layers = new LayoutContainer { HorizontalExpand = true, VerticalExpand = true, MinSize = minimum };
        var background = new PanelContainer { PanelOverride = Face(Ink, Ink, 0), MouseFilter = Control.MouseFilterMode.Ignore };
        var glass = new WFCrtGlass();
        foreach (var layer in new[] { background, radar, glass })
        {
            layers.AddChild(Detach(layer));
            LayoutContainer.SetAnchorAndMarginPreset(layer, LayoutContainer.LayoutPreset.Wide);
        }
        if (radar is ShuttleNavControl navigation)
            navigation.WfAttachRadarControls(layers);
        return layers;
    }

    /// <summary>Applies the instrument palette to existing bound controls.</summary>
    public static void Apply(Control root, WolfgateSkin? previous = null)
    {
        if (root.HasStyleClass("WfNativeStyle"))
            return;
        if (root is Slider slider)
        {
            slider.BackgroundStyleBoxOverride = Face(Ink, Muted, 0);
            slider.ForegroundStyleBoxOverride = Face(Color.Transparent, Color.Transparent, 0);
            slider.FillStyleBoxOverride = Face(Accent.WithAlpha(0.25f), Color.Transparent, 0);
            slider.GrabberStyleBoxOverride = new WFKnobStyleBox();
            slider.MinHeight = 28;
            slider.OnReleased -= WFConsoleAudio.Release;
            slider.OnReleased += WFConsoleAudio.Release;
            return;
        }
        if (root is ProgressBar progress)
        {
            progress.BackgroundStyleBoxOverride = Face(Ink, Muted, 1);
            if (previous == null)
                progress.ForegroundStyleBoxOverride = Face(Accent, Accent, 1);
        }
        if (root is LineEdit input)
        {
            input.StyleBoxOverride = Face(Ink, Muted, 5);
            input.Stylesheet = new Stylesheet(new StyleRule[]
            {
                Element<LineEdit>().Prop("font", Mono).Prop("font-color", Accent)
                    .Prop(LineEdit.StylePropertyCursorColor, Cream).Prop(LineEdit.StylePropertySelectionColor, Muted),
            });
        }
        if (root is Robust.Client.UserInterface.Controls.Button or CheckBox)
        {
            Switch((ContainerButton) root);
            return;
        }
        if (root is Label label)
        {
            label.FontOverride = Mono;
            var color = label.FontColorOverride;
            if (previous != null && color is { } old)
                color = Remap(old, previous);
            label.FontColorOverride = color == Color.FromHex("#00ff2a") ? Accent : color ?? Cream;
        }
        else if (root is PanelContainer panel && root is not WFInstrumentPanel && root is not WFScreenBezel && root is not WFGlassReadout &&
            panel.PanelOverride is null or WFConsoleFrameStyleBox)
            panel.PanelOverride = new WFConsoleFrameStyleBox(4);
        foreach (var child in root.Children.ToArray())
            Apply(child, previous);
    }
    private static Color Remap(Color color, WolfgateSkin previous)
    {
        if (color == previous.Accent) return Accent;
        if (color == previous.Text) return Cream;
        if (color == previous.TextMuted) return Muted;
        if (color == previous.Good) return Green;
        if (color == previous.Danger) return Red;
        if (color == previous.Ink) return Ink;
        return color;
    }
}

/// <summary>Draws the worn instrument housing with recessed edges and corner fasteners.</summary>
public sealed class WFInstrumentPanel : PanelContainer
{
    public WFInstrumentPanel() : this(12)
    {
    }

    /// <summary>Creates a housing whose content sits the given distance inside its fasteners.</summary>
    public WFInstrumentPanel(int padding)
    {
        PanelOverride = new WFConsoleFrameStyleBox(padding, housing: true);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (WFInstrumentTheme.Digital)
            return;
        var size = PixelSize;
        var inset = 5 * UIScale;
        var metal = WFInstrumentTheme.Skin.EdgeLight;
        var radius = 2.5f * UIScale;
        WFConsoleMetal.Screw(handle, new Vector2(inset, inset), radius, metal);
        WFConsoleMetal.Screw(handle, new Vector2(size.X - inset, inset), radius, metal);
        WFConsoleMetal.Screw(handle, new Vector2(inset, size.Y - inset), radius, metal);
        WFConsoleMetal.Screw(handle, new Vector2(size.X - inset, size.Y - inset), radius, metal);
    }
}

/// <summary>Frames a live radar with a recessed metal display bezel.</summary>
public sealed class WFScreenBezel : PanelContainer
{
    public WFScreenBezel()
    {
        PanelOverride = new WFConsoleFrameStyleBox(22, housing: true, bezel: true);
    }
}

/// <summary>Adds a smooth glass sheen and edge ticks without intercepting radar input.</summary>
public sealed class WFCrtGlass : Control
{
    public WFCrtGlass()
    {
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var size = PixelSize;
        WFInstrumentGlass.Window(handle, new UIBox2(Vector2.Zero, size), UIScale);
        if (WFInstrumentTheme.Digital)
        {
            var skin = WFInstrumentTheme.Skin;
            handle.DrawRect(new UIBox2(Vector2.Zero, size), skin.Accent.WithAlpha(0.004f));
            handle.DrawRect(new UIBox2(Vector2.Zero, size), skin.EdgeLight, false);
            return;
        }
        handle.DrawRect(new UIBox2(Vector2.Zero, size), WFInstrumentTheme.Accent.WithAlpha(0.004f));
        for (var x = 12; x < size.X - 12; x += 20)
            handle.DrawLine(new Vector2(x, 0), new Vector2(x, 4), WFInstrumentTheme.Accent.WithAlpha(0.4f));
        handle.DrawRect(new UIBox2(Vector2.Zero, size), WFInstrumentTheme.Accent.WithAlpha(0.3f), false);
    }
}
