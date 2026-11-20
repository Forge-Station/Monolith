using System.Linq;
using System.Numerics;
using Content.Shared._NF.Bank;
using Content.Shared._WF.Augments;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Augments.UI;

/// <summary>The creator's Augments tab: an augment and a style per body slot, with what the priced ones cost.</summary>
public sealed partial class WFAugmentEditor : BoxContainer
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IComponentFactory _factory = default!;

    /// <summary>Raised with the new choices when the player changes one.</summary>
    public event Action<Dictionary<WFAugmentSlot, WFAugmentChoice>>? OnAugmentsChanged;

    private readonly BoxContainer _rows;
    private readonly Label _due;
    private Dictionary<WFAugmentSlot, WFAugmentChoice> _choices = new();

    public WFAugmentEditor()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 6;

        AddChild(new RichTextLabel { Text = Loc.GetString("wf-augments-intro"), Margin = new Thickness(0, 0, 0, 6) });
        _rows = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4 };
        AddChild(_rows);
        _due = new Label { Margin = new Thickness(0, 8, 0, 0) };
        AddChild(_due);
    }

    /// <summary>Rebuilds the rows for a profile: the slots its species has, and its choices.</summary>
    public void SetProfile(HumanoidCharacterProfile profile)
    {
        _choices = new Dictionary<WFAugmentSlot, WFAugmentChoice>(profile.Augments);
        _rows.RemoveAllChildren();

        var claimed = WFAugmentRules.ClaimedSlots(profile.TraitPreferences, _proto);
        foreach (WFAugmentSlot slot in Enum.GetValues(typeof(WFAugmentSlot)))
        {
            var augments = WFAugmentRules.AugmentsFor(profile.Species, slot, _proto, _factory);
            if (augments.Count == 0)
                continue;

            _rows.AddChild(claimed.TryGetValue(slot, out var trait)
                ? MakeClaimedRow(slot, trait)
                : MakeRow(slot, augments, profile));
        }

        var owed = WFAugmentRules.Owed(profile, _proto);
        _due.Visible = owed > 0;
        _due.Text = Loc.GetString(owed > profile.BankBalance ? "wf-augments-due-short" : "wf-augments-due",
            ("due", BankSystemExtensions.ToSpesoString(owed)),
            ("balance", BankSystemExtensions.ToSpesoString(profile.BankBalance)));
        _due.FontColorOverride = owed > profile.BankBalance ? Color.FromHex("#E0605A") : null;
    }

    /// <summary>A slot a trait puts its own limb in: named, not pickable.</summary>
    private Control MakeClaimedRow(WFAugmentSlot slot, ProtoId<TraitPrototype> trait)
    {
        var name = _proto.TryIndex(trait, out var traitProto) ? Loc.GetString(traitProto.Name) : trait.Id;
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 6 };
        row.AddChild(new Label
        {
            Text = Loc.GetString("wf-augment-slot-" + SlotKey(slot)),
            MinSize = new Vector2(90, 0),
            VAlign = Label.VAlignMode.Center,
        });
        row.AddChild(new Label
        {
            Text = Loc.GetString("wf-augments-claimed", ("trait", name)),
            HorizontalExpand = true,
            FontColorOverride = Color.FromHex("#A0A0A8"),
        });
        return row;
    }

    private Control MakeRow(WFAugmentSlot slot, List<WFAugmentPrototype> augments, HumanoidCharacterProfile profile)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 6 };
        row.AddChild(new Label
        {
            Text = Loc.GetString("wf-augment-slot-" + SlotKey(slot)),
            MinSize = new Vector2(90, 0),
            VAlign = Label.VAlignMode.Center,
        });

        _choices.TryGetValue(slot, out var current);
        var picked = augments.FirstOrDefault(a => a.ID == current.Augment.Id);

        var augmentButton = new OptionButton { HorizontalExpand = true, MinSize = new Vector2(180, 0) };
        augmentButton.AddItem(Loc.GetString("wf-augments-none"), 0);
        for (var i = 0; i < augments.Count; i++)
        {
            augmentButton.AddItem(Loc.GetString(augments[i].Name), i + 1);
            if (augments[i] == picked)
                augmentButton.SelectId(i + 1);
        }
        augmentButton.ToolTip = picked != null ? Loc.GetString(picked.Description) : null;
        augmentButton.OnItemSelected += args =>
        {
            if (args.Id == 0)
                _choices.Remove(slot);
            else
                _choices[slot] = new WFAugmentChoice(augments[args.Id - 1].ID, current.Style);
            OnAugmentsChanged?.Invoke(new Dictionary<WFAugmentSlot, WFAugmentChoice>(_choices));
        };
        row.AddChild(augmentButton);

        if (picked is { Styled: true })
        {
            var styles = WFAugmentRules.StylesFor(slot, _proto);
            var styleButton = new OptionButton { HorizontalExpand = true, MinSize = new Vector2(180, 0) };
            styleButton.ToolTip = Loc.GetString("wf-augments-style");
            for (var i = 0; i < styles.Count; i++)
            {
                styleButton.AddItem(Loc.GetString(styles[i].Name), i);
                if (styles[i].ID == current.Style?.Id)
                    styleButton.SelectId(i);
            }
            styleButton.OnItemSelected += args =>
            {
                _choices[slot] = new WFAugmentChoice(picked.ID, styles[args.Id].ID);
                OnAugmentsChanged?.Invoke(new Dictionary<WFAugmentSlot, WFAugmentChoice>(_choices));
            };
            row.AddChild(styleButton);
        }

        row.AddChild(new Label
        {
            Text = PriceText(picked, profile),
            MinSize = new Vector2(100, 0),
            Align = Label.AlignMode.Right,
            VAlign = Label.VAlignMode.Center,
            FontColorOverride = Color.FromHex("#A0A0A8"),
        });
        return row;
    }

    private static string PriceText(WFAugmentPrototype? augment, HumanoidCharacterProfile profile)
    {
        if (augment == null)
            return string.Empty;
        if (augment.Price <= 0)
            return Loc.GetString("wf-augments-free");
        if (profile.AugmentUnlocks.Contains(augment.ID))
            return Loc.GetString("wf-augments-unlocked");
        return Loc.GetString("wf-augments-price", ("price", BankSystemExtensions.ToSpesoString(augment.Price)));
    }

    private static string SlotKey(WFAugmentSlot slot) => slot switch
    {
        WFAugmentSlot.LeftArm => "left-arm",
        WFAugmentSlot.RightArm => "right-arm",
        WFAugmentSlot.LeftLeg => "left-leg",
        WFAugmentSlot.RightLeg => "right-leg",
        _ => slot.ToString().ToLowerInvariant(),
    };
}
