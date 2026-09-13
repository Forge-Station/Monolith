using System.Linq;
using Content.Client._WF.Stylesheets;
using Content.Client._WF.UserInterface.Controls;
using Content.Shared.Humanoid.Markings;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.Utility;

namespace Content.Client._WF.Humanoid;

/// <summary>
/// Grid picker for one marking category with slots (hair, facial hair). Same surface as SingleMarkingPicker,
/// so the profile editor drives it unchanged: tiles select, the "none" tile clears the slot.
/// </summary>
public sealed class WolfgateSlotMarkingPicker : BoxContainer
{
    [Dependency] private MarkingManager _markingManager = default!;

    /// <summary>Raised with the slot index and the chosen marking id.</summary>
    public Action<(int slot, string id)>? OnMarkingSelect;
    /// <summary>Raised with the slot index that was cleared.</summary>
    public Action<int>? OnSlotRemove;
    /// <summary>Raised when a slot should be added; the owner fills it with a default marking.</summary>
    public Action? OnSlotAdd;
    /// <summary>Raised with the slot index and the recoloured marking.</summary>
    public Action<(int slot, Marking marking)>? OnColorChanged;

    private readonly Label _categoryName;
    private readonly OptionButton _slotSelector;
    private readonly Button _addSlot;
    private readonly Button _removeSlot;
    private readonly LineEdit _search;
    private readonly GridContainer _grid;
    private readonly BoxContainer _colors;
    private readonly ButtonGroup _group = new();
    private readonly List<WolfgateMarkingTile> _tiles = new();

    private MarkingCategories _category;
    private IReadOnlyDictionary<string, MarkingPrototype>? _prototypes;
    private string? _species;
    private List<Marking>? _markings;
    private int _totalPoints;
    private int _slot = -1;
    private Direction _direction = Direction.South;

    /// <summary>Direction the tile sprites face; follows the preview pawn.</summary>
    public Direction PreviewDirection
    {
        get => _direction;
        set
        {
            _direction = value;
            foreach (var tile in _tiles)
                tile.SetDirection(value);
        }
    }

    public MarkingCategories Category
    {
        get => _category;
        set
        {
            _category = value;
            _categoryName.Text = Loc.GetString($"markings-category-{_category}");
            if (!string.IsNullOrEmpty(_species))
                Populate(_search.Text);
        }
    }

    private int Slot
    {
        get
        {
            if (_markings == null || _markings.Count == 0)
                _slot = -1;
            else if (_slot == -1)
                _slot = 0;
            return _slot;
        }
        set
        {
            _slot = _markings == null || _markings.Count == 0 ? -1 : value;
            Populate(_search.Text);
            PopulateColors();
        }
    }

    private int PointsUsed => _markings?.Count ?? 0;
    private int PointsLeft => _markings == null ? 0 : _totalPoints < 0 ? -1 : _totalPoints - _markings.Count;

    public WolfgateSlotMarkingPicker()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 6;

        _categoryName = new Label { StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel }, VerticalAlignment = VAlignment.Center };
        _slotSelector = new OptionButton { Visible = false };
        _slotSelector.OnItemSelected += args =>
        {
            _slotSelector.SelectId(args.Id);
            Slot = args.Id;
        };
        _addSlot = new Button { Text = Loc.GetString("marking-slot-add"), StyleClasses = { StyleWolfgate.StyleClassLinkButton }, Visible = false };
        _addSlot.OnPressed += _ => OnSlotAdd?.Invoke();
        _removeSlot = new Button { Text = Loc.GetString("marking-slot-remove"), StyleClasses = { StyleWolfgate.StyleClassLinkButton }, Visible = false };
        _removeSlot.OnPressed += _ => OnSlotRemove?.Invoke(Slot);

        AddChild(new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            Children = { _categoryName, new Control { HorizontalExpand = true }, _slotSelector, _addSlot, _removeSlot },
        });

        _search = new LineEdit { PlaceHolder = Loc.GetString("markings-search"), HorizontalExpand = true };
        _search.OnTextChanged += args => Populate(args.Text);
        AddChild(_search);

        _grid = new GridContainer { Columns = 5, HSeparationOverride = 4, VSeparationOverride = 4 };
        // Capped height: past it the grid scrolls inside its box
        AddChild(new ScrollContainer { HScrollEnabled = false, ReturnMeasure = true, MaxHeight = 320, HorizontalExpand = true, Children = { _grid } });

        _colors = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 6 };
        AddChild(_colors);
    }

    public void UpdateData(List<Marking> markings, string species, int totalPoints)
    {
        _markings = markings;
        _species = species;
        _totalPoints = totalPoints;
        _prototypes = _markingManager.MarkingsByCategoryAndSpecies(Category, _species);

        Visible = _prototypes.Count != 0;
        if (!Visible)
            return;

        Populate(_search.Text);
        PopulateColors();
        PopulateSlotControls();
    }

    protected override void Resized()
    {
        base.Resized();
        // c tiles occupy 108c - 4, and the vertical scrollbar eats 10 more whenever the grid overflows.
        var columns = Math.Max(1, (int) ((Width - 6) / WolfgateMarkingTile.TileWidth));
        if (_grid.Columns != columns)
            _grid.Columns = columns;
    }

    /// <summary>Rebuilds the tile grid: a "none" tile first, then every marking matching the filter.</summary>
    private void Populate(string filter)
    {
        if (string.IsNullOrEmpty(_species))
            return;

        _prototypes ??= _markingManager.MarkingsByCategoryAndSpecies(Category, _species);
        _grid.DisposeAllChildren();
        _tiles.Clear();

        var current = Slot >= 0 ? _markings![Slot] : null;
        var tint = current?.MarkingColors.Count > 0 ? current.MarkingColors[0] : Color.White;

        AddTile(new WolfgateMarkingTile(null, Loc.GetString("wf-marking-none"), null, _direction) { Pressed = current == null });

        var sorted = _prototypes.Where(m =>
                m.Key.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                Name(m.Value).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => Name(p.Value));

        foreach (var (id, proto) in sorted)
        {
            var tile = new WolfgateMarkingTile(id, Name(proto), proto.Sprites, _direction)
            {
                Pressed = current?.MarkingId == id,
                Tint = tint,
            };
            AddTile(tile);
        }
    }

    private void AddTile(WolfgateMarkingTile tile)
    {
        tile.Group = _group;
        tile.OnPressed += _ => TilePressed(tile);
        _tiles.Add(tile);
        _grid.AddChild(tile);
    }

    private void TilePressed(WolfgateMarkingTile tile)
    {
        if (tile.MarkingId == null)
        {
            if (Slot >= 0)
                OnSlotRemove?.Invoke(Slot);
            return;
        }

        // No slot yet: let the owner create one (it calls UpdateData back), then pick into it.
        if (Slot < 0)
            OnSlotAdd?.Invoke();
        if (Slot < 0)
            return;

        Select(tile.MarkingId);
    }

    private void Select(string id)
    {
        if (_markings == null || !_markingManager.Markings.TryGetValue(id, out var proto))
            return;

        var old = _markings[Slot];
        if (old.MarkingId == id)
            return;

        _markings[Slot] = proto.AsMarking();
        for (var i = 0; i < _markings[Slot].MarkingColors.Count && i < old.MarkingColors.Count; i++)
            _markings[Slot].SetColor(i, old.MarkingColors[i]);

        PopulateColors();
        OnMarkingSelect?.Invoke((_slot, id));
        Populate(_search.Text);
    }

    private void PopulateColors()
    {
        _colors.DisposeAllChildren();
        if (_markings == null || _markings.Count == 0 || !_markingManager.TryGetMarking(_markings[Slot], out var proto))
            return;

        var marking = _markings[Slot];
        if (marking.MarkingColors.Count != proto.Sprites.Count)
            marking = new Marking(marking.MarkingId, proto.Sprites.Count);

        for (var i = 0; i < marking.MarkingColors.Count; i++)
        {
            var picker = new WolfgateColorPicker { HorizontalExpand = true, Color = marking.MarkingColors[i] };
            picker.SelectorType = ColorSelectorSliders.ColorSelectorType.Hsv;
            var layer = i;
            picker.OnColorChanged += color =>
            {
                marking.SetColor(layer, color);
                if (layer == 0)
                {
                    foreach (var tile in _tiles.Where(t => t.MarkingId != null))
                        tile.Tint = color;
                }
                OnColorChanged?.Invoke((_slot, marking));
            };
            _colors.AddChild(picker);
        }
    }

    private void PopulateSlotControls()
    {
        var multi = _totalPoints != 1;
        _slotSelector.Visible = multi && PointsUsed > 1;
        _addSlot.Visible = multi && PointsUsed > 0 && PointsLeft != 0;
        _removeSlot.Visible = multi && PointsUsed > 1;
        _slotSelector.Clear();
        if (!_slotSelector.Visible)
            return;

        for (var i = 0; i < PointsUsed; i++)
        {
            _slotSelector.AddItem(Loc.GetString("marking-slot", ("number", $"{i + 1}")), i);
            if (i == Slot)
                _slotSelector.SelectId(i);
        }
    }

    private static string Name(MarkingPrototype proto) => Loc.GetString($"marking-{proto.ID}");
}
