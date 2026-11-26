using System.Linq;
using Content.Client._WF.Gags;
using Content.Client._WF.Stylesheets;
using Content.Client.Stylesheets;
using Content.Shared._NF.Bank;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WF.Loadouts;

/// <summary>
/// Column to the right of the tile grid. It lists what the open group holds, one card per pick with its price, a
/// button to drop it, why it can't be paid for and, for a recolourable item, its colours. Only picking changes it.
/// </summary>
public sealed class LoadoutSelectedPanel : PanelContainer
{
    public const float PanelWidth = 260;

    private readonly LoadoutIconCache _icons;
    private readonly Label _heading;
    private readonly Label _message;
    private readonly BoxContainer _cards;
    private readonly ScrollContainer _scroll;
    private readonly List<LoadoutIconView> _cardIcons = new();
    private readonly Dictionary<LoadoutEntry, WFGagsColorRow> _rows = new();
    private string? _shown;

    /// <summary>Raised when a pick's remove button is pressed.</summary>
    public event Action<LoadoutEntry>? OnRemove;

    /// <summary>Raised when the colours of a recolourable pick change; null resets them to the item's own.</summary>
    public event Action<LoadoutEntry, List<Color>?>? OnColorsChanged;

    /// <summary>A pick's colours and its item's defaults, or null when its item isn't recolourable.</summary>
    public Func<LoadoutEntry, (List<Color> Current, List<Color> Defaults)?>? ColorsOf;

    public LoadoutSelectedPanel(LoadoutIconCache icons)
    {
        _icons = icons;
        AddStyleClass(StyleWolfgate.StyleClassLoadoutPanel);
        SetWidth = PanelWidth;
        VerticalExpand = true;

        _heading = new Label { StyleClasses = { StyleWolfgate.StyleClassCreatorHeading } };
        _message = new Label
        {
            Align = Label.AlignMode.Center,
            StyleClasses = { StyleBase.StyleClassLabelSubText },
        };
        _cards = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            HorizontalExpand = true,
        };
        _scroll = new ScrollContainer
        {
            HScrollEnabled = false,
            VerticalExpand = true,
            Children = { _cards },
        };

        AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Children = { _heading, _message, _scroll },
        });
    }

    /// <summary>Lists what a group holds, or a plain message when there is no group or nothing is picked.</summary>
    /// <param name="canRemove">Whether a pick may be dropped; a required pick can only be replaced.</param>
    public void ShowPicks(LoadoutGroupView? group, string? message, Func<LoadoutEntry, bool> canRemove)
    {
        _heading.Text = Loc.GetString("wf-loadout-selected-heading");

        var picks = group?.Picks.Count ?? 0;
        _message.Visible = picks == 0;
        if (picks == 0)
        {
            _message.Text = message ?? Loc.GetString("wf-loadout-detail-none");
            _shown = string.Empty;
            ClearCards();
            return;
        }

        // Called on every refresh; the cards are only remade when what they show changed, so a colour being
        // picked keeps its popup.
        var shown = string.Join(' ', group!.Picks.Select(p => $"{p.Id}:{canRemove(p)}:{p.Unaffordable}").Prepend(group.Id.Id));
        if (shown != _shown)
        {
            _shown = shown;
            ClearCards();
            foreach (var pick in group.Picks)
            {
                _cards.AddChild(Card(pick, canRemove(pick)));
            }
        }

        foreach (var (pick, row) in _rows)
        {
            if (ColorsOf?.Invoke(pick) is { } colors)
                row.Set(pick.Id.Id, colors.Current, colors.Defaults);
        }
    }

    /// <summary>One pick: icon, name, price and status, a remove button, why it is locked or can't be paid for, and its colours.</summary>
    private Control Card(LoadoutEntry entry, bool removable)
    {
        var icon = new LoadoutIconView(_icons, 48, 1) { VerticalAlignment = VAlignment.Top };
        icon.SetPrototype(entry.Icon);
        _cardIcons.Add(icon);

        var name = new RichTextLabel { HorizontalExpand = true };
        name.SetMessage(FormattedMessage.FromUnformatted(entry.Name));

        var chips = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        chips.AddChild(new Label
        {
            Text = entry.Price > 0 ? BankSystemExtensions.ToSpesoString(entry.Price) : Loc.GetString("wf-loadout-free"),
            StyleClasses = { StyleWolfgate.StyleClassLoadoutChip },
        });

        var reason = new FormattedMessage();
        if (entry.Locked)
        {
            chips.AddChild(Status(Loc.GetString("wf-loadout-detail-locked"), LoadoutTone.Danger));
            if (entry.Reason != null)
                reason.AddMessage(entry.Reason);
        }
        else if (entry.Unaffordable)
        {
            chips.AddChild(Status(Loc.GetString("wf-loadout-detail-unaffordable"), LoadoutTone.Danger));
            reason.AddText(entry.Group.Replacement is { } replacement
                ? Loc.GetString("wf-loadout-dropped-fallback", ("item", replacement))
                : Loc.GetString("wf-loadout-dropped"));
        }

        var top = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            Children =
            {
                icon,
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    HorizontalExpand = true,
                    Children = { name, chips },
                },
            },
        };

        if (removable)
        {
            var remove = new Button
            {
                Text = Loc.GetString("wf-loadout-detail-remove-glyph"),
                ToolTip = Loc.GetString("wf-loadout-detail-remove", ("name", entry.Name)),
                VerticalAlignment = VAlignment.Top,
            };
            remove.OnPressed += _ => OnRemove?.Invoke(entry);
            top.AddChild(remove);
        }

        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Children = { top },
        };

        if (!reason.IsEmpty)
        {
            var text = new RichTextLabel { HorizontalExpand = true };
            text.SetMessage(reason);
            body.AddChild(text);
        }

        if (ColorsOf?.Invoke(entry) is { } colors)
        {
            var row = new WFGagsColorRow();
            row.Set(entry.Id.Id, colors.Current, colors.Defaults);
            row.OnColorsChanged += picked => OnColorsChanged?.Invoke(entry, picked);
            _rows[entry] = row;
            body.AddChild(row);
        }

        return new PanelContainer
        {
            HorizontalExpand = true,
            MouseFilter = MouseFilterMode.Pass,
            ToolTip = entry.Name,
            StyleClasses = { StyleWolfgate.StyleClassLoadoutPick },
            Children = { body },
        };
    }

    private static Label Status(string text, LoadoutTone tone)
    {
        var label = new Label { Text = text, StyleClasses = { StyleWolfgate.StyleClassLoadoutChip } };
        LoadoutTones.Set(label, tone);
        return label;
    }

    private void ClearCards()
    {
        foreach (var icon in _cardIcons)
        {
            icon.SetPrototype(null);
        }

        _cardIcons.Clear();
        _rows.Clear();
        _cards.RemoveAllChildren();
    }
}
