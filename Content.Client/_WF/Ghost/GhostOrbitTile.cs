using System.Numerics;
using Content.Client._WF.Stylesheets;
using Content.Client.Stylesheets;
using Content.Shared._WF.Ghost;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Ghost;

/// <summary>
/// One orbit target: category stripe, icon, health-coloured name, detail line, follower count and health bar.
/// </summary>
public sealed class GhostOrbitTile : ContainerButton
{
    public const float TileWidth = 236;

    public readonly GhostOrbitTarget Target;

    /// <summary>Lower-case name and detail, matched against the search text.</summary>
    public readonly string SearchText;

    public GhostOrbitTile(GhostOrbitTarget target, Texture? icon, Color accent, WolfgateSkin skin)
    {
        Target = target;
        AddStyleClass(StyleClassButton);
        SetWidth = TileWidth;
        MinHeight = 44;

        var detail = BuildDetail(target);
        SearchText = $"{target.Name} {detail}".ToLowerInvariant();
        ToolTip = BuildTooltip(target, detail);

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
        };
        AddChild(row);

        row.AddChild(new PanelContainer
        {
            SetWidth = 3,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat(accent),
        });

        row.AddChild(new TextureRect
        {
            Texture = icon,
            SetSize = new Vector2(32, 32),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            VerticalAlignment = VAlignment.Center,
        });

        var text = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(0, 2, 4, 2),
        };
        row.AddChild(text);

        var top = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        text.AddChild(top);

        top.AddChild(new Label
        {
            Text = target.Name,
            ClipText = true,
            HorizontalExpand = true,
            FontColorOverride = NameColor(target, skin),
        });

        if (target.Followers > 0)
        {
            top.AddChild(new Label
            {
                Text = Loc.GetString("wf-ghost-orbit-followers-badge", ("count", target.Followers)),
                StyleClasses = { StyleNano.StyleClassLabelSmall },
                FontColorOverride = skin.Accent,
                VerticalAlignment = VAlignment.Center,
            });
        }

        if (detail.Length > 0)
        {
            text.AddChild(new Label
            {
                Text = detail,
                ClipText = true,
                StyleClasses = { StyleNano.StyleClassLabelSmall },
                FontColorOverride = skin.TextMuted,
            });
        }

        if (target.Damage is { } damage)
        {
            text.AddChild(new ProgressBar
            {
                MinValue = 0,
                MaxValue = 1,
                Value = 1 - damage,
                SetHeight = 3,
                Margin = new Thickness(0, 2, 0, 0),
                BackgroundStyleBoxOverride = new StyleBoxFlat(skin.EdgeSoft),
                ForegroundStyleBoxOverride = new StyleBoxFlat(HealthColor(damage, skin)),
            });
        }
    }

    private static string BuildDetail(GhostOrbitTarget target)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrEmpty(target.Detail))
            parts.Add(target.Detail);
        if (target.Disconnected)
            parts.Add(Loc.GetString("wf-ghost-orbit-tag-ssd"));
        if (target.AdminOnly)
            parts.Add(Loc.GetString("wf-ghost-orbit-tag-admin"));

        return string.Join(" · ", parts);
    }

    private static string BuildTooltip(GhostOrbitTarget target, string detail)
    {
        var tip = detail.Length > 0 ? $"{target.Name}\n{detail}" : target.Name;
        if (target.Damage is { } damage)
            tip += "\n" + Loc.GetString("wf-ghost-orbit-tooltip-health", ("percent", (int) MathF.Round((1 - damage) * 100)));
        if (target.Followers > 0)
            tip += "\n" + Loc.GetString("wf-ghost-orbit-tooltip-followers", ("count", target.Followers));

        return tip;
    }

    /// <summary>
    /// Plain text when healthy, sliding to caution then danger as damage builds; dead and SSD read muted.
    /// </summary>
    private static Color NameColor(GhostOrbitTarget target, WolfgateSkin skin)
    {
        if (target.Category == GhostOrbitCategory.Dead)
            return skin.TextMuted;
        if (target.Category == GhostOrbitCategory.Critical)
            return skin.Danger;

        var color = target.Damage is { } damage and > 0.05f ? HealthColor(damage, skin) : skin.Text;
        return target.Disconnected ? color.WithAlpha(0.6f) : color;
    }

    private static Color HealthColor(float damage, WolfgateSkin skin)
    {
        return damage < 0.5f
            ? Color.InterpolateBetween(skin.Good, skin.Caution, damage * 2)
            : Color.InterpolateBetween(skin.Caution, skin.Danger, (damage - 0.5f) * 2);
    }
}
