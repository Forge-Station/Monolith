using System.Linq;
using Content.Client._WF.Stylesheets;
using Content.Shared._WF.CCVar;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;

namespace Content.Client._WF.Branding;

/// <summary>
/// Brands a window's title bar: the logo's lettering and six-bar mark beside the header buttons, and the logo's
/// spectrum as a thin rule under the header. Works on FancyWindow, DefaultWindow and custom headers alike, since it
/// finds the header from the close button.
/// </summary>
public static class WFWindowBranding
{
    public const string BadgeName = "WFBrandBadge";
    public const string RuleName = "WFBrandRule";

    private const string LowDividerClass = "LowDivider";
    // Pre-shrunk from logo.png by generate_icons.py, drawn at half its pixel height.
    private const string WordmarkPath = "/Textures/_WF/Branding/Logo/wordmark.png";
    private const float WordmarkHeight = 9f;
    private const int SearchDepth = 6;
    private const float BadgeAlpha = 0.85f;
    private const float RuleAlpha = 0.7f;

    // The badge gives way before it can crowd the title.
    private const float BadgeWidth = 112f;
    private const float ButtonsWidth = 72f;
    private const float TitleCharWidth = 10f;
    private const float NameOnlyWidth = 26f;

    /// <summary>Adds the badge and rule once; later calls do nothing.</summary>
    public static void Apply(BaseWindow window)
    {
        if (FindCloseButton(window) is not { Parent: { } row } || row.Children.Any(c => c.Name == BadgeName))
            return;

        var badge = BuildBadge();
        var firstButton = row.Children.FirstOrDefault(c => c is TextureButton);
        row.AddChild(badge);
        if (firstButton != null)
            badge.SetPositionInParent(firstButton.GetPositionInParent());

        AddRule(row);

        var title = row.Children.OfType<Label>().FirstOrDefault();
        var name = badge.Children.OfType<TextureRect>().First();
        void Fit()
        {
            var titleWidth = (title?.Text?.Length ?? 0) * TitleCharWidth;
            var free = row.Width - titleWidth - ButtonsWidth;
            badge.Visible = free >= NameOnlyWidth;
            name.Visible = free >= BadgeWidth;
        }

        row.OnResized += Fit;
        Fit();
    }

    private static Control BuildBadge()
    {
        var wordmark = IoCManager.Resolve<IResourceCache>().GetResource<TextureResource>(WordmarkPath).Texture;
        var skin = WolfgateSkins.Get(IoCManager.Resolve<IConfigurationManager>().GetCVar(WolfgateCVars.UiStyle));

        var badge = new BoxContainer
        {
            Name = BadgeName,
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            VerticalAlignment = Control.VAlignment.Center,
            SeparationOverride = 6,
            Margin = new Thickness(8, 0, 8, 0),
            MouseFilter = Control.MouseFilterMode.Ignore,
        };

        // Lettering then bars, as on the logo.
        badge.AddChild(new TextureRect
        {
            Texture = wordmark,
            Stretch = TextureRect.StretchMode.Scale,
            SetSize = new(MathF.Round(wordmark.Width * WordmarkHeight / wordmark.Height), WordmarkHeight),
            ModulateSelfOverride = skin.TextMuted.WithAlpha(BadgeAlpha),
            VerticalAlignment = Control.VAlignment.Center,
            MouseFilter = Control.MouseFilterMode.Ignore,
        });
        badge.AddChild(new WFBrandBars
        {
            SetSize = new(17, 12),
            Alpha = BadgeAlpha,
            VerticalAlignment = Control.VAlignment.Center,
        });
        return badge;
    }

    /// <summary>Puts the spectrum rule under the header, standing in for a FancyWindow's plain divider.</summary>
    private static void AddRule(Control row)
    {
        // Climb to the header's slot in the window's vertical stack.
        Control header = row;
        while (header.Parent is { } parent
               && !(parent is BoxContainer { Orientation: BoxContainer.LayoutOrientation.Vertical }))
        {
            header = parent;
            if (header is BaseWindow)
                return;
        }

        if (header.Parent is not { } stack)
            return;

        var index = header.GetPositionInParent();
        if (index + 1 < stack.ChildCount
            && stack.GetChild(index + 1) is PanelContainer divider
            && divider.HasStyleClass(LowDividerClass))
        {
            divider.Visible = false;
        }

        var rule = new WFBrandBars
        {
            Name = RuleName,
            Rule = true,
            Alpha = RuleAlpha,
            SetHeight = 2,
            HorizontalExpand = true,
        };
        stack.AddChild(rule);
        rule.SetPositionInParent(index + 1);
    }

    private static TextureButton? FindCloseButton(Control root)
    {
        var queue = new Queue<(Control Control, int Depth)>();
        queue.Enqueue((root, 0));
        while (queue.TryDequeue(out var entry))
        {
            if (entry.Control is TextureButton button && button.HasStyleClass(DefaultWindow.StyleClassWindowCloseButton))
                return button;

            if (entry.Depth >= SearchDepth)
                continue;

            foreach (var child in entry.Control.Children)
                queue.Enqueue((child, entry.Depth + 1));
        }

        return null;
    }
}
