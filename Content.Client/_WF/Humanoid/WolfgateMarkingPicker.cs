using System.Linq;
using System.Numerics;
using Content.Client._WF.Stylesheets;
using Content.Client._WF.UserInterface.Controls;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.Utility;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WF.Humanoid;

/// <summary>
/// Grid marking picker: one toggle per body part the species can decorate, a tile grid where applied markings are
/// lit and toggle on and off, and a card per applied marking with rank and colour controls. Markings are grouped by
/// their body part rather than their points category, so left and right limbs, snout, tail and so on each get their
/// own view while points are still tracked per category. Same surface as MarkingPicker.
/// </summary>
public sealed class WolfgateMarkingPicker : BoxContainer
{
    [Dependency] private MarkingManager _markingManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    public Action<MarkingSet>? OnMarkingAdded;
    public Action<MarkingSet>? OnMarkingRemoved;
    public Action<MarkingSet>? OnMarkingColorChange;
    public Action<MarkingSet>? OnMarkingRankChange;

    public Color CurrentSkinColor = Color.White;
    public Color CurrentEyeColor = Color.Black;
    public Marking? HairMarking;
    public Marking? FacialHairMarking;
    public bool Forced { get; set; }

    /// <summary>Body parts in the order they are offered, head to tail; anything else follows in enum order.</summary>
    private static readonly HumanoidVisualLayers[] PartOrder =
    {
        HumanoidVisualLayers.Head, HumanoidVisualLayers.HeadTop, HumanoidVisualLayers.HeadSide, HumanoidVisualLayers.Snout,
        HumanoidVisualLayers.Eyes, HumanoidVisualLayers.Chest,
        HumanoidVisualLayers.LArm, HumanoidVisualLayers.RArm, HumanoidVisualLayers.LHand, HumanoidVisualLayers.RHand,
        HumanoidVisualLayers.LLeg, HumanoidVisualLayers.RLeg, HumanoidVisualLayers.LFoot, HumanoidVisualLayers.RFoot,
        HumanoidVisualLayers.Tail, HumanoidVisualLayers.Wings,
    };

    private readonly GridContainer _parts;
    private readonly Label _points;
    private readonly LineEdit _search;
    private readonly GridContainer _grid;
    private readonly Label _appliedHeading;
    private readonly BoxContainer _applied;
    private readonly PanelContainer _appliedCard;
    private readonly Button _clearAll;
    private readonly BoxContainer _clearConfirm;
    private readonly ButtonGroup _partGroup = new();
    private readonly Dictionary<HumanoidVisualLayers, Button> _partButtons = new();
    private readonly HashSet<MarkingCategories> _ignoreCategories = new();
    private readonly List<MarkingCategories> _allCategories = Enum.GetValues<MarkingCategories>().ToList();
    private IReadOnlySet<string> _hiddenMarkings = new HashSet<string>();

    private MarkingSet _current = new();
    private HumanoidVisualLayers _part = HumanoidVisualLayers.Chest;
    private string _species = SharedHumanoidAppearanceSystem.DefaultSpecies;
    private Sex _sex = Sex.Unsexed;
    private bool _ignoreSpecies;
    private Direction _direction = Direction.South;
    private readonly List<WolfgateMarkingIcon> _appliedIcons = new();

    /// <summary>Direction the tile and card sprites face; follows the preview pawn.</summary>
    public Direction PreviewDirection
    {
        get => _direction;
        set
        {
            _direction = value;
            foreach (var child in _grid.Children)
            {
                if (child is WolfgateMarkingTile tile)
                    tile.SetDirection(value);
            }
            foreach (var icon in _appliedIcons)
                icon.SetDirection(value);
        }
    }

    public string IgnoreCategories
    {
        get => string.Join(',', _ignoreCategories);
        set
        {
            _ignoreCategories.Clear();
            foreach (var category in value.Split(','))
            {
                if (Enum.TryParse(category, out MarkingCategories parsed))
                    _ignoreCategories.Add(parsed);
            }
            SetupPartButtons();
        }
    }

    public bool IgnoreSpecies
    {
        get => _ignoreSpecies;
        set
        {
            _ignoreSpecies = value;
            Populate(_search.Text);
        }
    }

    /// <summary>Marking ids never offered as tiles, e.g. the adult-only markings while the character is below the adult age.</summary>
    public IReadOnlySet<string> HiddenMarkings
    {
        get => _hiddenMarkings;
        set
        {
            if (_hiddenMarkings.SetEquals(value))
                return;

            _hiddenMarkings = value;
            Populate(_search.Text);
        }
    }

    public WolfgateMarkingPicker()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 8;

        _parts = new GridContainer { Columns = 6, HSeparationOverride = 4, VSeparationOverride = 4 };
        _points = new Label { StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel }, HorizontalAlignment = HAlignment.Right };
        _search = new LineEdit { PlaceHolder = Loc.GetString("markings-search"), HorizontalExpand = true };
        _search.OnTextChanged += args => Populate(args.Text);
        _grid = new GridContainer { Columns = 8, HSeparationOverride = 4, VSeparationOverride = 4 };
        _appliedHeading = new Label { Text = Loc.GetString("wf-creator-markings-applied"), StyleClasses = { StyleWolfgate.StyleClassCreatorHeading } };
        _clearAll = new Button { Text = Loc.GetString("wf-markings-clear-all"), StyleClasses = { StyleWolfgate.StyleClassLinkButton } };
        _clearConfirm = BuildClearConfirm();
        _applied = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 6 };

        AddChild(Card(new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Children =
            {
                new BoxContainer
                {
                    Orientation = LayoutOrientation.Horizontal,
                    SeparationOverride = 8,
                    Children =
                    {
                        new Label { Text = Loc.GetString("wf-creator-markings-available"), StyleClasses = { StyleWolfgate.StyleClassCreatorHeading }, HorizontalExpand = true },
                        _points,
                        _clearAll,
                    },
                },
                _clearConfirm,
                _parts,
                _search,
                // Capped height: past it the grid scrolls inside its box
                new ScrollContainer { HScrollEnabled = false, ReturnMeasure = true, MaxHeight = 420, HorizontalExpand = true, Children = { _grid } },
            },
        }));
        _appliedCard = Card(new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Children = { _appliedHeading, _applied },
        });
        _appliedCard.Visible = false;
        AddChild(_appliedCard);
    }

    /// <summary>
    /// Confirmation row for Clear all, hidden until the button is pressed. Inline rather than a dialog so
    /// the warning sits next to what it is about to wipe.
    /// </summary>
    private BoxContainer BuildClearConfirm()
    {
        var confirm = new Button { Text = Loc.GetString("wf-markings-clear-confirm"), StyleClasses = { StyleWolfgate.StyleClassCreatorPrimary } };
        var cancel = new Button { Text = Loc.GetString("wf-markings-clear-cancel") };

        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            Visible = false,
            Children =
            {
                new RichTextLabel { HorizontalExpand = true, VerticalAlignment = VAlignment.Center },
                confirm,
                cancel,
            },
        };
        ((RichTextLabel) row.Children.First()).SetMessage(Loc.GetString("wf-markings-clear-warning"));

        _clearAll.OnPressed += _ => SetClearConfirmVisible(true);
        cancel.OnPressed += _ => SetClearConfirmVisible(false);
        confirm.OnPressed += _ =>
        {
            SetClearConfirmVisible(false);
            ClearAll();
        };

        return row;
    }

    private void SetClearConfirmVisible(bool visible)
    {
        _clearConfirm.Visible = visible;
        _clearAll.Visible = !visible;
    }

    /// <summary>
    /// Drops every marking this tab owns, then puts back the ones the species requires. Hair and facial
    /// hair are not touched: they have their own pickers on the Appearance tab.
    /// </summary>
    private void ClearAll()
    {
        foreach (var category in _allCategories)
        {
            if (_ignoreCategories.Contains(category) || !_current.Markings.TryGetValue(category, out var list))
                continue;

            foreach (var marking in list.Where(m => !m.Forced).Select(m => m.MarkingId).ToList())
                _current.Remove(category, marking);
        }

        // Required categories would otherwise leave the character missing a part it cannot go without.
        foreach (var (category, points) in _current.Points)
        {
            if (!points.Required || _ignoreCategories.Contains(category))
                continue;

            foreach (var id in points.DefaultMarkings)
            {
                if (_markingManager.Markings.TryGetValue(id, out var proto))
                    _current.AddBack(category, new Marking(id, proto.ResolveLinkedColors(MarkingColoring.GetMarkingLayerColors(proto, CurrentSkinColor, CurrentEyeColor, _current))));
            }
        }

        if (!IgnoreSpecies)
            _current.EnsureSpecies(_species, CurrentSkinColor, _markingManager);

        Populate(_search.Text);
        PopulateUsed();
        OnMarkingRemoved?.Invoke(_current);
    }

    private static PanelContainer Card(Control content)
    {
        return new PanelContainer { StyleClasses = { StyleWolfgate.StyleClassCreatorCard }, HorizontalExpand = true, Children = { content } };
    }

    public void SetData(List<Marking> newMarkings, string species, Sex sex, Color skinColor, Color eyeColor)
    {
        var points = _prototypeManager.Index<SpeciesPrototype>(species).MarkingPoints;
        SetData(new MarkingSet(newMarkings, points, _markingManager), species, sex, skinColor, eyeColor);
    }

    public void SetData(MarkingSet set, string species, Sex sex, Color skinColor, Color eyeColor)
    {
        _current = set;
        if (!IgnoreSpecies)
            _current.EnsureSpecies(species, skinColor, _markingManager);

        _species = species;
        _sex = sex;
        CurrentSkinColor = skinColor;
        CurrentEyeColor = eyeColor;

        Populate(_search.Text);
        PopulateUsed();
    }

    public void SetSkinColor(Color color) => CurrentSkinColor = color;
    public void SetEyeColor(Color color) => CurrentEyeColor = color;

    public void SetSpecies(string species)
    {
        _species = species;
        Rebuild();
    }

    public void SetSex(Sex sex)
    {
        _sex = sex;
        Rebuild();
    }

    /// <summary>Re-validates the set for the current species and sex, keeping whatever markings survive.</summary>
    private void Rebuild()
    {
        var list = _current.GetForwardEnumerator().ToList();
        var speciesProto = _prototypeManager.Index<SpeciesPrototype>(_species);
        _current = new MarkingSet(list, speciesProto.MarkingPoints, _markingManager, _prototypeManager);
        _current.EnsureSpecies(_species, null, _markingManager);
        _current.EnsureSexes(_sex, _markingManager);
        Populate(_search.Text);
        PopulateUsed();
    }

    protected override void Resized()
    {
        base.Resized();
        var columns = Math.Max(1, (int) ((Width - 32) / WolfgateMarkingTile.TileWidth));
        if (_grid.Columns != columns)
            _grid.Columns = columns;
    }

    private IReadOnlyDictionary<string, MarkingPrototype> GetMarkings(MarkingCategories category)
    {
        return IgnoreSpecies
            ? _markingManager.MarkingsByCategoryAndSex(category, _sex)
            : _markingManager.MarkingsByCategoryAndSpeciesAndSex(category, _species, _sex);
    }

    /// <summary>Every marking the species can use, from every category that is not ignored, minus the hidden ones.</summary>
    private IEnumerable<MarkingPrototype> ValidMarkings()
    {
        return _allCategories.Where(c => !_ignoreCategories.Contains(c))
            .SelectMany(c => GetMarkings(c).Values)
            .Where(m => !_hiddenMarkings.Contains(m.ID));
    }

    private IEnumerable<MarkingPrototype> PartMarkings(HumanoidVisualLayers part)
    {
        return ValidMarkings().Where(m => m.BodyPart == part);
    }

    private static int PartRank(HumanoidVisualLayers part)
    {
        var index = Array.IndexOf(PartOrder, part);
        return index >= 0 ? index : PartOrder.Length + (int) part;
    }

    private static string PartName(HumanoidVisualLayers part)
    {
        return Loc.TryGetString($"wf-bodypart-{part}", out var name) ? name : part.ToString();
    }

    /// <summary>One toggle per body part that has markings; only rebuilt when that list changes.</summary>
    private void SetupPartButtons()
    {
        var valid = ValidMarkings().Select(m => m.BodyPart).Distinct().OrderBy(PartRank).ToList();
        if (!valid.Contains(_part))
            _part = valid.Count > 0 ? valid[0] : HumanoidVisualLayers.Chest;
        if (!valid.SequenceEqual(_partButtons.Keys))
        {
            _parts.DisposeAllChildren();
            _partButtons.Clear();
            foreach (var part in valid)
            {
                var button = new Button
                {
                    Text = PartName(part),
                    ToggleMode = true,
                    Group = _partGroup,
                    StyleClasses = { StyleWolfgate.StyleClassCreatorToggle },
                    HorizontalExpand = true,
                };
                var chosen = part;
                button.OnPressed += _ =>
                {
                    _part = chosen;
                    Populate(_search.Text);
                    PopulateUsed();
                };
                _partButtons[part] = button;
                _parts.AddChild(button);
            }
        }

        foreach (var (part, button) in _partButtons)
            button.Pressed = part == _part;
    }

    /// <summary>Rebuilds the tile grid for the selected body part; applied markings are lit and tinted.</summary>
    public void Populate(string filter)
    {
        SetClearConfirmVisible(false);
        SetupPartButtons();
        _grid.DisposeAllChildren();

        var sorted = PartMarkings(_part).Where(m =>
                m.ID.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                Name(m).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(Name);

        foreach (var proto in sorted)
        {
            var applied = _current.TryGetMarking(proto.MarkingCategory, proto.ID, out var marking);
            var tile = new WolfgateMarkingTile(proto.ID, Name(proto), proto.Sprites, _direction)
            {
                Pressed = applied,
            };
            if (applied && marking!.MarkingColors.Count > 0)
                tile.SetColors(proto.ResolveLinkedColors(marking.MarkingColors));
            var chosen = proto;
            tile.OnPressed += _ => Toggle(chosen);
            _grid.AddChild(tile);
        }

        UpdatePoints();
    }

    /// <summary>Points left in every category the selected body part draws its markings from.</summary>
    private void UpdatePoints()
    {
        var counts = PartMarkings(_part)
            .Select(m => m.MarkingCategory)
            .Distinct()
            .Select(c => (category: c, left: _current.PointsLeft(c)))
            .Where(p => p.left != -1)
            .ToList();

        _points.Visible = counts.Count > 0;
        if (counts.Count == 1)
            _points.Text = Loc.GetString("marking-points-remaining", ("points", counts[0].left));
        else if (counts.Count > 1)
            _points.Text = string.Join("   ", counts.Select(p => Loc.GetString("wf-marking-points-category",
                ("category", Loc.GetString($"markings-category-{p.category}")), ("points", p.left))));
    }

    private void Toggle(MarkingPrototype proto)
    {
        if (_current.TryGetMarking(proto.MarkingCategory, proto.ID, out _))
            Remove(proto);
        else
            Add(proto);
    }

    private void Add(MarkingPrototype proto)
    {
        var category = proto.MarkingCategory;

        // A full category whose whole budget is one marking (an undergarment top or bottom, a tail) swaps that
        // marking for the new one in place, as the hair slot picker does, instead of refusing the pick.
        var replaceAt = -1;
        if (_current.PointsLeft(category) == 0 && !Forced)
        {
            replaceAt = SingleSlotIndex(category);
            if (replaceAt < 0)
            {
                Populate(_search.Text);
                return;
            }
        }

        Marking? replaced = replaceAt >= 0 ? _current.Markings[category][replaceAt] : null;
        var marking = proto.AsMarking();

        // Hair lives outside this set, so clone it in for the default colouring rules. The marking being replaced
        // is left out, as if it had been removed first.
        var withHair = new MarkingSet(_current);
        if (replaced != null)
            withHair.Remove(category, replaceAt);
        if (HairMarking != null)
            withHair.AddBack(MarkingCategories.Hair, HairMarking);
        if (FacialHairMarking != null)
            withHair.AddBack(MarkingCategories.FacialHair, FacialHairMarking);

        if (!_markingManager.MustMatchSkin(_species, proto.BodyPart, out _, _prototypeManager))
        {
            var colors = MarkingColoring.GetMarkingLayerColors(proto, CurrentSkinColor, CurrentEyeColor, withHair);
            if (replaced != null)
                KeepColors(proto, replaced, colors);

            // Linked sprites take their parent's colour, so the stored colours match what is drawn.
            colors = proto.ResolveLinkedColors(colors);
            for (var i = 0; i < colors.Count; i++)
                marking.SetColor(i, colors[i]);
        }
        else
        {
            for (var i = 0; i < proto.Sprites.Count; i++)
                marking.SetColor(i, CurrentSkinColor);
        }

        marking.Forced = Forced;
        // Replace keeps the points as they are: both markings count against the same single point.
        if (replaced != null)
            _current.Replace(category, replaceAt, marking);
        else
            _current.AddBack(category, marking);

        Populate(_search.Text);
        PopulateUsed();
        OnMarkingAdded?.Invoke(_current);
    }

    /// <summary>
    /// Index of the only marking counted in a full category whose whole budget is one marking, or -1 when the
    /// category is not such a single slot. Forced markings cost no points, so they are skipped.
    /// </summary>
    private int SingleSlotIndex(MarkingCategories category)
    {
        if (_current.PointsLeft(category) != 0 || !_current.Markings.TryGetValue(category, out var applied))
            return -1;

        var index = -1;
        for (var i = 0; i < applied.Count; i++)
        {
            if (applied[i].Forced)
                continue;

            if (index >= 0)
                return -1;

            index = i;
        }

        return index;
    }

    /// <summary>
    /// A swapped-in marking keeps the colours of the one it replaces, sprite by sprite, as a new hairstyle keeps the
    /// hair colour. Markings with forced colouring keep their own colours.
    /// </summary>
    private void KeepColors(MarkingPrototype proto, Marking replaced, List<Color> colors)
    {
        if (proto.ForcedColoring
            || !_markingManager.TryGetMarking(replaced, out var replacedProto)
            || replacedProto.ForcedColoring)
        {
            return;
        }

        for (var i = 0; i < colors.Count && i < replaced.MarkingColors.Count; i++)
            colors[i] = replaced.MarkingColors[i];
    }

    private void Remove(MarkingPrototype proto)
    {
        _current.Remove(proto.MarkingCategory, proto.ID);
        Populate(_search.Text);
        PopulateUsed();
        OnMarkingRemoved?.Invoke(_current);
    }

    /// <summary>One card per applied marking on the selected body part; within a category the top card draws on top.</summary>
    public void PopulateUsed()
    {
        _applied.DisposeAllChildren();
        _appliedIcons.Clear();
        if (!IgnoreSpecies)
            _current.EnsureSpecies(_species, null, _markingManager);

        var any = false;
        foreach (var category in _allCategories)
        {
            if (_ignoreCategories.Contains(category))
                continue;

            var stack = new List<(Marking marking, MarkingPrototype proto)>();
            foreach (var marking in _current.GetReverseEnumerator(category))
            {
                if (_markingManager.TryGetMarking(marking, out var proto) && proto.BodyPart == _part)
                    stack.Add((marking, proto));
            }

            for (var i = 0; i < stack.Count; i++)
            {
                any = true;
                _applied.AddChild(BuildAppliedCard(stack[i].marking, stack[i].proto, i == 0, i == stack.Count - 1));
            }
        }

        _appliedCard.Visible = any;
        UpdatePoints();
    }

    private Control BuildAppliedCard(Marking marking, MarkingPrototype proto, bool topmost, bool bottommost)
    {
        var icon = new WolfgateMarkingIcon(proto.Sprites, _direction);
        icon.SetColors(proto.ResolveLinkedColors(marking.MarkingColors));
        _appliedIcons.Add(icon);
        var name = new Label
        {
            Text = Loc.GetString(marking.Forced ? "marking-used-forced" : "marking-used",
                ("marking-name", Name(proto)), ("marking-category", Loc.GetString($"markings-category-{proto.MarkingCategory}"))),
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };
        var up = new Button { Text = Loc.GetString("wf-marking-up"), StyleClasses = { StyleWolfgate.StyleClassLinkButton }, Disabled = topmost };
        var down = new Button { Text = Loc.GetString("wf-marking-down"), StyleClasses = { StyleWolfgate.StyleClassLinkButton }, Disabled = bottommost };
        var remove = new Button { Text = Loc.GetString("wf-marking-remove"), StyleClasses = { StyleWolfgate.StyleClassLinkButton } };
        up.OnPressed += _ => Shift(proto, true);
        down.OnPressed += _ => Shift(proto, false);
        remove.OnPressed += _ => Remove(proto);

        var card = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4 };
        card.AddChild(new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            Children = { icon, name, up, down, remove },
        });

        if (!proto.ForcedColoring)
        {
            var layers = GetMarkingStateNames(proto);
            // A sprite linked to another takes that sprite's colour, so only the unlinked ones get a picker.
            var ownColours = Enumerable.Range(0, proto.Sprites.Count).Count(j => !proto.IsColorLinked(j));
            for (var i = 0; i < proto.Sprites.Count && i < marking.MarkingColors.Count; i++)
            {
                if (proto.IsColorLinked(i))
                    continue;

                var layer = i;
                var picker = new WolfgateColorPicker { HorizontalExpand = true, Color = marking.MarkingColors[i] };
                picker.SelectorType = ColorSelectorSliders.ColorSelectorType.Hsv;
                picker.OnColorChanged += color =>
                {
                    Recolor(proto, layer, color);
                    if (_current.TryGetMarking(proto.MarkingCategory, proto.ID, out var updated))
                        icon.SetColors(updated.MarkingColors);
                };
                var caption = ownColours == 1
                    ? Loc.GetString("wf-marking-colour")
                    : Loc.GetString("wf-marking-layer-colour", ("layer", layers[i]));
                card.AddChild(new Label { Text = caption, StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel } });
                card.AddChild(picker);
            }
        }

        var panel = new PanelContainer { StyleClasses = { StyleWolfgate.StyleClassCreatorCard }, HorizontalExpand = true };
        panel.AddChild(card);
        return panel;
    }

    /// <summary>
    /// Moves a marking past the next applied marking on the same body part within its category's draw order.
    /// Later entries draw on top, and entries on other body parts are stepped over since their order is irrelevant.
    /// </summary>
    private void Shift(MarkingPrototype proto, bool towardsTop)
    {
        var category = proto.MarkingCategory;
        if (!_current.Markings.TryGetValue(category, out var list))
            return;

        var index = _current.FindIndexOf(category, proto.ID);
        if (index < 0)
            return;

        var step = towardsTop ? 1 : -1;
        var target = -1;
        for (var i = index + step; i >= 0 && i < list.Count; i += step)
        {
            if (_markingManager.TryGetMarking(list[i], out var other) && other.BodyPart == _part)
            {
                target = i;
                break;
            }
        }
        if (target < 0)
            return;

        while (index != target)
        {
            if (step > 0)
                _current.ShiftRankDown(category, index);
            else
                _current.ShiftRankUp(category, index);
            index += step;
        }

        PopulateUsed();
        OnMarkingRankChange?.Invoke(_current);
    }

    private void Recolor(MarkingPrototype proto, int layer, Color color)
    {
        var category = proto.MarkingCategory;
        var index = _current.FindIndexOf(category, proto.ID);
        if (index < 0)
            return;

        var marking = new Marking(_current.Markings[category][index]);
        marking.SetColor(layer, color);

        // Keep linked sprites in step with the sprite they follow, so the tiles and the saved colours agree.
        var resolved = proto.ResolveLinkedColors(marking.MarkingColors);
        for (var i = 0; i < resolved.Count; i++)
            marking.SetColor(i, resolved[i]);
        _current.Replace(category, index, marking);

        foreach (var child in _grid.Children)
        {
            if (child is WolfgateMarkingTile { MarkingId: { } id } tile && id == proto.ID)
                tile.SetColors(marking.MarkingColors);
        }

        OnMarkingColorChange?.Invoke(_current);
    }

    private static string Name(MarkingPrototype proto) => Loc.GetString($"marking-{proto.ID}");

    /// <summary>
    /// Display name per sprite layer. Most markings have no per-layer string - only about two thirds are
    /// translated - so an untranslated layer falls back to its readable state name rather than the raw key.
    /// </summary>
    private static List<string> GetMarkingStateNames(MarkingPrototype proto)
    {
        var result = new List<string>();
        foreach (var state in proto.Sprites)
        {
            switch (state)
            {
                case SpriteSpecifier.Rsi rsi:
                    result.Add(LayerName(proto.ID, rsi.RsiState));
                    break;
                case SpriteSpecifier.Texture texture:
                    result.Add(LayerName(proto.ID, texture.TexturePath.Filename));
                    break;
                default:
                    result.Add(proto.ID);
                    break;
            }
        }
        return result;
    }

    private static string LayerName(string markingId, string state)
    {
        return WolfgateMarkingNames.LayerName(markingId, state);
    }
}
