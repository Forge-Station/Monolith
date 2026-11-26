using System.Numerics;
using Content.Client._WF.Stylesheets;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WF.Humanoid;

/// <summary>
/// One species in the species tab: a small full-body preview, the name, a few key facts, and a button to
/// its guidebook page. Fixed size, so a grid of them lines up whatever the summary length.
/// </summary>
public sealed class WolfgateSpeciesCard : ContainerButton
{
    public const float CardWidth = 296f;
    public const float CardHeight = 124f;

    private const float PreviewSize = 72f;
    private const float Padding = 12f;
    private const int Gap = 8;

    /// <summary>Raised when the info button is clicked.</summary>
    public Action? OnInfoRequested;

    public WolfgateSpeciesCard(string species, string title, string facts, Control preview, bool hasGuide)
    {
        AddStyleClass(WolfgateMarkingTile.StyleClassTile);
        ToggleMode = true;
        SetSize = new Vector2(CardWidth, CardHeight);
        // A summary longer than the card would otherwise bleed over the card below it.
        RectClipContent = true;

        var name = new Label
        {
            Text = title,
            ClipText = true,
            HorizontalExpand = true,
            StyleClasses = { StyleWolfgate.StyleClassCreatorCardTitle },
        };

        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            HorizontalExpand = true,
            Children = { name },
        };

        // A species with no guidebook entry gets no button rather than one that lands on the generic page.
        if (hasGuide)
        {
            var info = new TextureButton
            {
                StyleClasses = { "SpeciesInfoDefault" },
                Scale = new Vector2(0.3f, 0.3f),
                VerticalAlignment = VAlignment.Center,
                ToolTip = Loc.GetString("humanoid-profile-editor-guidebook-button-tooltip"),
            };
            // The button stops the mouse itself, so clicking it does not also toggle the card.
            info.OnPressed += _ => OnInfoRequested?.Invoke();
            header.AddChild(info);
        }

        var summary = new RichTextLabel { HorizontalExpand = true };
        summary.SetMessage(FormattedMessage.FromMarkupPermissive(facts));

        var text = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
            VerticalAlignment = VAlignment.Center,
            // RichTextLabel only wraps inside a bounded width, so the column is measured, not expanded.
            SetWidth = CardWidth - Padding - PreviewSize - Gap,
            Children = { header, summary },
        };

        AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = Gap,
            Margin = new Thickness(6, 4),
            Children = { preview, text },
        });
    }

    /// <summary>Box the preview sprite is drawn into. Sprites are fitted, so nothing clips.</summary>
    public static Vector2 PreviewBox => new(PreviewSize, PreviewSize);
}
