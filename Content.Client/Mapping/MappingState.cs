using System.Linq;
using System.Numerics;
using Content.Client._Forge.Mapping; // Forge-Change
using Content.Client.Administration.Managers;
using Content.Client.ContextMenu.UI;
using Content.Client.Decals;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Client.Verbs;
using Content.Shared.Administration;
using Content.Shared.Decals;
using Content.Shared.Input;
using Content.Shared.Maps;
using Robust.Shared.Configuration; // Forge-Change
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Placement;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Enums;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using static System.StringComparison;
using static Robust.Client.UserInterface.Controls.BaseButton;
using static Robust.Client.UserInterface.Controls.LineEdit;
using static Robust.Client.UserInterface.Controls.OptionButton;
using static Robust.Shared.Input.Binding.PointerInputCmdHandler;

namespace Content.Client.Mapping;

public sealed partial class MappingState : GameplayStateBase
{
    [Dependency] private IClientAdminManager _admin = default!;
    [Dependency] private IConfigurationManager _cfg = default!; // Forge-Change
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IEntityNetworkManager _entityNetwork = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private ILogManager _log = default!;
    [Dependency] private SharedMapSystem _mapMan = default!;
    [Dependency] private MappingManager _mapping = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlacementManager _placement = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private IGameTiming _timing = default!;

    private EntityMenuUIController _entityMenuController = default!;

    private DecalPlacementSystem _decal = default!;
    private SpriteSystem _sprite = default!;
    private TransformSystem _transform = default!;
    private VerbSystem _verbs = default!;
    private MappingEyedropperSystem _eyedropper = default!; // Forge-Change
    private MappingPaletteSession _palette = default!; // Forge-Change

    private readonly ISawmill _sawmill;
    private readonly GameplayStateLoadController _loadController;
    private bool _setup;
    private readonly List<MappingPrototype> _allPrototypes = new();
    private readonly Dictionary<IPrototype, MappingPrototype> _allPrototypesDict = new();
    private readonly Dictionary<Type, Dictionary<string, MappingPrototype>> _idDict = new();
    private readonly List<MappingPrototype> _prototypes = new();
    private (TimeSpan At, MappingSpawnButton Button)? _lastClicked;
    private Control? _scrollTo;
    private bool _updatePlacement;
    private bool _updateEraseDecal;

    private MappingScreen Screen => (MappingScreen) UserInterfaceManager.ActiveScreen!;
    private MainViewport Viewport => UserInterfaceManager.ActiveScreen!.GetWidget<MainViewport>()!;

    public CursorState State { get; set; }

    public MappingState()
    {
        IoCManager.InjectDependencies(this);

        _sawmill = _log.GetSawmill("mapping");
        _loadController = UserInterfaceManager.GetUIController<GameplayStateLoadController>();
    }

    protected override void Startup()
    {
        EnsureSetup();
        base.Startup();

        UserInterfaceManager.LoadScreen<MappingScreen>();
        _loadController.LoadScreen();

        var context = _input.Contexts.GetContext("common");
        context.AddFunction(ContentKeyFunctions.SaveMap);
        context.AddFunction(ContentKeyFunctions.MappingEnablePick);
        context.AddFunction(ContentKeyFunctions.MappingEnableDelete);
        context.AddFunction(ContentKeyFunctions.MappingPick);
        context.AddFunction(ContentKeyFunctions.MappingRemoveDecal);
        context.AddFunction(ContentKeyFunctions.MappingCancelEraseDecal);
        context.AddFunction(ContentKeyFunctions.MappingOpenContextMenu);

        Screen.DecalSystem = _decal;
        Screen.Prototypes.SearchBar.OnTextChanged += OnSearch;
        Screen.Prototypes.CollapseAllButton.OnPressed += OnCollapseAll;
        Screen.Prototypes.ClearSearchButton.OnPressed += OnClearSearch;
        Screen.Prototypes.ClearPlacementButton.OnPressed += OnClearPlacement; // Forge-Change
        Screen.Prototypes.GetPrototypeData += OnGetData;
        Screen.Prototypes.SelectionChanged += OnSelected;
        Screen.Prototypes.CollapseToggled += OnCollapseToggled;
        Screen.Prototypes.TabChanged += OnPaletteTabChanged; // Forge-Change
        Screen.Prototypes.FavoriteToggled += OnPaletteFavoriteToggled; // Forge-Change
        Screen.Prototypes.RecentClicked += OnPaletteRecentClicked; // Forge-Change
        Screen.Pick.OnPressed += OnPickPressed;
        Screen.Delete.OnPressed += OnDeletePressed;
        Screen.EntityReplaceButton.OnToggled += OnEntityReplacePressed;
        Screen.EntityPlacementMode.OnItemSelected += OnEntityPlacementSelected;
        Screen.EraseEntityButton.OnToggled += OnEraseEntityPressed;
        Screen.EraseDecalButton.OnToggled += OnEraseDecalPressed;
        _placement.PlacementChanged += OnPlacementChanged;

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.MappingUnselect, new PointerInputCmdHandler(HandleMappingUnselect, outsidePrediction: true))
            .Bind(ContentKeyFunctions.SaveMap, new PointerInputCmdHandler(HandleSaveMap, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingEnablePick, new PointerStateInputCmdHandler(HandleEnablePick, HandleDisablePick, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingEnableDelete, new PointerStateInputCmdHandler(HandleEnableDelete, HandleDisableDelete, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingPick, new PointerInputCmdHandler(HandlePick, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingRemoveDecal, new PointerInputCmdHandler(HandleEditorCancelPlace, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingCancelEraseDecal, new PointerInputCmdHandler(HandleCancelEraseDecal, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingOpenContextMenu, new PointerInputCmdHandler(HandleOpenContextMenu, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingEyedropper, new PointerInputCmdHandler((s, c, u) => HandleEyedropper(s, c, u, MappingEyedropperMode.Auto), outsidePrediction: true)) // Forge-Change
            .Bind(ContentKeyFunctions.MappingEyedropperTile, new PointerInputCmdHandler((s, c, u) => HandleEyedropper(s, c, u, MappingEyedropperMode.Tile), outsidePrediction: true)) // Forge-Change
            .Bind(ContentKeyFunctions.MappingEyedropperDecal, new PointerInputCmdHandler((s, c, u) => HandleEyedropper(s, c, u, MappingEyedropperMode.Decal), outsidePrediction: true)) // Forge-Change
            .Register<MappingState>();

        _overlays.AddOverlay(new MappingOverlay(this));

        _prototypeManager.PrototypesReloaded += OnPrototypesReloaded;

        Screen.Prototypes.UpdateVisible(_palette.RootsFor(Screen.Prototypes.CurrentTab));
        Screen.Prototypes.SetFavoritePredicate(_palette.IsFavorite);
        RefreshPaletteChrome();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs obj)
    {
        if (!obj.WasModified<EntityPrototype>() &&
            !obj.WasModified<ContentTileDefinition>() &&
            !obj.WasModified<DecalPrototype>())
        {
            return;
        }

        ReloadPrototypes();
    }

    private bool HandleOpenContextMenu(in PointerInputCmdArgs args)
    {
        Deselect();

        var coords = args.Coordinates.ToMap(_entityManager, _transform);
        if (_verbs.TryGetEntityMenuEntities(coords, out var entities))
            _entityMenuController.OpenRootMenu(entities);

        return true;
    }

    protected override void Shutdown()
    {
        CommandBinds.Unregister<MappingState>();

        Screen.Prototypes.SearchBar.OnTextChanged -= OnSearch;
        Screen.Prototypes.CollapseAllButton.OnPressed -= OnCollapseAll;
        Screen.Prototypes.ClearSearchButton.OnPressed -= OnClearSearch;
        Screen.Prototypes.ClearPlacementButton.OnPressed -= OnClearPlacement; // Forge-Change
        Screen.Prototypes.GetPrototypeData -= OnGetData;
        Screen.Prototypes.SelectionChanged -= OnSelected;
        Screen.Prototypes.CollapseToggled -= OnCollapseToggled;
        Screen.Prototypes.TabChanged -= OnPaletteTabChanged; // Forge-Change
        Screen.Prototypes.FavoriteToggled -= OnPaletteFavoriteToggled; // Forge-Change
        Screen.Prototypes.RecentClicked -= OnPaletteRecentClicked; // Forge-Change
        Screen.Pick.OnPressed -= OnPickPressed;
        Screen.Delete.OnPressed -= OnDeletePressed;
        Screen.EntityReplaceButton.OnToggled -= OnEntityReplacePressed;
        Screen.EntityPlacementMode.OnItemSelected -= OnEntityPlacementSelected;
        Screen.EraseEntityButton.OnToggled -= OnEraseEntityPressed;
        Screen.EraseDecalButton.OnToggled -= OnEraseDecalPressed;
        _placement.PlacementChanged -= OnPlacementChanged;
        _prototypeManager.PrototypesReloaded -= OnPrototypesReloaded;

        UserInterfaceManager.ClearWindows();
        _loadController.UnloadScreen();
        UserInterfaceManager.UnloadScreen();

        var context = _input.Contexts.GetContext("common");
        context.RemoveFunction(ContentKeyFunctions.SaveMap);
        context.RemoveFunction(ContentKeyFunctions.MappingEnablePick);
        context.RemoveFunction(ContentKeyFunctions.MappingEnableDelete);
        context.RemoveFunction(ContentKeyFunctions.MappingPick);
        context.RemoveFunction(ContentKeyFunctions.MappingRemoveDecal);
        context.RemoveFunction(ContentKeyFunctions.MappingCancelEraseDecal);
        context.RemoveFunction(ContentKeyFunctions.MappingOpenContextMenu);

        _overlays.RemoveOverlay<MappingOverlay>();

        base.Shutdown();
    }

    private void EnsureSetup()
    {
        if (_setup)
            return;

        _setup = true;

        _entityMenuController = UserInterfaceManager.GetUIController<EntityMenuUIController>();

        _decal = _entityManager.System<DecalPlacementSystem>();
        _sprite = _entityManager.System<SpriteSystem>();
        _transform = _entityManager.System<TransformSystem>();
        _verbs = _entityManager.System<VerbSystem>();
        _eyedropper = _entityManager.System<MappingEyedropperSystem>(); // Forge-Change
        _palette = new MappingPaletteSession(new MappingPaletteMemory(_cfg)); // Forge-Change
        ReloadPrototypes();
    }

    private void ReloadPrototypes()
    {
        // Forge-Change-Start: rebuild from scratch so hot-reload doesn't duplicate the tree.
        _allPrototypes.Clear();
        _allPrototypesDict.Clear();
        _idDict.Clear();
        _prototypes.Clear();
        _palette.Reset();
        // Forge-Change-End

        var entities = new MappingPrototype(null, Loc.GetString("mapping-entities")) { Children = new List<MappingPrototype>() };
        _prototypes.Add(entities);

        var mappings = new Dictionary<string, MappingPrototype>();
        foreach (var entity in _prototypeManager.EnumeratePrototypes<EntityPrototype>())
        {
            Register(entity, entity.ID, entities);
        }

        Sort(mappings, entities);
        mappings.Clear();

        var tiles = new MappingPrototype(null, Loc.GetString("mapping-tiles")) { Children = new List<MappingPrototype>() };
        _prototypes.Add(tiles);

        foreach (var tile in _prototypeManager.EnumeratePrototypes<ContentTileDefinition>())
        {
            Register(tile, tile.ID, tiles);
        }

        Sort(mappings, tiles);
        mappings.Clear();

        var decals = new MappingPrototype(null, Loc.GetString("mapping-decals")) { Children = new List<MappingPrototype>() };
        _prototypes.Add(decals);

        foreach (var decal in _prototypeManager.EnumeratePrototypes<DecalPrototype>())
        {
            Register(decal, decal.ID, decals);
        }

        Sort(mappings, decals);
        mappings.Clear();

        // Forge-Change-Start
        _palette.HierarchyRoots.AddRange(_prototypes);
        _palette.BuildGroups(_prototypeManager);
        if (UserInterfaceManager.ActiveScreen is MappingScreen)
            RefreshPaletteView();
        // Forge-Change-End
    }

    private void Sort(Dictionary<string, MappingPrototype> prototypes, MappingPrototype topLevel)
    {
        static int Compare(MappingPrototype a, MappingPrototype b)
        {
            return string.Compare(a.Name, b.Name, OrdinalIgnoreCase);
        }

        topLevel.Children ??= new List<MappingPrototype>();

        foreach (var prototype in prototypes.Values)
        {
            if (prototype.Parents == null && prototype != topLevel)
            {
                prototype.Parents = new List<MappingPrototype> { topLevel };
                topLevel.Children.Add(prototype);
            }

            prototype.Parents?.Sort(Compare);
            prototype.Children?.Sort(Compare);
        }

        topLevel.Children.Sort(Compare);
    }

    private MappingPrototype? Register<T>(T? prototype, string id, MappingPrototype topLevel) where T : class, IPrototype, IInheritingPrototype
    {
        {
            if (prototype == null &&
                _prototypeManager.TryIndex(id, out prototype) &&
                prototype is EntityPrototype entity)
            {
                if (entity.HideSpawnMenu || entity.Abstract)
                    prototype = null;
            }
        }

        if (prototype == null)
        {
            if (!_prototypeManager.TryGetMapping(typeof(T), id, out var node))
            {
                _sawmill.Error($"No {nameof(T)} found with id {id}");
                return null;
            }

            var ids = _idDict.GetOrNew(typeof(T));
            if (ids.TryGetValue(id, out var mapping))
            {
                return mapping;
            }
            else
            {
                var name = node.TryGet("name", out ValueDataNode? nameNode)
                    ? nameNode.Value
                    : id;

                if (node.TryGet("suffix", out ValueDataNode? suffix))
                    name = $"{name} [{suffix.Value}]";

                mapping = new MappingPrototype(prototype, name);
                _allPrototypes.Add(mapping);
                ids.Add(id, mapping);

                if (node.TryGet("parent", out ValueDataNode? parentValue))
                {
                    var parent = Register<T>(null, parentValue.Value, topLevel);

                    if (parent != null)
                    {
                        mapping.Parents ??= new List<MappingPrototype>();
                        mapping.Parents.Add(parent);
                        parent.Children ??= new List<MappingPrototype>();
                        parent.Children.Add(mapping);
                    }
                }
                else if (node.TryGet("parent", out SequenceDataNode? parentSequence))
                {
                    foreach (var parentNode in parentSequence.Cast<ValueDataNode>())
                    {
                        var parent = Register<T>(null, parentNode.Value, topLevel);

                        if (parent != null)
                        {
                            mapping.Parents ??= new List<MappingPrototype>();
                            mapping.Parents.Add(parent);
                            parent.Children ??= new List<MappingPrototype>();
                            parent.Children.Add(mapping);
                        }
                    }
                }
                else
                {
                    topLevel.Children ??= new List<MappingPrototype>();
                    topLevel.Children.Add(mapping);
                    mapping.Parents ??= new List<MappingPrototype>();
                    mapping.Parents.Add(topLevel);
                }

                return mapping;
            }
        }
        else
        {
            var ids = _idDict.GetOrNew(typeof(T));
            if (ids.TryGetValue(id, out var mapping))
            {
                return mapping;
            }
            else
            {
                var entity = prototype as EntityPrototype;
                var name = entity?.Name ?? prototype.ID;

                if (!string.IsNullOrWhiteSpace(entity?.EditorSuffix))
                    name = $"{name} [{entity.EditorSuffix}]";

                mapping = new MappingPrototype(prototype, name);
                _allPrototypes.Add(mapping);
                _allPrototypesDict.Add(prototype, mapping);
                ids.Add(prototype.ID, mapping);
            }

            if (prototype.Parents == null)
            {
                topLevel.Children ??= new List<MappingPrototype>();
                topLevel.Children.Add(mapping);
                mapping.Parents ??= new List<MappingPrototype>();
                mapping.Parents.Add(topLevel);
                return mapping;
            }

            foreach (var parentId in prototype.Parents)
            {
                var parent = Register<T>(null, parentId, topLevel);

                if (parent != null)
                {
                    mapping.Parents ??= new List<MappingPrototype>();
                    mapping.Parents.Add(parent);
                    parent.Children ??= new List<MappingPrototype>();
                    parent.Children.Add(mapping);
                }
            }

            return mapping;
        }
    }

    private void OnPlacementChanged(object? sender, EventArgs e)
    {
        _updatePlacement = true;
    }

    protected override void OnKeyBindStateChanged(ViewportBoundKeyEventArgs args)
    {
        if (args.Viewport == null)
            base.OnKeyBindStateChanged(new ViewportBoundKeyEventArgs(args.KeyEventArgs, Viewport.Viewport));
        else
            base.OnKeyBindStateChanged(args);
    }

    private void OnSearch(LineEditEventArgs args)
    {
        if (string.IsNullOrEmpty(args.Text))
        {
            RefreshPaletteView();
            return;
        }

        var matches = _palette.Search(args.Text, Screen.Prototypes.CurrentTab);
        Screen.Prototypes.Search(matches);
    }

    private void OnCollapseAll(ButtonEventArgs args)
    {
        foreach (var child in Screen.Prototypes.PrototypeList.Children)
        {
            if (child is not MappingSpawnButton button)
                continue;

            Collapse(button);
        }

        Screen.Prototypes.ScrollContainer.SetScrollValue(new Vector2(0, 0));
    }

    private void OnClearSearch(ButtonEventArgs obj)
    {
        Screen.Prototypes.SearchBar.Text = string.Empty;
        OnSearch(new LineEditEventArgs(Screen.Prototypes.SearchBar, string.Empty));
    }

    private void OnClearPlacement(ButtonEventArgs obj)
    {
        Deselect();
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

    private void OnSelected(MappingPrototype mapping)
    {
        if (mapping.Prototype == null)
            return;

        // Forge-Change-Start: search/grid already virtualizes; walking the tree would expand huge groups.
        if (!Screen.Prototypes.PrototypeList.Visible)
        {
            PlacePrototype(mapping.Prototype);
            return;
        }
        // Forge-Change-End

        var chain = new Stack<MappingPrototype>();
        chain.Push(mapping);

        // Forge-Change-Start: grouped tabs walk the palette folder, hierarchy still uses YAML parents.
        if (Screen.Prototypes.NestedChildren)
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
        // Forge-Change-End

        _lastClicked = null;

        Control? last = null;
        var children = Screen.Prototypes.PrototypeList.Children;
        foreach (var prototype in chain)
        {
            foreach (var child in children)
            {
                if (child is MappingSpawnButton button &&
                    button.Prototype == prototype)
                {
                    // Forge-Change: never inline-expand folders with thousands of children.
                    if (Screen.Prototypes.IsLargeGroup(prototype))
                    {
                        PlacePrototype(mapping.Prototype);
                        return;
                    }

                    UnCollapse(button);
                    OnSelected(button, prototype.Prototype);
                    children = button.ChildrenPrototypes.Children;
                    last = child;
                    break;
                }
            }
        }

        if (last != null && Screen.Prototypes.PrototypeList.Visible)
            _scrollTo = last;
        else if (mapping.Prototype != null) // Forge-Change: still place when the tree isn't on-screen (grid/search).
            PlacePrototype(mapping.Prototype);
    }

    private void OnSelected(MappingSpawnButton button, IPrototype? prototype)
    {
        var time = _timing.CurTime;
        if (prototype is DecalPrototype)
            Screen.SelectDecal(prototype.ID);

        // Double-click functionality if it's collapsible.
        if (_lastClicked is { } lastClicked &&
            lastClicked.Button == button &&
            lastClicked.At > time - TimeSpan.FromSeconds(0.333) &&
            string.IsNullOrEmpty(Screen.Prototypes.SearchBar.Text) &&
            button.CollapseButton.Visible)
        {
            button.CollapseButton.Pressed = !button.CollapseButton.Pressed;
            ToggleCollapse(button);
            button.Button.Pressed = true;
            Screen.Prototypes.Selected = button;
            _lastClicked = null;
            return;
        }

        // Toggle if it's the same button (at least if we just unclicked it).
        if (!button.Button.Pressed && button.Prototype?.Prototype != null && _lastClicked?.Button == button)
        {
            _lastClicked = null;
            Deselect();
            return;
        }

        _lastClicked = (time, button);

        if (button.Prototype == null)
            return;

        if (Screen.Prototypes.Selected is { } oldButton &&
            oldButton != button)
        {
            Deselect();
        }

        Screen.EntityContainer.Visible = false;
        Screen.DecalContainer.Visible = false;

        if (prototype != null)
            PlacePrototype(prototype);

        Screen.Prototypes.Selected = button;

        button.Button.Pressed = true;
    }

    private void Deselect()
    {
        if (Screen.Prototypes.Selected is { } selected)
        {
            selected.Button.Pressed = false;
            Screen.Prototypes.Selected = null;
        }

        Screen.Prototypes.SetSelectionInfo(null, null); // Forge-Change
        Screen.EntityContainer.Visible = false;
        Screen.DecalContainer.Visible = false;
        Screen.EraseEntityButton.Pressed = false;
        Screen.EraseDecalButton.Pressed = false;
        _updateEraseDecal = false;
        _decal.SetActive(false);
        _placement.Clear();
    }

    private void OnCollapseToggled(MappingSpawnButton button, ButtonToggledEventArgs args)
    {
        ToggleCollapse(button);
    }

    private void OnPickPressed(ButtonEventArgs args)
    {
        if (args.Button.Pressed)
            EnablePick();
        else
            DisablePick();
    }

    private void OnDeletePressed(ButtonEventArgs obj)
    {
        if (obj.Button.Pressed)
            EnableDelete();
        else
            DisableDelete();
    }

    private void OnEntityReplacePressed(ButtonToggledEventArgs args)
    {
        _placement.Replacement = args.Pressed;
    }

    private void OnEntityPlacementSelected(ItemSelectedEventArgs args)
    {
        Screen.EntityPlacementMode.SelectId(args.Id);

        if (_placement.CurrentMode != null)
        {
            var placement = new PlacementInformation
            {
                PlacementOption = EntitySpawnWindow.InitOpts[args.Id],
                EntityType = _placement.CurrentPermission!.EntityType,
                TileType = _placement.CurrentPermission.TileType,
                Range = 2,
                IsTile = _placement.CurrentPermission.IsTile,
            };

            _placement.BeginPlacing(placement);
        }
    }

    private void OnEraseEntityPressed(ButtonEventArgs args)
    {
        if (args.Button.Pressed == _placement.Eraser)
            return;

        if (args.Button.Pressed)
            EnableEraser();
        else
            DisableEraser();
    }

    private void OnEraseDecalPressed(ButtonToggledEventArgs args)
    {
        _placement.Clear();
        Deselect();
        Screen.EraseEntityButton.Pressed = false;
        _updatePlacement = true;
        _updateEraseDecal = args.Pressed;
    }

    private void EnableEraser()
    {
        if (_placement.Eraser)
            return;

        _placement.Clear();
        _placement.ToggleEraser();
        Screen.EntityPlacementMode.Disabled = true;
        Screen.EraseDecalButton.Pressed = false;
        Deselect();
    }

    private void DisableEraser()
    {
        if (!_placement.Eraser)
            return;

        _placement.ToggleEraser();
        Screen.EntityPlacementMode.Disabled = false;
    }

    private void EnablePick()
    {
        Screen.UnPressActionsExcept(Screen.Pick);
        State = CursorState.Pick;
    }

    private void DisablePick()
    {
        Screen.Pick.Pressed = false;
        State = CursorState.None;
    }

    private void EnableDelete()
    {
        Screen.UnPressActionsExcept(Screen.Delete);
        State = CursorState.Delete;
        EnableEraser();
    }

    private void DisableDelete()
    {
        Screen.Delete.Pressed = false;
        State = CursorState.None;
        DisableEraser();
    }

    private bool HandleMappingUnselect(in PointerInputCmdArgs args)
    {
        // Forge-Change: RMB cancels spawn; Shift+RMB is the eyedropper.
        if (!_timing.IsFirstTimePredicted)
            return false;

        if (UserInterfaceManager.CurrentlyHovered is not IViewportControl)
            return false;

        if (!_placement.IsActive &&
            _decal.GetActiveDecal().Decal is null &&
            !_placement.Eraser &&
            !_updateEraseDecal)
        {
            return false;
        }

        Deselect();
        return true;
    }

    private bool HandleSaveMap(in PointerInputCmdArgs args)
    {
#if FULL_RELEASE
        return false;
#endif
        if (!_admin.IsAdmin(true) || !_admin.HasFlag(AdminFlags.Host))
            return false;

        SaveMap();
        return true;
    }

    private bool HandleEnablePick(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        EnablePick();
        return true;
    }

    private bool HandleDisablePick(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        DisablePick();
        return true;
    }

    private bool HandleEnableDelete(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        EnableDelete();
        return true;
    }

    private bool HandleDisableDelete(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        DisableDelete();
        return true;
    }

    private bool HandlePick(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (State != CursorState.Pick)
            return false;

        return ApplyEyedropper(coords, uid, MappingEyedropperMode.Auto, deselectOnMiss: true); // Forge-Change
    }

    private bool HandleEditorCancelPlace(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (!Screen.EraseDecalButton.Pressed)
            return false;

        _entityNetwork.SendSystemNetworkMessage(new RequestDecalRemovalEvent(_entityManager.GetNetCoordinates(coords)));
        return true;
    }

    private bool HandleCancelEraseDecal(in PointerInputCmdArgs args)
    {
        if (!Screen.EraseDecalButton.Pressed)
            return false;

        Screen.EraseDecalButton.Pressed = false;
        return true;
    }

    private async void SaveMap()
    {
        await _mapping.SaveMap();
    }

    private void ToggleCollapse(MappingSpawnButton button)
    {
        if (button.CollapseButton.Pressed)
        {
            if (button.Prototype != null)
                Screen.Prototypes.InsertChildren(button.ChildrenPrototypes, button.Prototype, Screen.Prototypes.NestedChildren); // Forge-Change

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

    public EntityUid? GetHoveredEntity()
    {
        if (UserInterfaceManager.CurrentlyHovered is not IViewportControl viewport ||
            _input.MouseScreenPosition is not { IsValid: true } position)
        {
            return null;
        }

        var mapPos = viewport.PixelToMap(position.Position);
        return GetClickedEntity(mapPos);
    }

    public override void FrameUpdate(FrameEventArgs e)
    {
        if (_updatePlacement)
        {
            _updatePlacement = false;

            if (!_placement.IsActive && _decal.GetActiveDecal().Decal == null)
                Deselect();

            Screen.EraseEntityButton.Pressed = _placement.Eraser;
            Screen.EraseDecalButton.Pressed = _updateEraseDecal;
            Screen.EntityPlacementMode.Disabled = _placement.Eraser;
        }

        if (_scrollTo is not { } scrollTo)
            return;

        // this is not ideal but we wait until the control's height is computed to use
        // its position to scroll to
        if (scrollTo.Height > 0 && Screen.Prototypes.PrototypeList.Visible)
        {
            var y = scrollTo.GlobalPosition.Y - Screen.Prototypes.ScrollContainer.Height / 2 + scrollTo.Height;
            var scroll = Screen.Prototypes.ScrollContainer;
            scroll.SetScrollValue(scroll.GetScrollValue() + new Vector2(0, y));
            _scrollTo = null;
        }
    }


    // TODO this doesn't handle pressing down multiple state hotkeys at the moment
    public enum CursorState
    {
        None,
        Pick,
        Delete
    }

    // Forge-Change-Start
    private void PlacePrototype(IPrototype prototype, Decal? sampledDecal = null)
    {
        Screen.EntityContainer.Visible = prototype is EntityPrototype;
        Screen.DecalContainer.Visible = prototype is DecalPrototype;

        var mode = prototype is EntityPrototype
            ? MappingPalettePlacement.ModeName(Screen.EntityPlacementMode.SelectedId)
            : null;
        MappingPalettePlacement.Begin(_placement, _decal, prototype, mode, sampledDecal);

        if (prototype is DecalPrototype)
            Screen.SelectDecal(prototype.ID, sampledDecal);

        var name = prototype switch
        {
            EntityPrototype entity => entity.Name,
            ContentTileDefinition tile => Loc.GetString(tile.Name),
            _ => prototype.ID
        };
        Screen.Prototypes.SetSelectionInfo(name, prototype.ID);

        if (MappingPaletteCatalog.GetRef(prototype) is { } id)
        {
            _palette.Memory.PushRecent(id);
            RefreshPaletteChrome();
        }
    }

    private void RefreshPaletteView()
    {
        var tab = Screen.Prototypes.CurrentTab;
        if (Screen.Prototypes.GridMode && tab is MappingPaletteTab.Tiles or MappingPaletteTab.Decals)
        {
            var query = Screen.Prototypes.SearchBar.Text;
            var items = string.IsNullOrEmpty(query)
                ? _palette.FlatFor(tab)
                : _palette.Search(query, tab);
            Screen.Prototypes.PopulateGrid(items);
        }
        else
        {
            Screen.Prototypes.UpdateVisible(_palette.RootsFor(tab));
        }

        RefreshPaletteChrome();
    }

    private void RefreshPaletteChrome()
    {
        Screen.Prototypes.SetFavoritePredicate(_palette.IsFavorite);
        Screen.Prototypes.RefreshRecents(_palette.ResolveRecents());
    }

    private void OnPaletteTabChanged(MappingPaletteTab tab)
    {
        if (string.IsNullOrEmpty(Screen.Prototypes.SearchBar.Text) || Screen.Prototypes.GridMode)
            RefreshPaletteView();
        else
            OnSearch(new LineEditEventArgs(Screen.Prototypes.SearchBar, Screen.Prototypes.SearchBar.Text));
    }

    private void OnPaletteFavoriteToggled(MappingPrototype mapping, bool pressed)
    {
        if (mapping.Prototype == null || MappingPaletteCatalog.GetRef(mapping.Prototype) is not { } id)
            return;

        if (_palette.Memory.IsFavorite(id) != pressed)
            _palette.Memory.ToggleFavorite(id);
    }

    private void OnPaletteRecentClicked(MappingPrototype mapping)
    {
        if (mapping.Prototype == null)
            return;

        RevealAndSelect(mapping);
    }

    private bool HandleEyedropper(ICommonSession? session, EntityCoordinates coords, EntityUid uid, MappingEyedropperMode mode)
    {
        if (State == CursorState.Delete)
            return false;

        return ApplyEyedropper(coords, uid, mode, deselectOnMiss: false);
    }

    private bool ApplyEyedropper(EntityCoordinates coords, EntityUid uid, MappingEyedropperMode mode, bool deselectOnMiss)
    {
        // Predicted replays re-run this with EntId=0; skip before ToMapCoordinates.
        if (!_timing.IsFirstTimePredicted)
            return false;

        if (UserInterfaceManager.CurrentlyHovered is not IViewportControl)
            return false;

        if (!_eyedropper.TryPick(coords, uid, mode, out var prototype, out var sampledDecal) || prototype == null)
        {
            if (!deselectOnMiss)
                return false;

            Deselect();
            return true;
        }

        DisablePick();

        if (!_allPrototypesDict.TryGetValue(prototype, out var mapping))
        {
            PlacePrototype(prototype, sampledDecal);
            return true;
        }

        if (mode != MappingEyedropperMode.Tile && uid.IsValid() &&
            _eyedropper.GetEntityDirection(uid) is { } dir)
        {
            _placement.Direction = dir;
        }

        RevealAndSelect(mapping, sampledDecal);
        return true;
    }

    private void RevealAndSelect(MappingPrototype mapping, Decal? sampledDecal = null)
    {
        if (mapping.Prototype == null)
            return;

        var tab = MappingPaletteCatalog.TabFor(mapping.Prototype);
        if (Screen.Prototypes.CurrentTab is not (MappingPaletteTab.Favorites or MappingPaletteTab.Recents or MappingPaletteTab.Hierarchy))
            Screen.Prototypes.SetTab(tab, invoke: false);

        // Search-by-ID uses the virtualized list instead of expanding huge folders.
        Screen.Prototypes.SearchBar.Text = mapping.Prototype.ID;
        OnSearch(new LineEditEventArgs(Screen.Prototypes.SearchBar, mapping.Prototype.ID));
        PlacePrototype(mapping.Prototype, sampledDecal);
    }
    // Forge-Change-End
}
