using System.Linq;
using Content.Client._WF.Stylesheets;
using Content.Client.Stylesheets;
using Content.Shared._NF.Bank;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Loadouts;

/// <summary>
/// One group in the kit rail: the icon and name of what is picked there and a chip for anything the player
/// should know about the slot. Lit while its group is open.
/// </summary>
public sealed class LoadoutRailRow : ContainerButton
{
    public const string StyleClassRow = "LoadoutRailRow";
    public const float RowHeight = 28;

    private readonly LoadoutIconView _icon;
    private readonly Label _pick;
    private readonly Label _chip;

    public new readonly LoadoutGroupView Group;

    public LoadoutRailRow(LoadoutGroupView group, LoadoutIconCache icons)
    {
        Group = group;
        AddStyleClass(StyleClassRow);
        SetHeight = RowHeight;
        RectClipContent = true;

        _icon = new LoadoutIconView(icons, RowHeight, 1);

        // Wide enough for most group names, so the picks line up in a column; capped so the chip always fits.
        var label = new Label
        {
            Text = group.Label,
            MinWidth = 100,
            MaxWidth = 144,
            RectClipContent = true,
            VerticalAlignment = VAlignment.Center,
        };

        _pick = new Label
        {
            ClipText = true,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            StyleClasses = { StyleBase.StyleClassLabelSubText },
        };

        _chip = new Label
        {
            VerticalAlignment = VAlignment.Center,
            StyleClasses = { StyleWolfgate.StyleClassLoadoutChip },
        };

        AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            Children = { _icon, label, _pick, _chip },
        });
    }

    /// <summary>Updates the row from its group.</summary>
    /// <param name="hits">Search matches in the group while a search is on, else null.</param>
    public void Refresh(int? hits)
    {
        var picks = Group.Picks;

        _icon.SetPrototype(picks.FirstOrDefault(e => e.Icon != null)?.Icon);
        _pick.Text = picks.Count switch
        {
            0 => string.Empty,
            1 => picks[0].Name,
            _ => Loc.GetString("wf-loadout-row-more", ("name", picks[0].Name), ("count", picks.Count - 1)),
        };

        // The row cuts long names off, so the tooltip spells them out.
        ToolTip = string.Join('\n', picks.Select(e => e.Name).Prepend(Group.Label));
        Modulate = hits == 0 ? LoadoutTones.Dimmed : Color.White;
        _icon.Modulate = Modulate;

        // During a search the chip counts matches; rows without any are dimmed and say nothing.
        if (hits != null)
        {
            SetChip(hits == 0 ? string.Empty : Loc.GetString("wf-loadout-row-hits", ("count", hits.Value)), LoadoutTone.Normal);
            return;
        }

        if (Group.UnaffordableCount > 0)
            SetChip(Loc.GetString("wf-loadout-chip-unaffordable"), LoadoutTone.Danger);
        else if (Group.NeedsPick)
            SetChip(Loc.GetString("wf-loadout-chip-pick"), LoadoutTone.Danger);
        else if (Group.Stale > 0)
        {
            SetChip(Loc.GetString("wf-loadout-chip-stale"), LoadoutTone.Caution);
            ToolTip = Loc.GetString("wf-loadout-chip-stale-tooltip");
        }
        else if (Group.AllLocked)
            SetChip(Loc.GetString("wf-loadout-chip-locked"), LoadoutTone.Muted);
        else if (Group.Fixed)
            SetChip(Loc.GetString("wf-loadout-chip-fixed"), LoadoutTone.Muted);
        else if (!Group.Single)
            SetChip(Loc.GetString("wf-loadout-chip-count", ("count", picks.Count), ("max", Group.Max)), LoadoutTone.Muted);
        else if (picks.Count == 0)
            SetChip(Loc.GetString("wf-loadout-row-none"), LoadoutTone.Muted);
        else if (picks[0].Price > 0)
            SetChip(BankSystemExtensions.ToSpesoString(picks[0].Price), LoadoutTone.Normal);
        else
            SetChip(string.Empty, LoadoutTone.Normal);
    }

    private void SetChip(string text, LoadoutTone tone)
    {
        _chip.Text = text;
        _chip.Visible = text.Length > 0;
        LoadoutTones.Set(_chip, tone);
    }
}
