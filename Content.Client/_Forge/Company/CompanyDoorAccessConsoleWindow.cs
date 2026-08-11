using System.Linq;
using System.Numerics;
using Content.Client.Pinpointer.UI;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Shared._Forge.Company;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Forge.Company;

public sealed class CompanyDoorAccessConsoleWindow : FancyWindow
{
    public Action<List<NetEntity>, CompanyDoorAccessMode>? OnSetMode;

    private readonly IEntityManager _entManager;
    private readonly SpriteSystem _sprite;

    private readonly Label _status = new() { HorizontalAlignment = HAlignment.Center };
    private readonly Label _selectionInfo = new()
    {
        Text = Loc.GetString("company-door-console-select-door"),
        HorizontalAlignment = HAlignment.Center,
    };
    private readonly CompanyDoorNavMapControl _navMap = new();
    private readonly BoxContainer _doorList = new() { Orientation = LayoutOrientation.Vertical };
    private readonly ScrollContainer _listScroll = new()
    {
        VerticalExpand = true,
        HorizontalExpand = true,
        HScrollEnabled = false,
    };
    private readonly BoxContainer _modeButtons = new()
    {
        Orientation = LayoutOrientation.Vertical,
        SeparationOverride = 4,
    };
    private readonly BoxContainer _legend = new()
    {
        Orientation = LayoutOrientation.Horizontal,
        HorizontalAlignment = HAlignment.Center,
        SeparationOverride = 12,
        Margin = new Thickness(0, 4, 0, 0),
    };

    private readonly Dictionary<NetEntity, CompanyDoorAccessEntry> _doors = new();
    private readonly Dictionary<NetEntity, Button> _listButtons = new();
    private readonly HashSet<NetEntity> _selected = new();
    private readonly Dictionary<CompanyDoorAccessMode, Button> _modeButtonByMode = new();
    private readonly Texture _blipTexture;
    private bool _canConfigure;
    private CompanyAccessTier _maxAccessTier;

    public CompanyDoorAccessConsoleWindow()
    {
        _entManager = IoCManager.Resolve<IEntityManager>();
        _sprite = _entManager.System<SpriteSystem>();

        Title = Loc.GetString("company-door-console-title");
        SetSize = new Vector2(1280, 860);
        MinSize = new Vector2(1100, 720);

        _blipTexture = _sprite.Frame0(new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/NavMap/beveled_circle.png")));
        _navMap.DoorPicked += OnMapDoorClicked;
        _navMap.VerticalExpand = true;
        _navMap.HorizontalExpand = true;
        _listScroll.AddChild(_doorList);

        BuildLegend();
        BuildModeButtons();

        var selectAll = new Button { Text = Loc.GetString("company-door-console-select-all"), HorizontalExpand = true };
        selectAll.OnPressed += _ => SelectAll();
        var clearSel = new Button { Text = Loc.GetString("company-door-console-clear-selection"), HorizontalExpand = true };
        clearSel.OnPressed += _ =>
        {
            _selected.Clear();
            RefreshHighlights();
        };

        var listHeader = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            Children = { selectAll, clearSel },
        };

        var accessHeader = new Label
        {
            Text = Loc.GetString("company-door-console-set-access"),
            HorizontalAlignment = HAlignment.Center,
            Margin = new Thickness(0, 8, 0, 4),
            StyleClasses = { "LabelHeading" },
        };

        var sidePanel = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SetWidth = 320,
            Margin = new Thickness(8, 0, 0, 0),
            Children =
            {
                new Label
                {
                    Text = Loc.GetString("company-door-console-door-list"),
                    HorizontalAlignment = HAlignment.Center,
                    StyleClasses = { "LabelHeading" },
                },
                listHeader,
                _listScroll,
                _selectionInfo,
                accessHeader,
                _modeButtons,
            },
        };

        var mapColumn = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Children = { _navMap, _legend },
        };

        ContentsContainer.AddChild(new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(8),
            Children =
            {
                _status,
                new BoxContainer
                {
                    Orientation = LayoutOrientation.Horizontal,
                    VerticalExpand = true,
                    HorizontalExpand = true,
                    Children = { mapColumn, sidePanel },
                },
            },
        });
    }

    private void BuildLegend()
    {
        AddLegendItem(_legend, Color.LightGray, Loc.GetString("company-door-mode-open"));
        AddLegendItem(_legend, Color.LimeGreen, Loc.GetString("company-door-mode-low"));
        AddLegendItem(_legend, Color.Gold, Loc.GetString("company-door-mode-medium"));
        AddLegendItem(_legend, Color.OrangeRed, Loc.GetString("company-door-mode-high"));
    }

    private static void AddLegendItem(BoxContainer parent, Color color, string text)
    {
        parent.AddChild(new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            Children =
            {
                new PanelContainer
                {
                    SetSize = new Vector2(12, 12),
                    VerticalAlignment = VAlignment.Center,
                    PanelOverride = new StyleBoxFlat { BackgroundColor = color },
                },
                new Label { Text = text, Margin = new Thickness(4, 0, 0, 0) },
            },
        });
    }

    private void BuildModeButtons()
    {
        AddModeButton(CompanyDoorAccessMode.Open, Loc.GetString("company-door-mode-open"));
        AddModeButton(CompanyDoorAccessMode.Low, Loc.GetString("company-door-mode-low"));
        AddModeButton(CompanyDoorAccessMode.Medium, Loc.GetString("company-door-mode-medium"));
        AddModeButton(CompanyDoorAccessMode.High, Loc.GetString("company-door-mode-high"));
        _modeButtons.Visible = false;
    }

    private void AddModeButton(CompanyDoorAccessMode mode, string text)
    {
        var btn = new Button { Text = text, HorizontalExpand = true };
        btn.OnPressed += _ =>
        {
            if (!_canConfigure || _selected.Count == 0)
                return;
            if ((CompanyAccessTier)mode > _maxAccessTier)
                return;

            // Only send doors the user is allowed to change to this mode.
            var allowed = _selected
                .Where(id => _doors.TryGetValue(id, out var entry) &&
                             CompanyAccessHelper.CanConfigureDoorMode(_maxAccessTier, entry.Mode, mode))
                .ToList();
            if (allowed.Count == 0)
                return;

            OnSetMode?.Invoke(allowed, mode);
        };
        _modeButtonByMode[mode] = btn;
        _modeButtons.AddChild(btn);
    }

    public void UpdateState(CompanyDoorAccessConsoleBoundUserInterfaceState state)
    {
        _doors.Clear();
        _listButtons.Clear();
        _doorList.RemoveAllChildren();
        _navMap.TrackedEntities.Clear();
        _navMap.TrackedCoordinates.Clear();
        _canConfigure = state.CanConfigure;
        _maxAccessTier = state.MaxAccessTier;

        foreach (var (mode, btn) in _modeButtonByMode)
            btn.Disabled = (CompanyAccessTier)mode > _maxAccessTier;

        if (!state.Active)
        {
            _status.Text = state.StatusMessage;
            _navMap.Visible = false;
            _selected.Clear();
            RefreshHighlights();
            return;
        }

        _status.Text = Loc.GetString("company-door-console-bound", ("company", state.BoundCompanyName));
        _navMap.Visible = true;

        if (state.GridEntity is { } gridNet && _entManager.TryGetEntity(gridNet, out var gridUid))
            _navMap.MapUid = gridUid;
        else
            _navMap.MapUid = null;

        if (state.ConsoleCoordinates is { } consoleCoords)
            _navMap.TrackedCoordinates[_entManager.GetCoordinates(consoleCoords)] = (true, Color.Aqua);

        _selected.RemoveWhere(id => state.Doors.All(d => d.Door != id));

        foreach (var door in state.Doors.OrderBy(d => d.Name))
        {
            _doors[door.Door] = door;
            var coords = _entManager.GetCoordinates(door.Coordinates);
            _navMap.TrackedEntities[door.Door] = MakeBlip(coords, door.Mode, _selected.Contains(door.Door));

            var modeName = Loc.GetString($"company-door-mode-{door.Mode.ToString().ToLowerInvariant()}");
            var locked = (CompanyAccessTier)door.Mode > _maxAccessTier;
            if (locked)
                _selected.Remove(door.Door);

            var btn = new Button
            {
                Text = $"{door.Name}  [{modeName}]",
                HorizontalExpand = true,
                ToggleMode = true,
                Pressed = _selected.Contains(door.Door),
                Disabled = locked,
                ToolTip = locked
                    ? Loc.GetString("company-door-console-tier-locked")
                    : null,
            };
            var doorId = door.Door;
            if (!locked)
                btn.OnPressed += _ => ToggleDoor(doorId);
            _listButtons[door.Door] = btn;
            _doorList.AddChild(btn);
        }

        _navMap.ForceNavMapUpdate();
        RefreshHighlights();
    }

    private void OnMapDoorClicked(NetEntity? door)
    {
        if (door is not { } id || !_doors.ContainsKey(id))
            return;

        // Can't select doors above your access tier.
        if ((CompanyAccessTier)_doors[id].Mode > _maxAccessTier)
            return;

        ToggleDoor(id);
    }

    private void ToggleDoor(NetEntity door)
    {
        if (!_selected.Add(door))
            _selected.Remove(door);

        RefreshHighlights();
    }

    private void SelectAll()
    {
        _selected.Clear();
        foreach (var (id, entry) in _doors)
        {
            if ((CompanyAccessTier)entry.Mode > _maxAccessTier)
                continue;
            _selected.Add(id);
        }
        RefreshHighlights();
    }

    private void RefreshHighlights()
    {
        foreach (var (id, entry) in _doors)
        {
            var selected = _selected.Contains(id);
            if (_navMap.TrackedEntities.TryGetValue(id, out var blip))
                _navMap.TrackedEntities[id] = MakeBlip(blip.Coordinates, entry.Mode, selected);

            if (_listButtons.TryGetValue(id, out var btn))
            {
                btn.Pressed = selected;
                btn.ModulateSelfOverride = selected ? Color.FromHex("#7ec8ff") : null;
            }
        }

        if (_selected.Count == 0)
        {
            _selectionInfo.Text = Loc.GetString("company-door-console-select-door");
            _modeButtons.Visible = false;
            return;
        }

        _selectionInfo.Text = Loc.GetString("company-door-console-selected-count", ("count", _selected.Count));
        _modeButtons.Visible = _canConfigure;
    }

    private NavMapBlip MakeBlip(EntityCoordinates coords, CompanyDoorAccessMode mode, bool selected)
    {
        var color = selected ? Color.Cyan : ModeColor(mode);
        return new NavMapBlip(coords, _blipTexture, color, blinks: selected, selectable: true, scale: selected ? 1.55f : 1.25f);
    }

    private static Color ModeColor(CompanyDoorAccessMode mode) => mode switch
    {
        CompanyDoorAccessMode.Low => Color.LimeGreen,
        CompanyDoorAccessMode.Medium => Color.Gold,
        CompanyDoorAccessMode.High => Color.OrangeRed,
        _ => Color.White,
    };
}

/// <summary>
/// Screen-space click picking for door blips (more reliable than world-distance checks).
/// </summary>
public sealed class CompanyDoorNavMapControl : NavMapControl
{
    public event Action<NetEntity?>? DoorPicked;

    private readonly SharedTransformSystem _transformSystem;

    public CompanyDoorNavMapControl()
    {
        MaxSelectableDistance = 64f;
        _transformSystem = EntManager.System<SharedTransformSystem>();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.UIClick)
        {
            if (TryPickDoor(args))
                args.Handle();
            return;
        }

        if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            DoorPicked?.Invoke(null);
            args.Handle();
            return;
        }

        base.KeyBindUp(args);
    }

    private bool TryPickDoor(GUIBoundKeyEventArgs args)
    {
        if (DoorPicked == null || TrackedEntities.Count == 0 || MapUid == null)
            return false;

        if ((StartDragPosition - args.PointerLocation.Position).Length() > MinDragDistance)
            return false;

        if (!EntManager.TryGetComponent(MapUid, out TransformComponent? xform))
            return false;

        EntManager.TryGetComponent(MapUid, out PhysicsComponent? physics);
        var offset = Offset + (physics?.LocalCenter ?? Vector2.Zero);
        var clickPos = args.PointerLocation.Position - GlobalPixelPosition;

        var closestEntity = NetEntity.Invalid;
        var closestDistance = float.PositiveInfinity;

        foreach (var (entity, blip) in TrackedEntities)
        {
            if (!blip.Selectable)
                continue;

            var mapPos = _transformSystem.ToMapCoordinates(blip.Coordinates);
            if (mapPos.MapId == MapId.Nullspace)
                continue;

            var position = Vector2.Transform(mapPos.Position, _transformSystem.GetInvWorldMatrix(xform)) - offset;
            position = ScalePosition(new Vector2(position.X, -position.Y));

            var dist = (position - clickPos).Length();
            if (dist >= closestDistance)
                continue;

            closestDistance = dist;
            closestEntity = entity;
        }

        var hitRadius = MathF.Max(28f, 20f * UIScale);
        if (!closestEntity.IsValid() || closestDistance > hitRadius)
            return false;

        DoorPicked.Invoke(closestEntity);
        return true;
    }
}
