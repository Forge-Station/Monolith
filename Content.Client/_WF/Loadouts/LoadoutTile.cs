using Content.Client._WF.Gags;
using Content.Client._WF.Stylesheets;
using Content.Client.Stylesheets;
using Content.Shared._NF.Bank;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WF.Loadouts;

/// <summary>
/// Pooled grid tile for one loadout: sprite, a name of up to two lines, the price when it has one, a badge
/// when it is locked and a palette when its item can be recoloured. Lit while the loadout is selected. Also draws the "Nothing" tile of an optional group.
/// </summary>
public sealed class LoadoutTile : ContainerButton
{
    public const string StyleClassTile = "LoadoutTile";
    public const float TileWidth = 120;
    public const float TileHeight = 120;

    /// <summary>Height of a tile that also names its group, as search results do.</summary>
    public const float CaptionedHeight = 142;

    private const float IconSize = 64;
    // Two lines of the default font: 23 for the first, 22 for the next.
    private const float NameHeight = 46;

    private readonly LoadoutIconView _icon;
    private readonly RichTextLabel _name;
    private readonly Label _caption;
    private readonly Label _price;
    private readonly Label _badge;
    private readonly TextureRect _palette;
    private string? _nameText;

    /// <summary>Loadout this tile shows, null for the "Nothing" tile and for a tile with no row to show.</summary>
    public LoadoutEntry? Entry { get; private set; }

    /// <summary>Group this tile clears when clicked, set on the "Nothing" tile only.</summary>
    public LoadoutGroupView? NothingFor { get; private set; }

    public LoadoutTile(LoadoutIconCache icons)
    {
        AddStyleClass(StyleClassTile);
        RectClipContent = true;

        _icon = new LoadoutIconView(icons, IconSize, 2) { HorizontalAlignment = HAlignment.Center };

        // Short names sit centred under the sprite; a wrapped one fills the width.
        _name = new RichTextLabel
        {
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Top,
        };

        _caption = new Label
        {
            ClipText = true,
            Align = Label.AlignMode.Center,
            StyleClasses = { StyleWolfgate.StyleClassLoadoutChip, StyleBase.StyleClassLabelSubText },
        };

        _price = new Label
        {
            HorizontalAlignment = HAlignment.Right,
            VerticalAlignment = VAlignment.Top,
            StyleClasses = { StyleWolfgate.StyleClassLoadoutChip },
        };

        _badge = new Label
        {
            Text = Loc.GetString("wf-loadout-chip-locked"),
            HorizontalAlignment = HAlignment.Left,
            VerticalAlignment = VAlignment.Top,
            StyleClasses = { StyleWolfgate.StyleClassLoadoutChip, StyleBase.StyleClassLabelSubText },
        };

        AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
            Children =
            {
                _icon,
                // A name longer than two lines is cut off here; the selected panel shows it whole.
                new Control { SetHeight = NameHeight, RectClipContent = true, Children = { _name } },
                _caption,
            },
        });
        _palette = new TextureRect
        {
            Texture = WFGagsColorRow.PaletteIcon(),
            TextureScale = new System.Numerics.Vector2(2, 2),
            HorizontalAlignment = HAlignment.Right,
            VerticalAlignment = VAlignment.Top,
            // Under the price, clear of the sprite.
            Margin = new Thickness(0, 22, 2, 0),
            ToolTip = Loc.GetString("wf-gags-colorable"),
            MouseFilter = MouseFilterMode.Pass,
        };

        AddChild(_price);
        AddChild(_badge);
        AddChild(_palette);
    }

    /// <summary>Takes the tile out of the grid: a pooled tile past the end of the list.</summary>
    public void Unbind()
    {
        Entry = null;
        NothingFor = null;
        Visible = false;
        _icon.SetPrototype(null);
    }

    /// <summary>Shows the "Nothing" choice of an optional single-pick group.</summary>
    public void BindNothing(LoadoutGroupView group)
    {
        Entry = null;
        NothingFor = group;
        Visible = true;
        Disabled = false;
        Pressed = group.Picks.Count == 0;
        Modulate = Color.White;
        _icon.Modulate = Color.White;

        _icon.SetPrototype(null);
        SetName(Loc.GetString("wf-loadout-nothing"));
        _caption.Visible = false;
        _price.Visible = false;
        _badge.Visible = false;
        _palette.Visible = false;
        ToolTip = null;
    }

    /// <summary>Shows a loadout.</summary>
    /// <param name="caption">Group name under the tile, for lists that mix groups.</param>
    /// <param name="dimmed">The group is full and this is not one of its picks.</param>
    public void Bind(LoadoutEntry entry, string? caption, bool dimmed)
    {
        Entry = entry;
        NothingFor = null;
        Visible = true;
        // A locked pick that is somehow held stays lit and clickable, so it can be dropped.
        Disabled = entry.Locked && !entry.Selected;
        Pressed = entry.Selected;
        Modulate = dimmed ? LoadoutTones.Dimmed : Color.White;

        // A sprite view draws through the world handle, which the control's modulate doesn't reach.
        _icon.Modulate = Modulate;

        _icon.SetPrototype(entry.Icon);
        SetName(entry.Name);

        _caption.Visible = caption != null;
        _caption.Text = caption;

        _price.Visible = entry.Price > 0;
        if (entry.Price > 0)
        {
            _price.Text = BankSystemExtensions.ToSpesoString(entry.Price);
            LoadoutTones.Set(_price, entry.Unaffordable ? LoadoutTone.Danger : LoadoutTone.Normal);
        }

        _badge.Visible = entry.Locked;
        _palette.Visible = entry.Colorable;

        // The selected panel only shows picks, so the whole name and why a tile is locked are on hover here.
        ToolTip = entry.Locked && entry.Reason != null ? $"{entry.Name}\n{entry.Reason}" : entry.Name;
    }

    private void SetName(string text)
    {
        if (_nameText == text)
            return;

        _nameText = text;
        _name.SetMessage(FormattedMessage.FromUnformatted(text));
    }
}
