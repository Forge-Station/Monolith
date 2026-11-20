using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.Cockpit;

public sealed partial class WFCockpitView
{
    /// <summary>The Wolfgate wordmark and the exit control in one slim row at the top of the left column.</summary>
    private static Control BuildBrand(Action exit)
    {
        var texture = IoCManager.Resolve<IResourceCache>()
            .GetResource<TextureResource>("/Textures/_WF/Branding/Logo/logo.png").Texture;
        var logo = new TextureRect
        {
            Name = "CockpitLogo",
            // The source wordmark has transparent margins intended for the full-screen splash.
            Texture = new AtlasTexture(texture, new UIBox2(48, 67, 754, 199)),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            CanShrink = true,
            HorizontalExpand = true,
            SetHeight = 32,
            MouseFilter = Control.MouseFilterMode.Pass,
            ToolTip = Loc.GetString("wf-cockpit-title"),
        };
        var exitButton = Button("wf-cockpit-exit");
        exitButton.Name = "CockpitExit";
        exitButton.HorizontalExpand = false;
        exitButton.AddStyleClass("WfHeader");
        Switch(exitButton);
        // Console buttons clip their text, so a button no wider than its label would measure 32 px.
        exitButton.SetWidth = 132;
        exitButton.OnPressed += _ => exit();
        var brand = Row(logo, exitButton);
        brand.Name = "CockpitBrand";
        return brand;
    }
}
