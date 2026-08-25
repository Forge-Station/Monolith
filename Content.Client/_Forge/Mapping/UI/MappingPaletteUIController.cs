using System.Linq;
using Content.Client._Forge.Mapping.UI;
using Content.Client.Administration.Managers;
using Content.Client.Decals;
using Content.Client.Gameplay;
using Content.Client.Mapping;
using Content.Client.Sandbox;
using Content.Shared.Decals;
using Content.Shared.Input;
using Content.Shared.Maps;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Placement;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using static Robust.Client.UserInterface.Controls.BaseButton;
using static Robust.Client.UserInterface.Controls.LineEdit;
using static Robust.Client.UserInterface.Controls.OptionButton;

namespace Content.Client._Forge.Mapping;

[UsedImplicitly]
public sealed class MappingPaletteUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>, IOnSystemChanged<SandboxSystem>
{
    [Dependency] private readonly IClientAdminManager _admin = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPlacementManager _placement = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IResourceCache _resources = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    [UISystemDependency] private readonly SandboxSystem _sandbox = default!;

    private MappingPaletteWindow? _window;
    private MappingPaletteSession? _session;
    private DecalPlacementSystem _decals = default!;
    private MappingEyedropperSystem _eyedropper = default!;
    private SpriteSystem _sprite = default!;
    private bool _updateEraseDecal;

    public bool IsOpen => _window is { Disposed: false, IsOpen: true };

    public void OnStateEntered(GameplayState state)
    {
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.MappingUnselect, new PointerInputCmdHandler(HandleUnselect, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingEyedropper, new PointerInputCmdHandler((s, c, u) => HandleEyedropper(s, c, u, MappingEyedropperMode.Auto), outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingEyedropperTile, new PointerInputCmdHandler((s, c, u) => HandleEyedropper(s, c, u, MappingEyedropperMode.Tile), outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingEyedropperDecal, new PointerInputCmdHandler((s, c, u) => HandleEyedropper(s, c, u, MappingEyedropperMode.Decal), outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingRemoveDecal, new PointerInputCmdHandler(HandleEraseDecal, outsidePrediction: true))
            .Register<MappingPaletteUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        CommandBinds.Unregister<MappingPaletteUIController>();
        CloseWindow();
    }

    public void OnSystemLoaded(SandboxSystem system)
    {
        system.SandboxDisabled += CloseWindow;
        _prototypes.PrototypesReloaded += OnPrototypesReloaded;
    }

    public void OnSystemUnloaded(SandboxSystem system)
    {
        system.SandboxDisabled -= CloseWindow;
        _prototypes.PrototypesReloaded -= OnPrototypesReloaded;
    }

    public void ToggleWindow()
    {
        if (!_admin.CanAdminPlace() && !_sandbox.SandboxAllowed)
            return;

        EnsureWindow();

        if (_window!.IsOpen)
        {
            _window.Close();
            return;
        }

        RefreshView();
        _window.Open();
    }

    public void CloseWindow()
    {
        if (_window == null || _window.Disposed)
            return;

        _window.Close();
    }

    public void Reveal(IPrototype prototype, Decal? sampledDecal = null)
    {
        EnsureWindow();
        if (_session == null || !_session.TryGet(prototype, out var mapping))
            return;

        var tab = MappingPaletteCatalog.TabFor(prototype);
        _window!.Prototypes.SetTab(tab, invoke: false);
        _window.Prototypes.SearchBar.Text = prototype.ID;
        RefreshView();
        Select(mapping, revealInTree: false, sampledDecal);
        if (!_window.IsOpen)
            _window.Open();
    }

    private void EnsureWindow()
    {
        if (_window is { Disposed: false })
            return;

        _decals = _entities.System<DecalPlacementSystem>();
        _eyedropper = _entities.System<MappingEyedropperSystem>();
        _sprite = _entities.System<SpriteSystem>();
        _session = new MappingPaletteSession(new MappingPaletteMemory(_cfg));
        _session.LoadFromPrototypes(_prototypes);

        _window = UIManager.CreateWindow<MappingPaletteWindow>();
        _window.DecalSystem = _decals;
        LayoutContainer.SetAnchorPreset(_window, LayoutContainer.LayoutPreset.CenterLeft);

        _window.Prototypes.GetPrototypeData += OnGetData;
        _window.Prototypes.SelectionChanged += OnSelected;
        _window.Prototypes.CollapseToggled += OnCollapseToggled;
        _window.Prototypes.TabChanged += _ => RefreshView();
        _window.Prototypes.FavoriteToggled += OnFavoriteToggled;
        _window.Prototypes.RecentClicked += mapping =>
        {
            if (mapping.Prototype != null)
                Reveal(mapping.Prototype);
        };
        _window.Prototypes.SearchBar.OnTextChanged += OnSearch;
        _window.Prototypes.ClearSearchButton.OnPressed += _ =>
        {
            _window.Prototypes.SearchBar.Text = string.Empty;
            RefreshView();
        };
        _window.Prototypes.ClearPlacementButton.OnPressed += _ => ClearPlacement();
        _window.Prototypes.CollapseAllButton.OnPressed += _ =>
        {
            foreach (var child in _window.Prototypes.PrototypeList.Children)
            {
                if (child is MappingSpawnButton button)
                    Collapse(button);
            }
        };
        _window.Prototypes.SetFavoritePredicate(_session.IsFavorite);
        _window.EntityReplaceButton.OnToggled += args => _placement.Replacement = args.Pressed;
        _window.EntityPlacementMode.OnItemSelected += OnEntityPlacementSelected;
        _window.EraseEntityButton.OnToggled += OnEraseEntity;
        _window.EraseDecalButton.OnToggled += OnEraseDecal;
        _window.OnClose += ClearPlacement;

        RefreshView();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<EntityPrototype>() &&
            !args.WasModified<ContentTileDefinition>() &&
            !args.WasModified<DecalPrototype>())
        {
            return;
        }

        if (_window == null || _window.Disposed || _session == null)
            return;

        _session.LoadFromPrototypes(_prototypes);
        RefreshView();
    }

    private void RefreshView()
    {
        if (_window == null || _session == null)
            return;

        var tab = _window.Prototypes.CurrentTab;
        var query = _window.Prototypes.SearchBar.Text;

        if (_window.Prototypes.GridMode && tab is MappingPaletteTab.Tiles or MappingPaletteTab.Decals)
        {
            var items = string.IsNullOrEmpty(query) ? _session.FlatFor(tab) : _session.Search(query, tab);
            _window.Prototypes.PopulateGrid(items);
        }
        else if (!string.IsNullOrEmpty(query))
        {
            _window.Prototypes.Search(_session.Search(query, tab));
        }
        else
        {
            _window.Prototypes.UpdateVisible(_session.RootsFor(tab));
        }

        _window.Prototypes.SetFavoritePredicate(_session.IsFavorite);
        _window.Prototypes.RefreshRecents(_session.ResolveRecents());
    }

    private void OnGetData(IPrototype prototype, List<Texture> textures)
    {
        switch (prototype)
        {
            case EntityPrototype entity:
                textures.AddRange(SpriteComponent.GetPrototypeTextures(entity, _resources).Select(t => t.Default));
                break;
            case DecalPrototype decal:
                textures.Add(_sprite.Frame0(decal.Sprite));
                break;
            case ContentTileDefinition tile:
                if (tile.Sprite?.ToString() is { } sprite)
                    textures.Add(_resources.GetResource<TextureResource>(sprite).Texture);
                break;
        }
    }

    private void OnSelected(MappingSpawnButton button, IPrototype? prototype)
    {
        if (prototype == null || button.Prototype == null)
            return;

        Select(button.Prototype);
    }

    private void Select(MappingPrototype mapping, bool revealInTree = true, Decal? sampledDecal = null)
    {
        if (_window == null || mapping.Prototype == null)
            return;

        var prototype = mapping.Prototype;
        _window.EntityContainer.Visible = prototype is EntityPrototype;
        _window.DecalContainer.Visible = prototype is DecalPrototype;
        _window.EraseEntityButton.Pressed = false;
        _window.EraseDecalButton.Pressed = false;
        _updateEraseDecal = false;

        if (_placement.Eraser)
            _placement.ToggleEraser();

        var mode = prototype is EntityPrototype
            ? MappingPalettePlacement.ModeName(_window.EntityPlacementMode.SelectedId)
            : null;
        MappingPalettePlacement.Begin(_placement, _decals, prototype, mode, sampledDecal);

        if (prototype is DecalPrototype)
            _window.SelectDecal(prototype.ID, sampledDecal);

        var name = prototype switch
        {
            EntityPrototype entity => entity.Name,
            ContentTileDefinition tile => Loc.GetString(tile.Name),
            _ => prototype.ID
        };
        _window.Prototypes.SetSelectionInfo(name, prototype.ID);

        if (MappingPaletteCatalog.GetRef(prototype) is { } id)
        {
            _session!.Memory.PushRecent(id);
            _window.Prototypes.RefreshRecents(_session.ResolveRecents());
        }

        if (revealInTree &&
            _window.Prototypes.PrototypeList.Visible &&
            (_window.Prototypes.NestedChildren || mapping.PaletteGroup != null) &&
            (mapping.PaletteGroup == null || !_window.Prototypes.IsLargeGroup(mapping.PaletteGroup)))
        {
            RevealInTree(mapping);
        }
    }

    private void RevealInTree(MappingPrototype mapping)
    {
        if (_window == null)
            return;

        var chain = new Stack<MappingPrototype>();
        chain.Push(mapping);
        if (_window.Prototypes.NestedChildren)
        {
            var parent = mapping.Parents?.FirstOrDefault();
            while (parent != null)
            {
                chain.Push(parent);
                parent = parent.Parents?.FirstOrDefault();
            }
        }
        else if (mapping.PaletteGroup != null)
        {
            chain.Push(mapping.PaletteGroup);
        }

        var children = _window.Prototypes.PrototypeList.Children;
        foreach (var node in chain)
        {
            foreach (var child in children)
            {
                if (child is not MappingSpawnButton button || button.Prototype != node)
                    continue;

                if (_window.Prototypes.IsLargeGroup(node))
                    return;

                UnCollapse(button);
                children = button.ChildrenPrototypes.Children;
                button.Button.Pressed = node.Prototype != null;
                _window.Prototypes.Selected = button;
                break;
            }
        }
    }

    private void OnCollapseToggled(MappingSpawnButton button, ButtonToggledEventArgs args)
    {
        ToggleCollapse(button);
    }

    private void ToggleCollapse(MappingSpawnButton button)
    {
        if (_window == null)
            return;

        if (button.CollapseButton.Pressed)
        {
            if (button.Prototype != null)
                _window.Prototypes.InsertChildren(button.ChildrenPrototypes, button.Prototype, _window.Prototypes.NestedChildren);

            button.CollapseButton.Label.Text = "▼";
        }
        else
        {
            button.ChildrenPrototypes.DisposeAllChildren();
            button.CollapseButton.Label.Text = "▶";
        }
    }

    private void Collapse(MappingSpawnButton button)
    {
        if (!button.CollapseButton.Pressed)
            return;

        button.CollapseButton.Pressed = false;
        ToggleCollapse(button);
    }

    private void UnCollapse(MappingSpawnButton button)
    {
        if (button.CollapseButton.Pressed)
            return;

        button.CollapseButton.Pressed = true;
        ToggleCollapse(button);
    }

    private void OnSearch(LineEditEventArgs args)
    {
        RefreshView();
    }

    private void OnFavoriteToggled(MappingPrototype mapping, bool pressed)
    {
        if (_session == null || mapping.Prototype == null ||
            MappingPaletteCatalog.GetRef(mapping.Prototype) is not { } id)
        {
            return;
        }

        if (_session.Memory.IsFavorite(id) != pressed)
            _session.Memory.ToggleFavorite(id);
    }

    private void OnEntityPlacementSelected(ItemSelectedEventArgs args)
    {
        if (_window == null)
            return;

        _window.EntityPlacementMode.SelectId(args.Id);
        if (_placement.CurrentMode == null || _placement.CurrentPermission == null)
            return;

        _placement.BeginPlacing(new PlacementInformation
        {
            PlacementOption = EntitySpawnWindow.InitOpts[args.Id],
            EntityType = _placement.CurrentPermission.EntityType,
            TileType = _placement.CurrentPermission.TileType,
            Range = 2,
            IsTile = _placement.CurrentPermission.IsTile
        });
    }

    private void OnEraseEntity(ButtonEventArgs args)
    {
        if (_window == null)
            return;

        if (args.Button.Pressed == _placement.Eraser)
            return;

        _placement.Clear();
        _decals.SetActive(false);
        _window.DecalContainer.Visible = false;
        _window.EntityContainer.Visible = false;
        _window.EraseDecalButton.Pressed = false;
        _updateEraseDecal = false;

        if (args.Button.Pressed && !_placement.Eraser)
            _placement.ToggleEraser();
        else if (!args.Button.Pressed && _placement.Eraser)
            _placement.ToggleEraser();
    }

    private void OnEraseDecal(ButtonToggledEventArgs args)
    {
        if (_window == null)
            return;

        _placement.Clear();
        if (_placement.Eraser)
            _placement.ToggleEraser();

        _window.EraseEntityButton.Pressed = false;
        _window.EntityContainer.Visible = false;
        _decals.SetActive(false);
        _updateEraseDecal = args.Pressed;
        _window.DecalContainer.Visible = false;
    }

    private void ClearPlacement()
    {
        _decals ??= _entities.System<DecalPlacementSystem>();
        _placement.Clear();
        _decals.SetActive(false);
        _updateEraseDecal = false;

        if (_window == null || _window.Disposed)
            return;

        _window.EraseEntityButton.Pressed = false;
        _window.EraseDecalButton.Pressed = false;
        _window.EntityContainer.Visible = false;
        _window.DecalContainer.Visible = false;
        _window.Prototypes.SetSelectionInfo(null, null);
        if (_window.Prototypes.Selected is { } selected)
        {
            selected.Button.Pressed = false;
            _window.Prototypes.Selected = null;
        }
    }

    private bool HandleUnselect(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (!_timing.IsFirstTimePredicted)
            return false;

        if (!_admin.CanAdminPlace() && !_sandbox.SandboxAllowed)
            return false;

        _decals ??= _entities.System<DecalPlacementSystem>();

        if (UIManager.CurrentlyHovered is not IViewportControl)
            return false;

        if (!_placement.IsActive && !_decals.IsActive && !_placement.Eraser && !_updateEraseDecal)
            return false;

        ClearPlacement();
        return true;
    }

    private bool HandleEyedropper(ICommonSession? session, EntityCoordinates coords, EntityUid uid, MappingEyedropperMode mode)
    {
        if (!_timing.IsFirstTimePredicted)
            return false;

        if (!_admin.CanAdminPlace() && !_sandbox.SandboxAllowed)
            return false;

        _decals ??= _entities.System<DecalPlacementSystem>();
        _eyedropper ??= _entities.System<MappingEyedropperSystem>();

        if (_updateEraseDecal)
            return false;

        // canFocus: true still fires this for RMB on the palette itself (EntId=0).
        if (UIManager.CurrentlyHovered is not IViewportControl)
            return false;

        if (!_eyedropper.TryPick(coords, uid, mode, out var prototype, out var sampledDecal) || prototype == null)
            return false;

        if (mode != MappingEyedropperMode.Tile && uid.IsValid() &&
            _eyedropper.GetEntityDirection(uid) is { } dir)
        {
            _placement.Direction = dir;
        }

        EnsureWindow();
        Reveal(prototype, sampledDecal);
        return true;
    }

    private bool HandleEraseDecal(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (!_updateEraseDecal)
            return false;

        _entities.EntityNetManager?.SendSystemNetworkMessage(
            new RequestDecalRemovalEvent(_entities.GetNetCoordinates(coords)));
        return true;
    }
}
